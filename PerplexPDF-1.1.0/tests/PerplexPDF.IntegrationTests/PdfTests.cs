using System.Collections.Immutable;
using System.Security.Cryptography;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using PerplexPDF.Infrastructure.Ocr;
using PerplexPDF.Infrastructure.Pdf;
using PerplexPDF.Infrastructure.Store;
using PerplexPDF.Tests;
using SkiaSharp;
using Xunit;

namespace PerplexPDF.IntegrationTests;

public sealed class PdfTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
    private static readonly string Fonts = Path.Combine(AppContext.BaseDirectory, "assets", "fonts");
    private static readonly string Pdfium = Setting("PDFIUM");
    private static readonly string Qpdf = Setting("QPDF");
    private static readonly string Tesseract = Setting("TESSERACT");
    private static readonly string Tessdata = Setting("TESSDATA");
    private readonly NativeScenarios _native = new(Pdfium, Qpdf, Tesseract, Tessdata, Fixtures);

    [Fact] public Task CreatesAndSavesBlankA4Pdf() => _native.BlankA4();
    [Fact] public Task OpensProtectedPdfAndSearchesRotatedCrop() => _native.OpenSearchAndPassword();
    [Fact] public Task ReordersExtractsAndMergesRealPdfs() => _native.Structure();
    [Fact] public Task ConvertsPdfToWordAndEpub() => _native.ConvertDocuments();
    [Fact] public Task ReplacesOriginalPdfText() => _native.ReplaceOriginalPdfText();
    [Fact] public Task ReconstructsEditableWordContent() => _native.StructuredWord();
    [Fact] public Task ScannedEnglishAndGermanBecomeSearchable() => _native.Ocr(german: true);
    [Fact] public Task OcrCancellationDoesNotPublishPartialOutput() => _native.CancelOcr();

    [Fact]
    public async Task OverlayProjectAndStandardPdfRetainPositionsAndSourceText()
    {
        using var test = new CoreScenarios.Scratch();
        using var work = test.Workspace.Create();
        var overlayEngine = new SkiaOverlayEngine(Fonts);
        var renderer = new PdfiumRenderer(Pdfium);
        var workspace = Build(test.Workspace, renderer, overlayEngine);
        var original = Path.Combine(Fixtures, "example.pdf");
        using var document = await workspace.OpenAsync(original, null, false, default);
        var project = document.History.Current;
        var page = project.Pages[1];
        project = PageEdits.Rotate(PageEdits.Move(project, page.Id, 0), page.Id, 90);
        project = PdfWorkspace.SelectPages(project, [0, 1]);
        page = project.Pages[0];
        var transform = new PageTransform(document.Pdf.Pages[page.SourcePage], page.Rotation);
        var image = await workspace.ReadImageAsync(Path.Combine(Fixtures, "logo.png"), default);
        var hash = Convert.ToHexStringLower(SHA256.HashData(image));
        var text = new OverlayObject(Guid.NewGuid(), page.Id, OverlayKind.Text,
            transform.ToSource(new PdfRect(260, 130, 180, 200)), 0xff172638, "Grüße from PerplexPDF",
            FontSize: 20, Font: OverlayFont.Serif, Bold: true, Italic: true, Underlined: true,
            CreationRotation: page.Rotation);
        var note = new OverlayObject(Guid.NewGuid(), page.Id, OverlayKind.Note, new(20, 45, 135, 65), 0xff172638, "Review note", 12);
        var highlight = new OverlayObject(Guid.NewGuid(), page.Id, OverlayKind.Highlight,
            transform.ToSource(new PdfRect(30, 50, 70, 40)), 0xffff00ff);
        var logo = new OverlayObject(Guid.NewGuid(), page.Id, OverlayKind.Image, new(200, 200, 130, 65), 0xff000000, ResourceHash: hash);
        project = project with
        {
            Overlays = [text, note, highlight, logo],
            Resources = project.Resources.Add(hash, ImmutableArray.CreateRange(image))
        };
        var path = work.File("project.ppdf");
        await workspace.SaveProjectAsync(document, project, path, default);
        using var loaded = await workspace.OpenAsync(path, null, false, default);
        var updated = loaded.History.Current;
        Check.That(updated.Overlays.SequenceEqual(project.Overlays) && updated.Resources[hash].AsSpan().SequenceEqual(image), "Project lost overlays.");
        updated = updated with { Overlays = updated.Overlays.SetItem(0, text with { Text = "Grüße again" }) };
        var export = work.File("export.pdf");
        await workspace.ExportAsync(loaded, updated, export, default);
        using var final = await renderer.OpenAsync(export, null, default);
        var exportedPage = new PageReference(page.Id, 0);
        Check.That(final.Pages.Count == 2 && final.Pages[0] == transform.Display, "Export size or page count changed.");
        Check.That((await final.SearchAsync([exportedPage], "quick brown fox", default)).Count == 1, "Source text was lost.");
        Check.That((await final.SearchAsync([exportedPage], "Grüße again", default)).Count == 1, "Edited Unicode overlay was lost.");

        var raster = await final.RenderAsync(exportedPage, 72, default);
        var pixel = (y: 60, x: 40);
        var position = (pixel.y * raster.Width + pixel.x) * 4;
        Check.That(raster.Bgra[position] > 245 && raster.Bgra[position + 1] < 10 && raster.Bgra[position + 2] > 245,
            "Overlay moved on export from a cropped and rotated page.");

        var png = work.File("page.png");
        await workspace.ExportPngAsync(loaded, updated, page, 144, png, default);
        using var bitmap = SKBitmap.Decode(png);
        Check.That(bitmap.Width == (int)transform.Display.Width * 2 && bitmap.Height == (int)transform.Display.Height * 2,
            "PNG export resolution is wrong.");
        await Check.FailsAsync<AppFailure>(() => workspace.ExportAsync(document, project, original, default),
            failure => failure.Code == "OriginalProtected");
    }

    [Fact]
    public async Task ImageOrderOrientationAndSplitPreservePageSizes()
    {
        using var test = new CoreScenarios.Scratch();
        using var work = test.Workspace.Create();
        var renderer = new PdfiumRenderer(Pdfium);
        var workspace = Build(test.Workspace, renderer, new SkiaOverlayEngine(Fonts));
        var jpg = await workspace.ReadImageAsync(Path.Combine(Fixtures, "portrait.jpg"), default);
        var png = await workspace.ReadImageAsync(Path.Combine(Fixtures, "logo.png"), default);
        using var document = await workspace.CreateFromImagesAsync([new(jpg, 90, true), new(png, 0, false)], default);
        Check.That(document.Pdf.Pages[0] == new PageGeometry(842, 595) &&
                   document.Pdf.Pages[1] == new PageGeometry(595, 842), "Image page order or orientation changed.");
        var folder = work.File("split");
        await workspace.SplitAsync(document, document.History.Current, [[1], [0]], folder, default);
        using var part = await renderer.OpenAsync(Path.Combine(folder, "part-001.pdf"), null, default);
        Check.That(part.Pages.Single() == new PageGeometry(595, 842), "Split chose the wrong source page.");
    }

    private static PdfWorkspace Build(LocalWorkspace local, IPdfRenderEngine renderer, IPdfOverlayEngine overlay)
    {
        var structure = new QpdfStructure(Qpdf, local);
        return new(renderer, structure, overlay, new TesseractOcr(Tesseract, Tessdata, renderer, structure, local),
            new ZipProjectStore(local), local);
    }

    private static string Setting(string key) => Environment.GetEnvironmentVariable("PERPLEXPDF_" + key)
        ?? throw new InvalidOperationException("Set PERPLEXPDF_" + key + " to the corresponding absolute dependency path.");
}
