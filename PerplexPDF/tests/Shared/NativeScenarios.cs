using System.Buffers.Binary;
using System.IO.Compression;
using System.Xml.Linq;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using PerplexPDF.Infrastructure.Ocr;
using PerplexPDF.Infrastructure.Pdf;
using PerplexPDF.Infrastructure.Store;

namespace PerplexPDF.Tests;

internal sealed class NativeScenarios(string pdfium, string qpdf, string tesseract, string tessdata, string fixtures)
{
    private readonly IPdfRenderEngine _renderer = new PdfiumRenderer(pdfium);
    private string Fixture(string name) => Path.Combine(fixtures, name);

    public async Task OpenSearchAndPassword()
    {
        using var document = await _renderer.OpenAsync(Fixture("example.pdf"), null, default);
        Check.That(document.Pages.Count == 4 && document.Metadata.Title == "PerplexPDF example", "Wrong document info.");
        Check.That(document.Bookmarks.Count == 3 && document.Bookmarks[1].SourcePage == 1, "Wrong bookmarks.");
        var page = new PageReference(Guid.NewGuid(), 1);
        var hit = (await document.SearchAsync([page], "quick brown fox", default)).Single();
        Check.That(hit.PageId == page.Id && hit.Bounds.Count > 0, "Search did not yield hit geometry.");
        var bounds = hit.Bounds[0];
        var expanded = new PdfRect(bounds.X - 2, bounds.Y - 2, bounds.Width + 4, bounds.Height + 4);
        Check.That((await document.ReadTextAsync(page, expanded, default)).Contains("quick brown fox"), "Selection on rotated crop was wrong.");
        var raster = await document.RenderAsync(page, 72, default);
        Check.That(raster.Width == 437 && raster.Height == 671 && raster.Bgra.Any(value => value < 90), "Rotated crop raster has wrong size or content.");
        var unicodePage = new PageReference(Guid.NewGuid(), 3);
        Check.That((await document.SearchAsync([unicodePage], "Grüße", default)).Count == 1, "Unicode search failed.");
        using var passiveFixture = await _renderer.OpenAsync(Fixture("javascript.pdf"), null, default);
        Check.That((await passiveFixture.RenderAsync(new(Guid.NewGuid(), 0), 72, default)).Width == 612, "Active-content fixture did not render.");
        using var large = await _renderer.OpenAsync(Fixture("200-pages.pdf"), null, default);
        Check.That(large.Pages.Count == 200, "200-page fixture did not open.");
        await Check.FailsAsync<AppFailure>(() => _renderer.OpenAsync(Fixture("protected.pdf"), null, default),
            error => error.Code == "PdfPassword");
        await Check.FailsAsync<AppFailure>(() => _renderer.OpenAsync(Fixture("protected.pdf"), "invalid", default),
            error => error.Code == "PdfPassword");
        using var decrypted = await _renderer.OpenAsync(Fixture("protected.pdf"), "perplex-test", default);
        Check.That(decrypted.Pages.Count == 4, "Valid password did not open.");
        await Check.FailsAsync<AppFailure>(() => _renderer.OpenAsync(Fixture("corrupt.pdf"), null, default),
            error => error.Code == "InvalidPdf");
    }

    public async Task Structure()
    {
        using var test = new CoreScenarios.Scratch();
        using var work = test.Workspace.Create();
        var structure = new QpdfStructure(qpdf, test.Workspace);
        var source = Fixture("protected.pdf");
        var reordered = work.File("reordered.pdf");
        var order = new PageReference[] { new(Guid.NewGuid(), 1, 90), new(Guid.NewGuid(), 0), new(Guid.NewGuid(), 2) };
        await structure.RebuildPagesAsync(source, "perplex-test", order, reordered, default);
        await structure.ValidateAsync(reordered, default);
        using var output = await _renderer.OpenAsync(reordered, null, default);
        Check.That(output.Pages.SequenceEqual(new[] { new PageGeometry(671, 437), new PageGeometry(612, 792), new PageGeometry(450, 600) }),
            "Crop, page ordering or relative rotation changed.");
        var extract = work.File("extract.pdf");
        await structure.RebuildPagesAsync(reordered, null, [new(Guid.NewGuid(), 2)], extract, default);
        var merge = work.File("merged.pdf");
        await structure.MergeAsync([new(extract, [0]), new(reordered, [1, 0])], merge, default);
        await structure.ValidateAsync(merge, default);
        using var merged = await _renderer.OpenAsync(merge, null, default);
        Check.That(merged.Pages.SequenceEqual(new[] { output.Pages[2], output.Pages[1], output.Pages[0] }), "Merge output order is wrong.");

        var project = DocumentProject.Create(new string('a', 64), 4);
        var original = project.Pages[0];
        var doubled = PageEdits.Duplicate(project, original.Id);
        var duplicate = doubled.Pages[1];
        Check.That(duplicate.Id != original.Id && duplicate.SourcePage == original.SourcePage, "Duplicate page has the wrong identity.");
        var duplicateFile = work.File("duplicated.pdf");
        await structure.RebuildPagesAsync(source, "perplex-test", doubled.Pages, duplicateFile, default);
        using var duplicates = await _renderer.OpenAsync(duplicateFile, null, default);
        Check.That(duplicates.Pages.Count == 5 &&
            (await duplicates.SearchAsync([new(Guid.NewGuid(), 1)], "Sample page 1", default)).Count == 1,
            "PDF export lost the duplicated page.");
    }

    public async Task ReplaceOriginalPdfText()
    {
        using var test = new CoreScenarios.Scratch();
        using var output = test.Workspace.Create();
        var structure = new QpdfStructure(qpdf, test.Workspace);
        var workspace = new PdfWorkspace(_renderer, structure, null!, null!, new ZipProjectStore(test.Workspace), test.Workspace);
        using var original = await workspace.OpenAsync(Fixture("example.pdf"), null, false, default);
        var page = original.History.Current.Pages[0];
        const string before = "quick brown fox";
        const string after = "quick blue fox";
        var hit = (await original.Pdf.SearchAsync([page], before, default)).Single().Bounds[0];
        var editor = (IAdvancedPdfEditEngine)original.Pdf;
        var selected = await editor.FindTextObjectAsync(page, new(hit.X + hit.Width / 2, hit.Y + hit.Height / 2), default);
        Check.That(selected is not null && selected.Text.Contains(before), "Hit-testing did not select the original PDF text.");
        var replacement = selected!.Text.Replace(before, after, StringComparison.Ordinal);
        using var edited = await workspace.EditTextCopyAsync(original, original.History.Current, page, selected, replacement, default);
        Check.That((await original.Pdf.SearchAsync([page], before, default)).Count == 1,
            "Editing changed the source document.");
        var target = output.File("edited.pdf");
        await workspace.ExportAsync(edited, edited.History.Current, target, default);
        using var reopened = await _renderer.OpenAsync(target, null, default);
        Check.That((await reopened.SearchAsync([page], before, default)).Count == 0 &&
            (await reopened.SearchAsync([page], after, default)).Count == 1,
            "The old content remained searchable or the edited text was lost.");
        var next = await ((IAdvancedPdfEditEngine)edited.Pdf).ReadTextObjectAsync(page, selected.Index, default);
        Check.That(next == replacement, "The working copy lost the text object needed for another edit.");
        const string final = "quick green fox";
        using var twiceEdited = await workspace.EditTextCopyAsync(edited, edited.History.Current, page,
            new PdfTextObject(selected.Index, next!), replacement.Replace(after, final, StringComparison.Ordinal), default);
        Check.That((await twiceEdited.Pdf.SearchAsync([page], final, default)).Count == 1 &&
            (await twiceEdited.Pdf.SearchAsync([page], after, default)).Count == 0,
            "A second edit did not replace the text in the working copy.");
        using var scan = await _renderer.OpenAsync(Fixture("scanned-en-de.pdf"), null, default);
        Check.That(await ((IAdvancedPdfEditEngine)scan).FindTextObjectAsync(page, new(80, 700), default) is null,
            "Image-only scan was treated as a text object.");
    }

    public async Task StructuredWord()
    {
        using var test = new CoreScenarios.Scratch();
        using var work = test.Workspace.Create();
        var workspace = new PdfWorkspace(_renderer, null!, null!, null!, new ZipProjectStore(test.Workspace), test.Workspace);
        using var document = await workspace.OpenAsync(Fixture("word-layout.pdf"), null, false, default);
        var target = work.File("layout.docx");
        var project = document.History.Current;
        Check.That(await workspace.ExportWordAsync(document, project, target, "Word conversion structure", new(), null, default) == 0,
            "The editable text pages were reported as scans.");

        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using (var archive = ZipFile.OpenRead(target))
        {
            var xml = Part(archive, "word/document.xml");
            var text = xml.Descendants(word + "t").Select(element => element.Value).ToArray();
            var heading = xml.Descendants(word + "p").Single(p => p.Descendants(word + "t").Any(t => t.Value == "Project overview"));
            Check.That(heading.Descendants(word + "shd").SingleOrDefault()?.Attribute(word + "fill")?.Value == "1757AD" &&
                heading.Descendants(word + "color").SingleOrDefault()?.Attribute(word + "val")?.Value == "FFFFFF",
                "The editable Word heading lost its blue background or white text.");
            var mixedColor = xml.Descendants(word + "p").Single(p => string.Concat(p.Descendants(word + "t").Select(t => t.Value)) ==
                "Red accent and green text");
            Check.That(mixedColor.Descendants(word + "color").Select(c => c.Attribute(word + "val")?.Value)
                .SequenceEqual(["A82746", "177044"]), "Different PDF text colors were flattened into one Word run.");
            Check.That(text.Contains("This paragraph starts here and continues on a second PDF line."),
                "Wrapped PDF lines did not become an editable paragraph.");
            Check.That(xml.Descendants(word + "tbl").Single().Elements(word + "tr").Count() == 3 &&
                text.Contains("Berlin") && text.Contains("15 EUR"), "Aligned PDF rows did not become an editable table.");
            Check.That(xml.Descendants(word + "numPr").Count() == 2 && text.Contains("7. Original list marker") &&
                archive.GetEntry("word/numbering.xml") is not null, "Bullets or numbered markers changed.");
            Check.That(xml.Descendants(word + "tabs").Count() == 6 && Array.IndexOf(text, "Results") < Array.IndexOf(text, "Appendix"),
                "The two-column contents did not become an ordered list of dotted Word tabs.");
            Check.That(xml.Descendants(word + "b").Any() && xml.Descendants(word + "i").Any() &&
                xml.Descendants(word + "drawing").Count() == 2 && text.Contains("Chart note") &&
                archive.Entries.Count(item => item.FullName.StartsWith("word/media/") && item.Length > 100) == 2,
                "PDF font emphasis, editable text, the visible image or the vector artwork was dropped.");
            Check.That(!text.Contains("Page 1 of 2") && !text.Contains("Page 2 of 2") &&
                Part(archive, "word/footer1.xml").Descendants(word + "fldSimple").Single().Attribute(word + "instr")?.Value == "PAGE",
                "Repeated page numbers were not moved to a dynamic Word footer.");
        }

        var layout = await ((IPdfPageLayoutSource)document.Pdf).ReadPageLayoutAsync(project.Pages[0], true, default);
        var chart = layout.Images.Single(image => image.Bounds.X > 400).Raster;
        var redPixels = 0;
        var darkPixels = 0;
        for (var i = 0; i < chart.Bgra.Length; i += 4)
        {
            if (chart.Bgra[i + 2] > 130 && chart.Bgra[i + 1] < 95)
                redPixels++;
            if (chart.Bgra[i] < 40 && chart.Bgra[i + 1] < 40 && chart.Bgra[i + 2] < 40)
                darkPixels++;
        }
        Check.That(redPixels > 100 && darkPixels == 0, "The vector image lost its color or also rasterized the editable chart label.");
        Check.That((await document.Pdf.SearchAsync(project.Pages, "Chart note", default)).Count == 1,
            "Graphics extraction changed text visibility in the source document.");

        using var scan = await workspace.OpenAsync(Fixture("scanned-en-de.pdf"), null, false, default);
        var scanWord = work.File("scan-editable.docx");
        Check.That(await workspace.ExportWordAsync(scan, scan.History.Current, scanWord, "Scan", new(), null, default) == 2,
            "Image-only pages were not reported as lacking editable text.");
        using (var archive = ZipFile.OpenRead(scanWord))
            Check.That(Part(archive, "word/document.xml").Descendants(word + "drawing").Count() == 2,
                "The editable export dropped image-only scanned pages.");

        var visualProject = PageEdits.Rotate(project, project.Pages[1].Id, 90);
        var visual = work.File("original.docx");
        await workspace.ExportDocumentAsync(document, visualProject, DocumentExportFormat.Word, visual, "Original layout", true, null, default);
        using (var archive = ZipFile.OpenRead(visual))
        {
            XNamespace drawing = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
            var xml = Part(archive, "word/document.xml");
            var sizes = xml.Descendants(word + "pgSz").ToArray();
            Check.That(sizes.Length == 2 && sizes[0].Attribute(word + "w")?.Value == "12240" &&
                sizes[0].Attribute(word + "h")?.Value == "15840" && sizes[1].Attribute(word + "w")?.Value == "15840" &&
                sizes[1].Attribute(word + "h")?.Value == "12240" &&
                xml.Descendants(word + "pgMar").All(margins => margins.Attributes().All(value => value.Value == "0")) &&
                xml.Descendants(drawing + "anchor").Count() == 2,
                "Original layout did not preserve page sizes, rotation or full-bleed placement.");
            for (var index = 0; index < visualProject.Pages.Length; index++)
            {
                var raster = await document.Pdf.RenderAsync(visualProject.Pages[index], 300, default);
                var png = work.File($"page-{index}.png");
                await RasterPng.WriteAsync(raster, 300, png, default);
                using var image = archive.GetEntry($"word/media/page-{index + 1:D4}.png")!.Open();
                using var actual = new MemoryStream();
                await image.CopyToAsync(actual);
                var expected = await File.ReadAllBytesAsync(png);
                Check.That(actual.ToArray().SequenceEqual(expected),
                    "Original layout did not embed the unchanged rendered PDF page.");
            }
        }

        var canceled = new CancellationTokenSource();
        using (canceled)
        {
            var previous = await File.ReadAllBytesAsync(target);
            await Check.FailsAsync<OperationCanceledException>(() =>
                workspace.ExportWordAsync(document, project, target, "Copy", new(), new InlineProgress(_ => canceled.Cancel()), canceled.Token));
            Check.That((await File.ReadAllBytesAsync(target)).SequenceEqual(previous),
                "Canceled Word conversion overwrote the previous file.");
        }

        static XDocument Part(ZipArchive archive, string name)
        {
            using var entry = archive.GetEntry(name)!.Open();
            return XDocument.Load(entry);
        }
    }

    public async Task Ocr(bool german)
    {
        using var test = new CoreScenarios.Scratch();
        using var work = test.Workspace.Create();
        var structure = new QpdfStructure(qpdf, test.Workspace);
        var engine = new TesseractOcr(tesseract, tessdata, _renderer, structure, test.Workspace);
        var input = Fixture("scanned-en-de.pdf");
        var pages = new PageReference[] { new(Guid.NewGuid(), 0), new(Guid.NewGuid(), 1) };
        using var before = await _renderer.OpenAsync(input, null, default);
        Check.That((await before.SearchAsync(pages, "lighthouse", default)).Count == 0, "OCR fixture has pre-existing text.");
        var output = work.File("recognized.pdf");
        await engine.CreateSearchablePdfAsync(input, output, german ? [0, 1] : [0], german ? "eng+deu" : "eng", 300, null, default);
        await structure.ValidateAsync(output, default);
        using var after = await _renderer.OpenAsync(output, null, default);
        Check.That(after.Pages.Count == 2 && (await after.SearchAsync(pages, "lighthouse", default)).Count == 1,
            "English scan did not become searchable.");
        Check.That(Math.Abs(after.Pages[0].Width - before.Pages[0].Width) < .25, "OCR changed physical page size.");
        if (german)
            Check.That((await after.SearchAsync(pages, "Leuchtturm", default)).Count == 1, "German scan did not become searchable.");
        else
            Check.That((await after.SearchAsync(pages, "Leuchtturm", default)).Count == 0 && after.Pages[1] == before.Pages[1],
                "OCR changed an unselected page.");
    }

    public async Task CancelOcr()
    {
        using var test = new CoreScenarios.Scratch();
        using var work = test.Workspace.Create();
        var output = work.File("canceled.pdf");
        using var cancellation = new CancellationTokenSource();
        var engine = new TesseractOcr(tesseract, tessdata, _renderer, new QpdfStructure(qpdf, test.Workspace), test.Workspace);
        var progress = new InlineProgress(_ => cancellation.Cancel());
        await Check.FailsAsync<OperationCanceledException>(() =>
            engine.CreateSearchablePdfAsync(Fixture("scanned-en-de.pdf"), output, [0], "eng", 300, progress, cancellation.Token));
        Check.That(!File.Exists(output), "Canceled OCR published a partial PDF.");
    }

    public async Task ConvertDocuments()
    {
        using var test = new CoreScenarios.Scratch();
        using var work = test.Workspace.Create();
        var structure = new QpdfStructure(qpdf, test.Workspace);
        var ocr = new TesseractOcr(tesseract, tessdata, _renderer, structure, test.Workspace);
        // These scenarios exercise conversion of PDFium text and rasters without rendered overlays.
        var workspace = new PdfWorkspace(_renderer, structure, null!, ocr, new ZipProjectStore(test.Workspace), test.Workspace);
        using var document = await workspace.OpenAsync(Fixture("example.pdf"), null, false, default);
        var project = PdfWorkspace.SelectPages(document.History.Current, [3, 1, 0]);
        var addedText = new OverlayObject(Guid.NewGuid(), project.Pages[0].Id, OverlayKind.Text, new(10, 10, 90, 20),
            0xff172638, "Grüße & <text> 😀\u0001");
        project = project with { Overlays = [addedText] };

        var word = work.File("text.docx");
        var epub = work.File("text.epub");
        await workspace.ExportDocumentAsync(document, project, DocumentExportFormat.Word, word, "Example", false, null, default);
        await workspace.ExportDocumentAsync(document, project, DocumentExportFormat.Epub, epub, "Example", false, null, default);

        using (var archive = ZipFile.OpenRead(word))
        {
            XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var xml = ReadXml(archive, "word/document.xml");
            var lines = xml.Descendants(w + "t").Select(element => element.Value).ToArray();
            var starts = xml.Descendants(w + "pageBreakBefore")
                .Select(element => element.Parent!.Parent!.Descendants(w + "t").Single().Value).ToArray();
            Check.That(lines[0].Contains("Page 4") && starts.SequenceEqual(["PerplexPDF example / Page 2", "PerplexPDF example / Page 1"]) &&
                lines.Contains("Grüße & <text> 😀"),
                "Word export lost the page order, page break, or editable Unicode text.");
        }
        using (var archive = ZipFile.OpenRead(epub))
        {
            XNamespace html = "http://www.w3.org/1999/xhtml";
            XNamespace opf = "http://www.idpf.org/2007/opf";
            Check.That(archive.Entries[0].FullName == "mimetype" &&
                archive.Entries[0].Length == archive.Entries[0].CompressedLength, "EPUB media type entry is misplaced or compressed.");
            var book = ReadXml(archive, "EPUB/book.opf");
            var navigation = ReadXml(archive, "EPUB/nav.xhtml").Descendants(html + "a")
                .Select(link => link.Attribute("href")!.Value).ToArray();
            var manifest = book.Descendants(opf + "item")
                .ToDictionary(item => item.Attribute("id")!.Value, item => item.Attribute("href")!.Value);
            Check.That(book.Descendants(opf + "itemref").Select(item => manifest[item.Attribute("idref")!.Value])
                .SequenceEqual(navigation), "EPUB navigation does not follow its reading order.");
            var text = navigation.SelectMany(href => ReadXml(archive, "EPUB/" + href).Descendants(html + "body")
                .SelectMany(body => body.Elements()).Select(block => block.Value)).ToArray();
            Check.That(text.Where(line => line.StartsWith("PerplexPDF example / Page ", StringComparison.Ordinal))
                .SequenceEqual(["PerplexPDF example / Page 4", "PerplexPDF example / Page 2", "PerplexPDF example / Page 1"]) &&
                text.Contains("Grüße & <text> 😀"), "EPUB export lost page order or editable Unicode text.");
        }

        using var tagged = await workspace.OpenAsync(Fixture("epub-structure.pdf"), null, false, default);
        var chapters = work.File("chapters.epub");
        await workspace.ExportDocumentAsync(tagged, tagged.History.Current, DocumentExportFormat.Epub, chapters, "Chapters", false, null, default);
        using (var archive = ZipFile.OpenRead(chapters))
        {
            XNamespace html = "http://www.w3.org/1999/xhtml";
            XNamespace opf = "http://www.idpf.org/2007/opf";
            var nav = ReadXml(archive, "EPUB/nav.xhtml").Descendants(html + "a").ToArray();
            Check.That(nav.Select(link => link.Value).SequenceEqual(["First chapter", "Second chapter", "Third chapter"]),
                "EPUB did not use the PDF's H1 tags as chapter boundaries.");
            var parts = nav.Select(link => ReadXml(archive, "EPUB/" + link.Attribute("href")!.Value)).ToArray();
            Check.That(parts[0].Descendants(html + "h2").Single().Value == "A larger subheading" &&
                parts[1].Descendants(html + "p").Any(element => element.Value == "This page continues the second chapter."),
                "EPUB split a chapter at a subheading or a page boundary.");
            var pictures = parts.SelectMany(part => part.Descendants(html + "img")).Select(image => image.Attribute("src")!.Value).ToArray();
            var manifest = ReadXml(archive, "EPUB/book.opf").Descendants(opf + "item").ToArray();
            Check.That(pictures.Length == 2 && pictures.All(path => archive.GetEntry("EPUB/" + path) is { Length: > 100 } &&
                manifest.Any(item => item.Attribute("href")?.Value == path && item.Attribute("media-type")?.Value == "image/png")),
                "EPUB chapter images are missing or unregistered.");
        }

        using var scan = await workspace.OpenAsync(Fixture("scanned-en-de.pdf"), null, false, default);
        var scanWord = work.File("scan.docx");
        await workspace.ExportDocumentAsync(scan, scan.History.Current, DocumentExportFormat.Word, scanWord, "Scan", false, null, default);
        using (var archive = ZipFile.OpenRead(scanWord))
        {
            XNamespace relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
            Check.That(archive.GetEntry("word/media/page-0001.png") is { Length: > 100 } &&
                archive.GetEntry("word/media/page-0002.png") is { Length: > 100 } &&
                ReadXml(archive, "word/_rels/document.xml.rels").Descendants(relationships + "Relationship").Count() == 2,
                "Word conversion dropped image-only pages.");
        }

        var imageBook = work.File("images.epub");
        var singlePage = PdfWorkspace.SelectPages(document.History.Current, [1]);
        await workspace.ExportDocumentAsync(document, singlePage, DocumentExportFormat.Epub, imageBook, "Images", true, null, default);
        using (var archive = ZipFile.OpenRead(imageBook))
        {
            XNamespace html = "http://www.w3.org/1999/xhtml";
            using var image = archive.GetEntry("EPUB/images/page-0001.png")!.Open();
            var header = new byte[24];
            image.ReadExactly(header);
            Check.That(BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16)) == 874 &&
                BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20)) == 1342 &&
                ReadXml(archive, "EPUB/page-0001.xhtml").Descendants(html + "img").Count() == 1,
                "Image mode failed to export the rendered PDF page.");
        }

        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cancellation.Cancel());
        var originalWord = await File.ReadAllBytesAsync(word);
        await Check.FailsAsync<OperationCanceledException>(() =>
            workspace.ExportDocumentAsync(document, project, DocumentExportFormat.Word, word, "Example", false, progress, cancellation.Token));
        Check.That((await File.ReadAllBytesAsync(word)).SequenceEqual(originalWord), "Canceled conversion changed the previous file.");
    }

    private static XDocument ReadXml(ZipArchive archive, string name)
    {
        using var stream = archive.GetEntry(name)!.Open();
        return XDocument.Load(stream);
    }

    private sealed class InlineProgress(Action<WorkProgress> action) : IProgress<WorkProgress>
    {
        public void Report(WorkProgress value) => action(value);
    }
}
