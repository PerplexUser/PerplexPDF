using PerplexPDF.Domain;

namespace PerplexPDF.Application;

public sealed class AppFailure(string code, Exception? cause = null, int? exitCode = null)
    : Exception(code, cause)
{
    public string Code { get; } = code;
    public int? ExitCode { get; } = exitCode;
    public Guid DiagnosticId { get; } = Guid.NewGuid();
}

public sealed record PdfBookmark(string Title, int? SourcePage, IReadOnlyList<PdfBookmark> Children);
public sealed record TextHit(Guid PageId, IReadOnlyList<PdfRect> Bounds);
public sealed record RasterPage(int Width, int Height, byte[] Bgra);
public sealed record PdfMetadata(string Title, string Author, string Keywords, bool Signed);
public sealed record SourceSelection(string Path, int[] Pages, string? Password = null);
public sealed record ImagePage(byte[] Image, int Rotation, bool Landscape);
public sealed record WorkProgress(int Completed, int Total);
public enum DocumentExportFormat { Word, Epub }
public sealed record PdfTextObject(int Index, string Text);
public readonly record struct PdfHeading(int Level, int Group);
public sealed record PdfTextSpan(string Text, double Left, double Right, double Baseline, double Size, bool Bold, bool Italic,
    uint? Foreground = null, uint? Background = null, PdfHeading Heading = default);
public sealed record PdfImageSpan(PdfRect Bounds, RasterPage Raster);
public sealed record PdfPageLayout(PageGeometry Geometry, IReadOnlyList<PdfTextSpan> Spans,
    IReadOnlyList<PdfImageSpan> Images, string FallbackText = "", bool Tagged = false);

public interface IPdfRenderEngine
{
    Task<IPdfDocument> OpenAsync(string path, string? password, CancellationToken ct);
    Task CreateBlankPageAsync(PageGeometry size, string output, CancellationToken ct);
}

public interface IPdfDocument : IDisposable
{
    IReadOnlyList<PageGeometry> Pages { get; }
    IReadOnlyList<PdfBookmark> Bookmarks { get; }
    PdfMetadata Metadata { get; }
    Task<RasterPage> RenderAsync(PageReference page, double dpi, CancellationToken ct);
    Task<IReadOnlyList<TextHit>> SearchAsync(IReadOnlyList<PageReference> pages, string term, CancellationToken ct);
    Task<string> ReadTextAsync(PageReference page, PdfRect bounds, CancellationToken ct);
    Task<string> ReadPageTextAsync(PageReference page, CancellationToken ct);
}

public interface IPdfPageLayoutSource
{
    Task<PdfPageLayout> ReadPageLayoutAsync(PageReference page, bool includeImages, CancellationToken ct, bool readHeadings = false);
}

public interface IPdfStructureEngine
{
    Task RebuildPagesAsync(string source, string? password, IReadOnlyList<PageReference> order,
        string output, CancellationToken ct);
    Task MergeAsync(IReadOnlyList<SourceSelection> sources, string output, CancellationToken ct);
    Task ApplyOverlayAsync(string source, string overlay, string output, CancellationToken ct);
    Task ValidateAsync(string source, CancellationToken ct);
}

public interface IPdfOverlayEngine
{
    Task WriteOverlayAsync(DocumentProject project, IReadOnlyList<PageGeometry> sourcePages, string output,
        CancellationToken ct);
    Task<byte[]> ComposePngAsync(RasterPage raster, PageTransform transform, DocumentProject project,
        Guid pageId, CancellationToken ct);
    Task<byte[]> NormalizeImageAsync(string path, CancellationToken ct);
    void ValidateImage(ReadOnlySpan<byte> png);
    Task CreateFromImagesAsync(IReadOnlyList<ImagePage> images, string output, CancellationToken ct);
}

public interface IOcrEngine
{
    Task CreateSearchablePdfAsync(string input, string output, IReadOnlyList<int> pages, string language,
        int dpi, IProgress<WorkProgress>? progress, CancellationToken ct);
}

public interface IProjectStore
{
    Task SaveAsync(DocumentProject project, string source, string target, CancellationToken ct);
    Task<DocumentProject> LoadAsync(string path, string sourceTarget, CancellationToken ct);
}

public interface IWorkDirectory : IDisposable
{
    string File(string name);
}

public interface IWorkDirectories
{
    IWorkDirectory Create();
}

public interface IAdvancedPdfEditEngine
{
    Task<PdfTextObject?> FindTextObjectAsync(PageReference page, PdfPoint point, CancellationToken ct);
    Task<string?> ReadTextObjectAsync(PageReference page, int index, CancellationToken ct);
    Task RewriteTextObjectAsync(PageReference page, PdfTextObject original, string replacement, string output, CancellationToken ct);
}

// Future adapters are deliberately excluded from the service composition.
public interface IRedactionEngine
{
    Task RemoveAndVerifyAsync(string source, string target, IReadOnlyList<TextHit> areas, CancellationToken ct);
}

public interface IPdfSignatureEngine
{
    Task VerifyAsync(string source, CancellationToken ct);
}
