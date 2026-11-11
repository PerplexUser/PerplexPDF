using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using PerplexPDF.Domain;

namespace PerplexPDF.Application;

public sealed class OpenDocument(
    IWorkDirectory work, string source, string? original, string? projectPath,
    string? password, IPdfDocument pdf, DocumentProject project, bool isNew) : IDisposable
{
    public string Source { get; } = source;
    public string? Original { get; } = original;
    public string? ProjectPath { get; set; } = projectPath;
    public bool Encrypted => Password is not null;
    internal string? Password { get; } = password;
    public IPdfDocument Pdf { get; } = pdf;
    public ProjectHistory History { get; } = new(project, isNew);

    public void Dispose()
    {
        Pdf.Dispose();
        work.Dispose();
    }
}

public sealed partial class PdfWorkspace(
    IPdfRenderEngine renderer, IPdfStructureEngine structure, IPdfOverlayEngine overlays,
    IOcrEngine ocr, IProjectStore store, IWorkDirectories directories)
{
    public async Task<OpenDocument> OpenAsync(string path, string? password, bool recovered, CancellationToken ct)
    {
        path = Path.GetFullPath(path);
        var isProject = string.Equals(Path.GetExtension(path), ".ppdf", StringComparison.OrdinalIgnoreCase);
        if (!isProject && !string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new AppFailure("UnsupportedFile");

        var work = directories.Create();
        IPdfDocument? pdf = null;
        try
        {
            var source = work.File("source.pdf");
            var package = isProject ? await store.LoadAsync(path, source, ct) : null;
            var hash = package?.SourceHash ?? await SafeFiles.CopyPdfAsync(path, source, ct);
            pdf = await renderer.OpenAsync(source, password, ct);
            var project = package ?? DocumentProject.Create(hash, pdf.Pages.Count);
            try { project.Validate(pdf.Pages); }
            catch (InvalidDataException error) { throw new AppFailure("InvalidProject", error); }
            foreach (var resource in project.Resources.Values)
                overlays.ValidateImage(resource.AsSpan());

            return new OpenDocument(work, source, isProject ? null : path,
                isProject && !recovered ? path : null, password, pdf, project, recovered);
        }
        catch
        {
            pdf?.Dispose();
            work.Dispose();
            throw;
        }
    }

    public async Task<OpenDocument> EditTextCopyAsync(OpenDocument document, DocumentProject project,
        PageReference page, PdfTextObject original, string replacement, CancellationToken ct)
    {
        if (document.Encrypted || document.Pdf.Metadata.Signed)
            throw new AppFailure("TextEditProtected");

        var work = directories.Create();
        IPdfDocument? edited = null;
        try
        {
            var output = work.File("rewritten.pdf");
            using (var copy = await renderer.OpenAsync(document.Source, null, ct))
            {
                if (copy is not IAdvancedPdfEditEngine editor)
                    throw new AppFailure("TextEditUnsupported");
                await editor.RewriteTextObjectAsync(page, original, replacement, output, ct);
            }
            await structure.ValidateAsync(output, ct);
            var source = work.File("source.pdf");
            var hash = await SafeFiles.CopyPdfAsync(output, source, ct);
            File.Delete(output);
            edited = await renderer.OpenAsync(source, null, ct);
            if (edited is not IAdvancedPdfEditEngine check || !edited.Pages.SequenceEqual(document.Pdf.Pages) ||
                !StringComparer.Ordinal.Equals(await check.ReadTextObjectAsync(page, original.Index, ct), replacement))
                throw new AppFailure("TextEditUnsupported");

            var now = DateTimeOffset.UtcNow;
            var updated = project with { Id = Guid.NewGuid(), SourceHash = hash, CreatedUtc = now, UpdatedUtc = now };
            return new OpenDocument(work, source, document.Original, null, null, edited, updated, true);
        }
        catch
        {
            edited?.Dispose();
            work.Dispose();
            throw;
        }
    }

    public Task SaveProjectAsync(OpenDocument document, DocumentProject project, string target, CancellationToken ct)
    {
        ProtectOriginal(document, target);
        return store.SaveAsync(project, document.Source, target, ct);
    }

    public async Task ExportAsync(OpenDocument document, DocumentProject project, string target, CancellationToken ct)
    {
        ProtectOriginal(document, target);
        using var work = directories.Create();
        var pdf = work.File("final.pdf");
        await BuildAsync(document, project, pdf, ct);
        await SafeFiles.CommitFileAsync(pdf, target, ct);
    }

    public async Task ExportDocumentAsync(OpenDocument document, DocumentProject project, DocumentExportFormat format,
        string target, string fallbackTitle, bool pagesAsImages, IProgress<WorkProgress>? progress, CancellationToken ct)
    {
        ProtectOriginal(document, target);
        var extension = format switch
        {
            DocumentExportFormat.Word => ".docx",
            DocumentExportFormat.Epub => ".epub",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        if (!Path.GetExtension(target).Equals(extension, StringComparison.OrdinalIgnoreCase))
            throw new AppFailure("OutputExtension");

        using var work = directories.Create();
        var output = work.File("converted" + extension);
        var title = DocumentExport.XmlText(document.Pdf.Metadata.Title).Trim();
        if (title.Length == 0)
            title = DocumentExport.XmlText(fallbackTitle).Trim();
        if (title.Length == 0)
            title = "PDF document";
        var author = DocumentExport.XmlText(document.Pdf.Metadata.Author).Trim();
        var originalWordLayout = format == DocumentExportFormat.Word && pagesAsImages;
        var epubLayout = format == DocumentExportFormat.Epub && !pagesAsImages ? document.Pdf as IPdfPageLayoutSource : null;
        var pages = epubLayout is null ? ReadExportPagesAsync(document, project, pagesAsImages, originalWordLayout, progress, ct)
            : ReadEpubPagesAsync(document, project, epubLayout, progress, ct);
        if (format == DocumentExportFormat.Word)
            await DocumentExport.WriteWordAsync(output, work.File("document.xml"), title, author, pages,
                originalWordLayout ? project.Pages.Length : 0, ct);
        else
            await DocumentExport.WriteEpubAsync(output, title, author, pages, epubLayout is not null, ct);
        SafeFiles.CheckLength(new FileInfo(output).Length, DocumentProject.MaxSourceBytes);
        await SafeFiles.CommitFileAsync(output, target, ct);
    }

    private async IAsyncEnumerable<ExportPage> ReadExportPagesAsync(OpenDocument document, DocumentProject project,
        bool pagesAsImages, bool originalWordLayout, IProgress<WorkProgress>? progress, [EnumeratorCancellation] CancellationToken ct)
    {
        var maxImageSide = originalWordLayout ? 4_400 : 2_200;
        var maxDpi = originalWordLayout ? 300 : 144;
        var byPage = project.Overlays.ToLookup(item => item.PageId);
        long totalBytes = 0;
        for (var index = 0; index < project.Pages.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var page = project.Pages[index];
            var pageOverlays = byPage[page.Id];
            string[] paragraphs = pagesAsImages ? [] : DocumentExport.Lines(await document.Pdf.ReadPageTextAsync(page, ct));
            ExportPage exported;
            if (paragraphs.Length == 0)
            {
                var geometry = new PageTransform(document.Pdf.Pages[page.SourcePage], page.Rotation).Display;
                var dpi = Math.Clamp(72.0 * maxImageSide / Math.Max(geometry.Width, geometry.Height), 6, maxDpi);
                var image = await RenderExportImageAsync(document, project, page, dpi, pageOverlays.Any(), ct);
                totalBytes += image.LongLength;
                exported = new([], new(image, geometry));
            }
            else
            {
                var text = paragraphs.Concat(pageOverlays
                    .Where(item => item.Kind is OverlayKind.Text or OverlayKind.Note)
                    .SelectMany(item => DocumentExport.Lines(item.Text))).ToArray();
                totalBytes += text.Sum(line => (long)line.Length * 4 + 96);
                exported = new(text, null);
            }
            SafeFiles.CheckLength(totalBytes, DocumentProject.MaxSourceBytes);
            yield return exported;
            progress?.Report(new(index + 1, project.Pages.Length));
        }
    }

    private async Task<byte[]> RenderExportImageAsync(OpenDocument document, DocumentProject project, PageReference page,
        double dpi, bool hasOverlays, CancellationToken ct)
    {
        if (hasOverlays)
            return await RenderPngAsync(document, project, page, dpi, ct);
        var raster = await document.Pdf.RenderAsync(page, dpi, ct);
        using var png = new MemoryStream();
        await RasterPng.WriteAsync(raster, dpi, png, ct);
        return png.ToArray();
    }

    public async Task SplitAsync(OpenDocument document, DocumentProject project, IReadOnlyList<int[]> groups,
        string folder, CancellationToken ct)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(folder))!;
        var stage = Path.Combine(parent, $".perplexpdf-split-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        try
        {
            for (var index = 0; index < groups.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var subset = SelectPages(project, groups[index]);
                await ExportAsync(document, subset, Path.Combine(stage, $"part-{index + 1:D3}.pdf"), ct);
            }
            ct.ThrowIfCancellationRequested();
            Directory.Move(stage, folder);
        }
        finally
        {
            try { Directory.Delete(stage, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public async Task ExportPngAsync(OpenDocument document, DocumentProject project, PageReference page,
        int dpi, string target, CancellationToken ct)
    {
        ProtectOriginal(document, target);
        using var work = directories.Create();
        var staged = work.File("page.png");
        var png = await RenderPngAsync(document, project, page, dpi, ct);
        await File.WriteAllBytesAsync(staged, png, ct);
        await SafeFiles.CommitFileAsync(staged, target, ct);
    }

    public async Task<byte[]> RenderPngAsync(OpenDocument document, DocumentProject project, PageReference page,
        double dpi, CancellationToken ct)
    {
        var raster = await document.Pdf.RenderAsync(page, dpi, ct);
        var transform = new PageTransform(document.Pdf.Pages[page.SourcePage], page.Rotation);
        return await overlays.ComposePngAsync(raster, transform, project, page.Id, ct);
    }

    public Task<byte[]> ReadImageAsync(string path, CancellationToken ct) => overlays.NormalizeImageAsync(path, ct);

    public async Task<OpenDocument> CreateFromImagesAsync(IReadOnlyList<ImagePage> images, CancellationToken ct)
    {
        using var work = directories.Create();
        var output = work.File("images.pdf");
        await overlays.CreateFromImagesAsync(images, output, ct);
        await ValidateAsync(output, images.Count, ct);
        return await AdoptAsync(output, ct);
    }

    public async Task<OpenDocument> MergeAsync(IReadOnlyList<OpenDocument> documents, CancellationToken ct)
    {
        using var work = directories.Create();
        var sources = new List<SourceSelection>();
        var pageCount = documents.Sum(doc => doc.History.Current.Pages.Length);
        if (pageCount > DocumentProject.MaxPages)
            throw new AppFailure("SizeLimit");
        foreach (var doc in documents)
        {
            var export = work.File($"merge-{sources.Count}.pdf");
            var project = doc.History.Current;
            await BuildAsync(doc, project, export, ct);
            sources.Add(new(export, Enumerable.Range(0, project.Pages.Length).ToArray()));
        }
        var output = work.File("merged.pdf");
        await structure.MergeAsync(sources, output, ct);
        await ValidateAsync(output, pageCount, ct);
        return await AdoptAsync(output, ct);
    }

    public async Task OcrAsync(OpenDocument document, DocumentProject project, string target, IReadOnlyList<int> pages,
        string language, int dpi, IProgress<WorkProgress> progress, CancellationToken ct)
    {
        ProtectOriginal(document, target);
        using var work = directories.Create();
        var input = work.File("input.pdf");
        var result = work.File("searchable.pdf");
        await BuildAsync(document, project, input, ct);
        await ocr.CreateSearchablePdfAsync(input, result, pages, language, dpi, progress, ct);
        await ValidateAsync(result, project.Pages.Length, ct);
        await SafeFiles.CommitFileAsync(result, target, ct);
    }

    public static DocumentProject SelectPages(DocumentProject project, IEnumerable<int> indices)
    {
        var pages = indices.Select(index => project.Pages[index]).ToImmutableArray();
        var ids = pages.Select(page => page.Id).ToHashSet();
        return project with
        {
            Pages = pages,
            Overlays = project.Overlays.Where(overlay => ids.Contains(overlay.PageId)).ToImmutableArray()
        };
    }

    private async Task<OpenDocument> AdoptAsync(string source, CancellationToken ct)
    {
        var work = directories.Create();
        try
        {
            var local = work.File("source.pdf");
            var hash = await SafeFiles.CopyPdfAsync(source, local, ct);
            var pdf = await renderer.OpenAsync(local, null, ct);
            return new OpenDocument(work, local, null, null, null, pdf, DocumentProject.Create(hash, pdf.Pages.Count), true);
        }
        catch
        {
            work.Dispose();
            throw;
        }
    }

    private async Task BuildAsync(OpenDocument document, DocumentProject project, string output, CancellationToken ct)
    {
        using var work = directories.Create();
        var hasOverlays = project.Overlays.Length != 0;
        var rebuilt = hasOverlays ? work.File("pages.pdf") : output;
        await structure.RebuildPagesAsync(document.Source, document.Password, project.Pages, rebuilt, ct);
        if (hasOverlays)
        {
            var layer = work.File("overlays.pdf");
            await overlays.WriteOverlayAsync(project, document.Pdf.Pages, layer, ct);
            await structure.ApplyOverlayAsync(rebuilt, layer, output, ct);
        }
        await ValidateAsync(output, project.Pages.Length, ct);
    }

    private async Task ValidateAsync(string output, int count, CancellationToken ct)
    {
        SafeFiles.CheckLength(new FileInfo(output).Length, DocumentProject.MaxSourceBytes);
        await structure.ValidateAsync(output, ct);
        using var check = await renderer.OpenAsync(output, null, ct);
        if (check.Pages.Count != count)
            throw new AppFailure("InvalidExport");
    }

    private static void ProtectOriginal(OpenDocument document, string target)
    {
        if (SafeFiles.SamePath(document.Source, target) ||
            document.Original is not null && SafeFiles.SamePath(document.Original, target))
            throw new AppFailure("OriginalProtected");
    }
}
