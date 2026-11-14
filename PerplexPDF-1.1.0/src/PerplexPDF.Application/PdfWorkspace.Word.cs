using System.Runtime.CompilerServices;
using PerplexPDF.Domain;

namespace PerplexPDF.Application;

public sealed partial class PdfWorkspace
{
    public async Task<int> ExportWordAsync(OpenDocument document, DocumentProject project, string target, string title,
        WordExportOptions options, IProgress<WorkProgress>? progress, CancellationToken ct)
    {
        ProtectOriginal(document, target);
        if (!Path.GetExtension(target).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            throw new AppFailure("OutputExtension");
        if (document.Pdf is not IPdfPageLayoutSource source)
            throw new AppFailure("DependencyMissing");

        using var work = directories.Create();
        var footer = options.MovePageNumbers && project.Pages.Length > 1 ? await ReadFooterAsync(source, project, ct) : null;
        var output = work.File("converted.docx");
        var author = WordLayout.Clean(document.Pdf.Metadata.Author);
        var pages = ReadWordPagesAsync(document, project, source, options, footer, progress, ct);
        var pagesWithoutText = await WordDocx.WriteAsync(output, work.File("document.xml"), WordLayout.Clean(title), author,
            options, footer, pages, ct);
        SafeFiles.CheckLength(new FileInfo(output).Length, DocumentProject.MaxSourceBytes);
        await SafeFiles.CommitFileAsync(output, target, ct);
        return pagesWithoutText;
    }

    private static async Task<WordFooter?> ReadFooterAsync(IPdfPageLayoutSource source, DocumentProject project, CancellationToken ct)
    {
        var candidates = new List<WordPageNumber>();
        for (var index = 0; index < project.Pages.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var layout = await source.ReadPageLayoutAsync(project.Pages[index], false, ct);
            if (WordLayout.FooterCandidate(WordLayout.Lines(layout), layout.Geometry, index + 1, project.Pages.Length) is { } match)
                candidates.Add(match);
        }
        return WordLayout.Footer(candidates, project.Pages.Length);
    }

    private static async IAsyncEnumerable<WordExportPage> ReadWordPagesAsync(OpenDocument document, DocumentProject project,
        IPdfPageLayoutSource source, WordExportOptions options, WordFooter? footer, IProgress<WorkProgress>? progress,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var overlaysByPage = project.Overlays.ToLookup(item => item.PageId);
        long payload = 0;
        for (var index = 0; index < project.Pages.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var page = project.Pages[index];
            var layout = await source.ReadPageLayoutAsync(page, options.IncludeImages, ct);
            var lines = WordLayout.Lines(layout);
            var hasEditableText = lines.Length != 0;
            if (footer?.Lines.TryGetValue(index + 1, out var number) == true)
                lines = lines.Where(line => line.Text != number.Text || Math.Abs(line.Baseline - number.Baseline) > .01).ToArray();
            var blocks = WordLayout.Blocks(lines, layout, options).ToList();
            if (lines.Length == 0 && blocks.Count == 0 && options.IncludeImages)
            {
                var display = new PageTransform(document.Pdf.Pages[page.SourcePage], page.Rotation).Display;
                var dpi = Math.Clamp(72 * 2_200 / Math.Max(display.Width, display.Height), 6, 144);
                var raster = await document.Pdf.RenderAsync(page, dpi, ct);
                blocks.Add(new WordPicture(0, raster, display));
            }
            foreach (var item in overlaysByPage[page.Id].Where(item => item.Kind is OverlayKind.Text or OverlayKind.Note))
            {
                var text = WordLayout.Clean(item.Text);
                if (text.Length != 0)
                {
                    blocks.Add(new WordParagraph(layout.Geometry.Height, text,
                        new WordStyle(item.FontSize, item.Bold, item.Italic, item.Color & 0xFFFFFF)));
                    hasEditableText = true;
                }
            }
            payload += blocks.Sum(block => block switch
            {
                WordPicture picture => picture.Raster.Bgra.LongLength,
                WordParagraph paragraph => 4L * paragraph.Text.Length + 256,
                WordTable table => table.Rows.Sum(row => row.Sum(cell => 4L * cell.Text.Length + 256)),
                _ => 0
            });
            if (payload > 4 * DocumentProject.MaxSourceBytes)
                throw new AppFailure("SizeLimit");
            yield return new(layout.Geometry, blocks, hasEditableText);
            progress?.Report(new(index + 1, project.Pages.Length));
        }
    }
}
