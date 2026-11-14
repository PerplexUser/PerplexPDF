using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using static PerplexPDF.Infrastructure.Pdf.PdfiumNative;

namespace PerplexPDF.Infrastructure.Pdf;

public sealed partial class PdfiumRenderer(string libraryPath) : IPdfRenderEngine
{
    private readonly RasterCache _cache = new();

    public Task<IPdfDocument> OpenAsync(string path, string? password, CancellationToken ct) => Task.Run<IPdfDocument>(() =>
    {
        lock (PdfiumNative.Sync)
        {
            ct.ThrowIfCancellationRequested();
            InitializeEngine();
            return PdfiumDocument.Open(path, password, _cache, ct);
        }
    }, ct);

    public Task CreateBlankPageAsync(PageGeometry size, string output, CancellationToken ct) => Task.Run(() =>
    {
        lock (PdfiumNative.Sync)
        {
            ct.ThrowIfCancellationRequested();
            InitializeEngine();
            var document = FPDF_CreateNewDocument();
            if (document == 0)
                throw new AppFailure("InvalidExport");
            try
            {
                var page = FPDFPage_New(document, 0, size.Width, size.Height);
                if (page == 0)
                    throw new AppFailure("InvalidExport");
                FPDF_ClosePage(page);
                SavePdf(document, output, "InvalidExport", ct);
            }
            finally { FPDF_CloseDocument(document); }
        }
    }, ct);

    private void InitializeEngine()
    {
        try { Initialize(libraryPath); }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new AppFailure("DependencyMissing", error);
        }
    }

    private readonly record struct RenderKey(Guid Document, int Index, int Rotation, int Width, int Height);

    private sealed class RasterCache
    {
        private const long Budget = 48L * 1024 * 1024;
        private readonly LinkedList<(RenderKey Key, RasterPage Page)> _order = new();
        private readonly Dictionary<RenderKey, LinkedListNode<(RenderKey Key, RasterPage Page)>> _items = [];
        private long _bytes;

        public RasterPage? Get(RenderKey key)
        {
            if (!_items.TryGetValue(key, out var node))
                return null;
            _order.Remove(node);
            _order.AddFirst(node);
            return node.Value.Page;
        }

        public void Add(RenderKey key, RasterPage page)
        {
            if (page.Bgra.Length > Budget)
                return;
            while (_order.Last is { } tail && _bytes + page.Bgra.Length > Budget)
                Remove(tail);
            _items.Add(key, _order.AddFirst((key, page)));
            _bytes += page.Bgra.Length;
        }

        public void DropDocument(Guid document)
        {
            foreach (var key in _items.Keys.Where(key => key.Document == document).ToArray())
                Remove(_items[key]);
        }

        private void Remove(LinkedListNode<(RenderKey Key, RasterPage Page)> node)
        {
            _bytes -= node.Value.Page.Bgra.Length;
            _items.Remove(node.Value.Key);
            _order.Remove(node);
        }
    }

    private sealed partial class PdfiumDocument : IPdfDocument, IAdvancedPdfEditEngine, IPdfPageLayoutSource
    {
        private const int CoordinateExtent = 100_000;
        private const int MaxMatches = 2_000;
        private readonly Guid _id = Guid.NewGuid();
        private readonly RasterCache _cache;
        private readonly MemoryMappedFile _file;
        private readonly MemoryMappedViewAccessor _view;
        private nint _document;

        private PdfiumDocument(RasterCache cache, MemoryMappedFile file, MemoryMappedViewAccessor view, nint document,
            IReadOnlyList<PageGeometry> pages, IReadOnlyList<PdfBookmark> bookmarks, PdfMetadata metadata)
        {
            _cache = cache;
            _file = file;
            _view = view;
            _document = document;
            Pages = pages;
            Bookmarks = bookmarks;
            Metadata = metadata;
        }

        public IReadOnlyList<PageGeometry> Pages { get; }
        public IReadOnlyList<PdfBookmark> Bookmarks { get; }
        public PdfMetadata Metadata { get; }

        public static unsafe PdfiumDocument Open(string path, string? password, RasterCache cache, CancellationToken ct)
        {
            var length = new FileInfo(path).Length;
            SafeFiles.CheckLength(length, DocumentProject.MaxSourceBytes);
            var file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            MemoryMappedViewAccessor? view = null;
            nint document = 0;
            byte* pointer = null;
            try
            {
                view = file.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
                document = FPDF_LoadMemDocument64((nint)(pointer + view.PointerOffset), (nuint)length, password);
                if (document == 0)
                    throw new AppFailure(FPDF_GetLastError() switch
                    {
                        4 => "PdfPassword",
                        5 => "UnsupportedPdfSecurity",
                        _ => "InvalidPdf"
                    });

                var count = FPDF_GetPageCount(document);
                if (count is < 1 or > DocumentProject.MaxPages)
                    throw new AppFailure("SizeLimit");
                var pages = new PageGeometry[count];
                for (var index = 0; index < count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (FPDF_GetPageSizeByIndex(document, index, out var width, out var height) == 0)
                        throw new AppFailure("InvalidPdf");
                    if (!double.IsFinite(width) || !double.IsFinite(height) || width is < 1 or > 20_000 ||
                        height is < 1 or > 20_000)
                        throw new AppFailure("UnsupportedPageSize");
                    pages[index] = new(width, height);
                }

                var seen = new HashSet<nint>();
                var bookmarks = ReadBookmarks(document, 0, count, 0, seen, ct);
                var metadata = new PdfMetadata(ReadMetadata(document, "Title"), ReadMetadata(document, "Author"),
                    ReadMetadata(document, "Keywords"), FPDF_GetSignatureCount(document) > 0);
                return new PdfiumDocument(cache, file, view, document, pages, bookmarks, metadata);
            }
            catch
            {
                if (document != 0)
                    FPDF_CloseDocument(document);
                if (pointer != null)
                    view!.SafeMemoryMappedViewHandle.ReleasePointer();
                view?.Dispose();
                file.Dispose();
                throw;
            }
        }

        private static IReadOnlyList<PdfBookmark> ReadBookmarks(nint document, nint parent, int pageCount, int depth,
            HashSet<nint> seen, CancellationToken ct)
        {
            var items = new List<PdfBookmark>();
            if (depth >= 32)
                return items;
            for (var bookmark = FPDFBookmark_GetFirstChild(document, parent);
                 bookmark != 0 && seen.Count < 4000 && seen.Add(bookmark);
                 bookmark = FPDFBookmark_GetNextSibling(document, bookmark))
            {
                ct.ThrowIfCancellationRequested();
                var size = FPDFBookmark_GetTitle(bookmark, null, 0);
                var title = "";
                if (size is > 1 and <= 8192)
                {
                    var bytes = new byte[(int)size];
                    FPDFBookmark_GetTitle(bookmark, bytes, size);
                    title = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
                }
                var destination = FPDFBookmark_GetDest(document, bookmark);
                if (destination == 0)
                {
                    var action = FPDFBookmark_GetAction(bookmark);
                    if (action != 0 && FPDFAction_GetType(action) == 1)
                        destination = FPDFAction_GetDest(document, action);
                }
                var index = destination == 0 ? -1 : FPDFDest_GetDestPageIndex(document, destination);
                items.Add(new PdfBookmark(title, index >= 0 && index < pageCount ? index : null,
                    ReadBookmarks(document, bookmark, pageCount, depth + 1, seen, ct)));
            }
            return items;
        }

        private static string ReadMetadata(nint document, string tag)
        {
            var size = FPDF_GetMetaText(document, tag, null, 0);
            if (size is <= 1 or > 8192)
                return "";
            var bytes = new byte[(int)size];
            FPDF_GetMetaText(document, tag, bytes, size);
            return Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }

        public Task<RasterPage> RenderAsync(PageReference page, double dpi, CancellationToken ct) => WithPageAsync(page, handle =>
        {
            var geometry = new PageTransform(Pages[page.SourcePage], page.Rotation).Display;
            if (!double.IsFinite(dpi) || dpi is < 6 or > 600)
                throw new AppFailure("RenderLimit");
            var width = Math.Max(1, (int)Math.Round(geometry.Width * dpi / 72));
            var height = Math.Max(1, (int)Math.Round(geometry.Height * dpi / 72));
            if ((long)width * height > 30_000_000 || width > 16_384 || height > 16_384)
                throw new AppFailure("RenderLimit");

            var key = new RenderKey(_id, page.SourcePage, page.Rotation, width, height);
            if (_cache.Get(key) is { } cached)
                return cached;
            var result = Render(handle, page.Rotation / 90, width, height);
            ct.ThrowIfCancellationRequested();
            _cache.Add(key, result);
            return result;
        }, ct);

        private static unsafe RasterPage Render(nint page, int rotate, int width, int height)
        {
            var buffer = GC.AllocateUninitializedArray<byte>(checked(width * height * 4));
            fixed (byte* pixels = buffer)
            {
                const int bgra = 4;
                const int renderAnnotations = 1;
                var bitmap = FPDFBitmap_CreateEx(width, height, bgra, (nint)pixels, width * 4);
                if (bitmap == 0)
                    throw new AppFailure("RenderFailed");
                try
                {
                    FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xffffffff);
                    FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, rotate, renderAnnotations);
                }
                finally { FPDFBitmap_Destroy(bitmap); }
            }
            return new(width, height, buffer);
        }

        public async Task<IReadOnlyList<TextHit>> SearchAsync(IReadOnlyList<PageReference> pages, string term, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(term))
                return [];
            if (term.Length > 256 || term.Contains('\0'))
                throw new AppFailure("SearchLimit");

            var result = new List<TextHit>();
            var query = Marshal.StringToHGlobalUni(term);
            try
            {
                foreach (var page in pages)
                {
                    ct.ThrowIfCancellationRequested();
                    var hits = await WithPageAsync(page, handle =>
                        Find(handle, page, query, MaxMatches - result.Count, ct), ct);
                    result.AddRange(hits);
                    if (result.Count >= MaxMatches)
                        break;
                }
                return result;
            }
            finally { Marshal.FreeHGlobal(query); }
        }

        private List<TextHit> Find(nint handle, PageReference page, nint query, int remaining, CancellationToken ct)
        {
            var result = new List<TextHit>();
            var text = FPDFText_LoadPage(handle);
            if (text == 0)
                return result;
            try
            {
                var search = FPDFText_FindStart(text, query, 0, 0);
                if (search == 0)
                    return result;
                try
                {
                    var geometry = Pages[page.SourcePage];
                    while (result.Count < remaining && FPDFText_FindNext(search) != 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        var count = FPDFText_CountRects(text, FPDFText_GetSchResultIndex(search), FPDFText_GetSchCount(search));
                        var bounds = new List<PdfRect>();
                        for (var index = 0; index < Math.Min(count, 512); index++)
                        {
                            if (FPDFText_GetRect(text, index, out var left, out var top, out var right, out var bottom) == 0)
                                continue;
                            bounds.Add(PdfRect.Between(FromPdf(handle, geometry, left, top),
                                FromPdf(handle, geometry, right, bottom)));
                        }
                        result.Add(new TextHit(page.Id, bounds));
                    }
                }
                finally { FPDFText_FindClose(search); }
            }
            finally { FPDFText_ClosePage(text); }
            return result;
        }

        private static PdfPoint FromPdf(nint page, PageGeometry geometry, double x, double y)
        {
            if (FPDF_PageToDevice(page, 0, 0, CoordinateExtent, CoordinateExtent, 0, x, y, out var dx, out var dy) == 0)
                throw new AppFailure("InvalidPdf");
            return new(Math.Clamp((double)dx / CoordinateExtent, 0, 1) * geometry.Width,
                (1 - Math.Clamp((double)dy / CoordinateExtent, 0, 1)) * geometry.Height);
        }

        private static PdfPoint ToPdf(nint page, PageGeometry geometry, PdfPoint point)
        {
            var dx = (int)Math.Round(point.X / geometry.Width * CoordinateExtent);
            var dy = (int)Math.Round((1 - point.Y / geometry.Height) * CoordinateExtent);
            if (FPDF_DeviceToPage(page, 0, 0, CoordinateExtent, CoordinateExtent, 0, dx, dy, out var x, out var y) == 0)
                throw new AppFailure("InvalidPdf");
            return new(x, y);
        }

        public Task<string> ReadTextAsync(PageReference page, PdfRect bounds, CancellationToken ct) =>
            WithPageAsync(page, handle => ReadBoundedText(handle, Pages[page.SourcePage], bounds, 100_000, truncate: true), ct);

        public Task<string> ReadPageTextAsync(PageReference page, CancellationToken ct) => WithPageAsync(page, handle =>
        {
            var geometry = Pages[page.SourcePage];
            // PDFium sorts text using the page rotation. Read in the content's original orientation.
            var rotation = FPDFPage_GetRotation(handle);
            if (rotation != 0)
                FPDFPage_SetRotation(handle, 0);
            try
            {
                return ReadBoundedText(handle, geometry, new(0, 0, geometry.Width, geometry.Height), 1_000_000, truncate: false);
            }
            finally
            {
                if (rotation != 0)
                    FPDFPage_SetRotation(handle, rotation);
            }
        }, ct);

        private static string ReadBoundedText(nint handle, PageGeometry geometry, PdfRect bounds, int limit, bool truncate)
        {
            var text = FPDFText_LoadPage(handle);
            if (text == 0)
                return "";
            try
            {
                var a = ToPdf(handle, geometry, new(bounds.X, bounds.Y));
                var b = ToPdf(handle, geometry, new(bounds.X + bounds.Width, bounds.Y + bounds.Height));
                var left = Math.Min(a.X, b.X);
                var right = Math.Max(a.X, b.X);
                var top = Math.Max(a.Y, b.Y);
                var bottom = Math.Min(a.Y, b.Y);
                var count = FPDFText_GetBoundedText(text, left, top, right, bottom, 0, 0);
                if (count <= 0)
                    return "";
                if (!truncate && count > limit)
                    throw new AppFailure("SizeLimit");
                var capacity = (int)Math.Min(count + 1L, limit + 1L);
                var buffer = Marshal.AllocHGlobal(capacity * 2);
                try
                {
                    var actual = FPDFText_GetBoundedText(text, left, top, right, bottom, buffer, capacity);
                    return actual <= 0 ? "" : Marshal.PtrToStringUni(buffer, Math.Min(actual, capacity))!.TrimEnd('\0');
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            finally { FPDFText_ClosePage(text); }
        }

        private Task<T> WithPageAsync<T>(PageReference reference, Func<nint, T> operation, CancellationToken ct) => Task.Run(() =>
        {
            lock (PdfiumNative.Sync)
            {
                ct.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(_document == 0, this);
                var page = FPDF_LoadPage(_document, reference.SourcePage);
                if (page == 0)
                    throw new AppFailure("InvalidPdf");
                try { return operation(page); }
                finally { FPDF_ClosePage(page); }
            }
        }, ct);

        public void Dispose()
        {
            lock (PdfiumNative.Sync)
            {
                if (_document == 0)
                    return;
                FPDF_CloseDocument(_document);
                _document = 0;
                _view.SafeMemoryMappedViewHandle.ReleasePointer();
                _view.Dispose();
                _file.Dispose();
                _cache.DropDocument(_id);
            }
        }
    }
}
