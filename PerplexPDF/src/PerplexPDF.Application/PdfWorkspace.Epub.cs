using System.Runtime.CompilerServices;
using PerplexPDF.Domain;

namespace PerplexPDF.Application;

public sealed partial class PdfWorkspace
{
    private async IAsyncEnumerable<ExportPage> ReadEpubPagesAsync(OpenDocument document, DocumentProject project,
        IPdfPageLayoutSource source, IProgress<WorkProgress>? progress, [EnumeratorCancellation] CancellationToken ct)
    {
        var overlays = project.Overlays.ToLookup(item => item.PageId);
        var bookmarks = document.Pdf.Bookmarks.ToLookup(item => item.SourcePage);
        long bytes = 0;
        for (var index = 0; index < project.Pages.Length; index++)
        {
            ct.ThrowIfCancellationRequested();
            var page = project.Pages[index];
            var layout = await source.ReadPageLayoutAsync(page, includeImages: true, ct, readHeadings: true);
            var blocks = TextBlocks(layout, bookmarks[page.SourcePage]);
            var annotations = overlays[page.Id];
            if (blocks.Count == 0)
            {
                var geometry = new PageTransform(document.Pdf.Pages[page.SourcePage], page.Rotation).Display;
                var dpi = Math.Clamp(72 * 2_200 / Math.Max(geometry.Width, geometry.Height), 6, 144);
                var image = await RenderExportImageAsync(document, project, page, dpi, annotations.Any(), ct);
                bytes += image.LongLength;
                SafeFiles.CheckLength(bytes, DocumentProject.MaxSourceBytes);
                yield return new([], new(image, geometry));
            }
            else
            {
                foreach (var picture in layout.Images)
                {
                    ct.ThrowIfCancellationRequested();
                    using var png = new MemoryStream();
                    var dpi = 72 * picture.Raster.Width / picture.Bounds.Width;
                    await RasterPng.WriteAsync(picture.Raster, dpi, png, ct);
                    blocks.Add(new(picture.Bounds.Y, $"Image on PDF page {index + 1}", Picture: png.ToArray()));
                }

                foreach (var item in annotations)
                {
                    if (item.Kind is OverlayKind.Text or OverlayKind.Note)
                        blocks.AddRange(DocumentExport.Lines(item.Text).Select(line => new EpubBlock(item.Bounds.Y, line,
                            Bold: item.Bold, Italic: item.Italic)));
                    else if (item.Kind == OverlayKind.Image && project.Resources.TryGetValue(item.ResourceHash!, out var image))
                        blocks.Add(new(item.Bounds.Y, $"Image on PDF page {index + 1}", Picture: image.ToArray()));
                }

                bytes += blocks.Sum(block => block.Picture is { } picture ? picture.LongLength : (long)block.Text.Length * 4 + 96);
                SafeFiles.CheckLength(bytes, DocumentProject.MaxSourceBytes);
                yield return new([], null, blocks.OrderBy(block => block.Top).ToArray());
            }
            progress?.Report(new(index + 1, project.Pages.Length));
        }
    }

    private static List<EpubBlock> TextBlocks(PdfPageLayout layout, IEnumerable<PdfBookmark> bookmarks)
    {
        var titles = bookmarks.Select(item => WordLayout.Clean(item.Title)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groups = layout.Spans.Where(span => span.Heading.Level != 0).GroupBy(span => span.Heading);
        var lines = WordLayout.Lines(layout with { Spans = layout.Spans.Where(span => span.Heading.Level == 0).ToArray() });
        var bodySize = WordLayout.BodyFontSize(lines);
        var blocks = new List<EpubBlock>();
        foreach (var line in lines)
        {
            var level = !layout.Tagged && (titles.Contains(line.Text) || WordLayout.IsHeading(line, bodySize)) ? 1 : 0;
            blocks.Add(new(line.Top, line.Text, level, line.Style.Bold, line.Style.Italic));
        }
        foreach (var group in groups)
        {
            var heading = WordLayout.Lines(layout with { Spans = group.ToArray(), FallbackText = "" });
            if (heading.Length != 0)
                blocks.Add(new(heading[0].Top, string.Join(" ", heading.Select(line => line.Text)), group.Key.Level));
        }
        return blocks;
    }
}
