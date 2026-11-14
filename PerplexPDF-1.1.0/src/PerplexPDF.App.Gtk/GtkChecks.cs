using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using PerplexPDF.Infrastructure.Store;

namespace PerplexPDF.Desktop;

internal static class GtkChecks
{
    internal static async Task<int> RunAsync(string fixtures, string output)
    {
        Directory.CreateDirectory(output);
        var services = new GtkServices(new LocalWorkspace(Path.Combine(output, "state")));
        var workspace = services.Workspace;
        using var document = await workspace.OpenAsync(Path.Combine(fixtures, "word-layout.pdf"), null, false, default);
        var page = document.History.Current.Pages[0];
        var image = await workspace.ReadImageAsync(Path.Combine(fixtures, "logo.png"), default);
        var hash = Convert.ToHexStringLower(SHA256.HashData(image));
        var overlay = new OverlayObject(Guid.NewGuid(), page.Id, OverlayKind.Text, new(48, 470, 280, 48), 0xff204978,
            "PerplexPDF verification", FontSize: 20, Bold: true, Italic: true, Underlined: true) { SystemFont = "Sans" };
        var picture = new OverlayObject(Guid.NewGuid(), page.Id, OverlayKind.Image, new(48, 560, 80, 40), 0xffffffff,
            ResourceHash: hash);
        var project = document.History.Current with
        {
            Overlays = [overlay, picture],
            Resources = ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add(hash, ImmutableArray.CreateRange(image))
        };
        project.Validate(document.Pdf.Pages);
        var package = Path.Combine(output, "roundtrip.ppdf");
        await workspace.SaveProjectAsync(document, project, package, default);
        using var reopened = await workspace.OpenAsync(package, null, false, default);
        Require(reopened.History.Current.Overlays.SequenceEqual(project.Overlays), "project round trip");
        var png = Path.Combine(output, "annotated.png");
        await workspace.ExportPngAsync(reopened, project, page, 96, png, default);
        Require(new FileInfo(png).Length > 5_000, "Cairo PNG export");
        var pdf = Path.Combine(output, "annotated.pdf");
        await workspace.ExportAsync(reopened, project, pdf, default);
        using var reloaded = await workspace.OpenAsync(pdf, null, false, default);
        Require((await reloaded.Pdf.ReadPageTextAsync(reloaded.History.Current.Pages[0], default)).Contains("PerplexPDF verification", StringComparison.Ordinal),
            "Cairo PDF overlays stay searchable");

        var docx = Path.Combine(output, "editable.docx");
        var noText = await workspace.ExportWordAsync(document, document.History.Current, docx, "PerplexPDF Word", new(), null, default);
        Require(noText == 0, "editable Word export");
        using (var zip = ZipFile.OpenRead(docx))
        {
            using var stream = zip.GetEntry("word/document.xml")!.Open();
            var xml = XDocument.Load(stream);
            XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            Require(xml.Descendants(word + "t").Any(node => node.Value == "Project overview") &&
                xml.Descendants(word + "shd").Any() && zip.Entries.Count(entry => entry.FullName.StartsWith("word/media/")) == 2,
                "Word text, colors and separate pictures");
        }
        Console.WriteLine("PASS project, Cairo overlays, PNG and editable DOCX");

        var editable = (IAdvancedPdfEditEngine)document.Pdf;
        var match = (await document.Pdf.SearchAsync([page], "Project overview", default)).First().Bounds.First();
        var text = await editable.FindTextObjectAsync(page, new(match.X + match.Width / 2, match.Y + match.Height / 2), default);
        Require(text is not null, "PDF text hit testing");
        using var copy = await workspace.EditTextCopyAsync(document, document.History.Current, page, text!, "Edited overview", default);
        Require((await copy.Pdf.ReadPageTextAsync(copy.History.Current.Pages[0], default)).Contains("Edited overview", StringComparison.Ordinal),
            "direct PDF text edit");
        using var images = await workspace.CreateFromImagesAsync([new ImagePage(image, 90, true)], default);
        Require(images.Pdf.Pages[0].Width > images.Pdf.Pages[0].Height, "image-to-PDF rotation and orientation");
        Console.WriteLine("PASS native text editing and image-to-PDF");

        using var tagged = await workspace.OpenAsync(Path.Combine(fixtures, "epub-structure.pdf"), null, false, default);
        var book = Path.Combine(output, "chapters.epub");
        await workspace.ExportDocumentAsync(tagged, tagged.History.Current, DocumentExportFormat.Epub, book, "Chapters", false, null, default);
        using (var zip = ZipFile.OpenRead(book))
        {
            using var stream = zip.GetEntry("EPUB/nav.xhtml")!.Open();
            var nav = XDocument.Load(stream);
            XNamespace html = "http://www.w3.org/1999/xhtml";
            Require(nav.Descendants(html + "a").Select(node => node.Value).SequenceEqual(["First chapter", "Second chapter", "Third chapter"]) &&
                zip.Entries.Count(entry => entry.FullName.StartsWith("EPUB/images/")) == 2, "tagged EPUB chapters and images");
        }
        var annotatedBook = Path.Combine(output, "annotated.epub");
        await workspace.ExportDocumentAsync(document, project, DocumentExportFormat.Epub, annotatedBook, "Annotated EPUB", false, null, default);
        using (var zip = ZipFile.OpenRead(annotatedBook))
            Require(zip.Entries.Count(entry => entry.FullName.StartsWith("EPUB/images/")) == 3, "inserted image in EPUB");
        Console.WriteLine("PASS EPUB chapters and embedded or inserted images");

        using var scan = await workspace.OpenAsync(Path.Combine(fixtures, "scanned-en-de.pdf"), null, false, default);
        var searchable = Path.Combine(output, "searchable.pdf");
        await workspace.OcrAsync(scan, scan.History.Current, searchable, [0, 1], "eng+deu", 300, new Progress<WorkProgress>(), default);
        using var ocr = await workspace.OpenAsync(searchable, null, false, default);
        Require((await ocr.Pdf.SearchAsync(ocr.History.Current.Pages, "lighthouse", default)).Count != 0 &&
                (await ocr.Pdf.SearchAsync(ocr.History.Current.Pages, "Leuchtturm", default)).Count != 0, "bilingual OCR");
        Console.WriteLine("PASS local English and German OCR");
        return 0;
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException($"Verification failed: {description}.");
    }
}
