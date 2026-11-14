using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PerplexPDF.Domain;

namespace PerplexPDF.Application;

internal sealed record ExportImage(byte[] Png, PageGeometry Geometry);
internal sealed record ExportPage(string[] Lines, ExportImage? Image, IReadOnlyList<EpubBlock>? EpubContent = null);
internal sealed record EpubBlock(double Top, string Text, int Heading = 0, bool Bold = false, bool Italic = false, byte[]? Picture = null);

internal static class DocumentExport
{
    private const string OfficeRelationship = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const int WordWidth = 11906;
    private const int WordHeight = 16838;
    private const int WordMargin = 960;
    private const int WordMaxPageTwips = 31680;
    private const int EmuPerTwip = 635;
    private static readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Relations = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Core = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Office = OfficeRelationship.TrimEnd('/');
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace WordDrawing = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace Picture = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace Package = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Html = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Epub = "http://www.idpf.org/2007/ops";
    private static readonly XmlWriterSettings XmlSettings = new() { Encoding = new UTF8Encoding(false) };

    internal static string XmlText(string value)
    {
        var text = new StringBuilder(value.Length);
        Span<char> pair = stackalloc char[2];
        foreach (var rune in value.EnumerateRunes())
        {
            if (!rune.IsBmp)
                text.Append(pair[..rune.EncodeToUtf16(pair)]);
            else if (XmlConvert.IsXmlChar((char)rune.Value))
                text.Append((char)rune.Value);
        }
        return text.ToString();
    }

    internal static string[] Lines(string text) => XmlText(text).ReplaceLineEndings("\n").Replace('\t', ' ')
        .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    internal static async Task WriteWordAsync(string output, string documentXml, string title, string author,
        IAsyncEnumerable<ExportPage> pages, int originalLayoutPageCount, CancellationToken ct)
    {
        using var archive = ZipFile.Open(output, ZipArchiveMode.Create);
        var relationships = new XElement(Relations + "Relationships");
        var originalLayout = originalLayoutPageCount > 0;
        var section = WordSection(WordWidth, WordHeight, fullPage: false);
        var imageCount = 0;
        var pageNumber = 0;
        // The document part is staged separately so images can be written to the ZIP as pages arrive.
        using (var xml = XmlWriter.Create(documentXml, XmlSettings))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("w", "document", Word.NamespaceName);
            xml.WriteAttributeString("xmlns", "r", null, Office.NamespaceName);
            xml.WriteAttributeString("xmlns", "wp", null, WordDrawing.NamespaceName);
            xml.WriteAttributeString("xmlns", "a", null, Drawing.NamespaceName);
            xml.WriteAttributeString("xmlns", "pic", null, Picture.NamespaceName);
            xml.WriteStartElement("w", "body", Word.NamespaceName);

            await foreach (var page in pages.WithCancellation(ct))
            {
                pageNumber++;
                if (page.Image is { } image)
                {
                    var id = $"page{pageNumber}";
                    var path = $"media/page-{pageNumber:D4}.png";
                    await WriteBytesAsync(archive, "word/" + path, image.Png, ct);
                    relationships.Add(Relationship(id, OfficeRelationship + "image", path));
                    imageCount++;
                    if (originalLayout)
                    {
                        // Word supports pages up to 22 inches. Fit larger PDFs proportionally.
                        var scale = Math.Min(20, WordMaxPageTwips / Math.Max(image.Geometry.Width, image.Geometry.Height));
                        var width = Math.Max(1, (int)Math.Round(image.Geometry.Width * scale));
                        var height = Math.Max(1, (int)Math.Round(image.Geometry.Height * scale));
                        section = WordSection(width, height, fullPage: true);
                        var run = WordImage(width * EmuPerTwip, height * EmuPerTwip, id, pageNumber, fullPage: true);
                        // Anchor to the page so the image does not consume a line of body space.
                        new XElement(Word + "p", new XElement(Word + "pPr",
                            new XElement(Word + "spacing", new XAttribute(Word + "before", 0), new XAttribute(Word + "after", 0),
                                new XAttribute(Word + "line", 20), new XAttribute(Word + "lineRule", "exact")),
                            pageNumber < originalLayoutPageCount ? section : null), run).WriteTo(xml);
                    }
                    else
                    {
                        var size = image.Geometry;
                        var scale = Math.Min(12_700, Math.Min((WordWidth - 2 * WordMargin) * EmuPerTwip / size.Width,
                            (WordHeight - 2 * WordMargin - 360) * EmuPerTwip / size.Height));
                        WordParagraph(pageNumber > 1, WordImage((long)Math.Round(size.Width * scale),
                            (long)Math.Round(size.Height * scale), id, pageNumber, fullPage: false)).WriteTo(xml);
                    }
                }
                else
                {
                    for (var index = 0; index < page.Lines.Length; index++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var run = new XElement(Word + "r", new XElement(Word + "t",
                            new XAttribute(XNamespace.Xml + "space", "preserve"), page.Lines[index]));
                        WordParagraph(pageNumber > 1 && index == 0, run).WriteTo(xml);
                    }
                }
            }

            section.WriteTo(xml);
            xml.WriteEndDocument();
        }

        ct.ThrowIfCancellationRequested();
        using (var entry = archive.CreateEntry("word/document.xml").Open())
        await using (var source = File.OpenRead(documentXml))
            await source.CopyToAsync(entry, ct);
        if (originalLayout)
        {
            relationships.Add(Relationship("settings", OfficeRelationship + "settings", "settings.xml"));
            WriteXml(archive, "word/settings.xml", new XElement(Word + "settings", new XElement(Word + "doNotAutoCompressPictures")));
        }
        if (relationships.HasElements)
            WriteXml(archive, "word/_rels/document.xml.rels", relationships);
        WriteXml(archive, "[Content_Types].xml", new XElement(Types + "Types",
            DefaultType("rels", "application/vnd.openxmlformats-package.relationships+xml"),
            DefaultType("xml", "application/xml"),
            imageCount > 0 ? DefaultType("png", "image/png") : null,
            OverrideType("/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"),
            originalLayout ? OverrideType("/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml") : null,
            OverrideType("/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml")));
        WriteXml(archive, "_rels/.rels", new XElement(Relations + "Relationships",
            Relationship("document", OfficeRelationship + "officeDocument", "word/document.xml"),
            Relationship("properties", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml")));
        WriteXml(archive, "docProps/core.xml", new XElement(Core + "coreProperties",
            new XElement(Dc + "title", title), author.Length == 0 ? null : new XElement(Dc + "creator", author)));
    }

    private static XElement WordParagraph(bool pageBreak, XElement run) => new(Word + "p",
        new XElement(Word + "pPr",
            pageBreak ? new XElement(Word + "pageBreakBefore") : null,
            new XElement(Word + "spacing", new XAttribute(Word + "after", 80))),
        run);

    private static XElement WordSection(int width, int height, bool fullPage)
    {
        var margin = fullPage ? 0 : WordMargin;
        var header = fullPage ? 0 : 360;
        return new XElement(Word + "sectPr",
            fullPage ? new XElement(Word + "type", new XAttribute(Word + "val", "nextPage")) : null,
            new XElement(Word + "pgSz", new XAttribute(Word + "w", width), new XAttribute(Word + "h", height),
                width > height ? new XAttribute(Word + "orient", "landscape") : null),
            new XElement(Word + "pgMar", new XAttribute(Word + "top", margin), new XAttribute(Word + "bottom", margin),
                new XAttribute(Word + "left", margin), new XAttribute(Word + "right", margin),
                new XAttribute(Word + "header", header), new XAttribute(Word + "footer", header), new XAttribute(Word + "gutter", 0)));
    }

    private static XElement WordImage(long width, long height, string relationshipId, int page, bool fullPage)
    {
        var label = $"PDF page {page}";
        var picture = new XElement(Picture + "pic",
            new XElement(Picture + "nvPicPr",
                new XElement(Picture + "cNvPr", new XAttribute("id", page), new XAttribute("name", label)),
                new XElement(Picture + "cNvPicPr")),
            new XElement(Picture + "blipFill",
                new XElement(Drawing + "blip", new XAttribute(Office + "embed", relationshipId)),
                new XElement(Drawing + "stretch", new XElement(Drawing + "fillRect"))),
            new XElement(Picture + "spPr",
                new XElement(Drawing + "xfrm",
                    new XElement(Drawing + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                    Extent(Drawing + "ext", width, height)),
                new XElement(Drawing + "prstGeom", new XAttribute("prst", "rect"), new XElement(Drawing + "avLst"))));
        var placement = fullPage
            ? new XElement(WordDrawing + "anchor", new XAttribute("simplePos", 0), new XAttribute("relativeHeight", 0),
                new XAttribute("behindDoc", 0), new XAttribute("locked", 0), new XAttribute("layoutInCell", 1), new XAttribute("allowOverlap", 1),
                new XElement(WordDrawing + "simplePos", new XAttribute("x", 0), new XAttribute("y", 0)),
                new XElement(WordDrawing + "positionH", new XAttribute("relativeFrom", "page"), new XElement(WordDrawing + "posOffset", 0)),
                new XElement(WordDrawing + "positionV", new XAttribute("relativeFrom", "page"), new XElement(WordDrawing + "posOffset", 0)))
            : new XElement(WordDrawing + "inline");
        placement.Add(
            Extent(WordDrawing + "extent", width, height),
            fullPage ? new XElement(WordDrawing + "wrapNone") : null,
            new XElement(WordDrawing + "docPr", new XAttribute("id", page), new XAttribute("name", label), new XAttribute("descr", label)),
            new XElement(Drawing + "graphic", new XElement(Drawing + "graphicData",
                new XAttribute("uri", Picture.NamespaceName), picture)));
        return new XElement(Word + "r", new XElement(Word + "drawing", placement));
    }

    internal static async Task WriteEpubAsync(string output, string title, string author,
        IAsyncEnumerable<ExportPage> pages, bool reflow, CancellationToken ct)
    {
        using var archive = ZipFile.Open(output, ZipArchiveMode.Create);
        await WriteBytesAsync(archive, "mimetype", "application/epub+zip"u8.ToArray(), ct);
        WriteXml(archive, "META-INF/container.xml", new XElement(Container + "container", new XAttribute("version", "1.0"),
            new XElement(Container + "rootfiles", new XElement(Container + "rootfile",
                new XAttribute("full-path", "EPUB/book.opf"), new XAttribute("media-type", "application/oebps-package+xml")))));

        var manifest = new XElement(Package + "manifest", ManifestItem("nav", "nav.xhtml", "application/xhtml+xml", "nav"));
        var spine = new XElement(Package + "spine");
        var links = new XElement(Html + "ol");
        var number = 0;
        var sections = 0;
        var images = 0;
        var label = title;
        var hasHeading = false;
        var body = new XElement(Html + "body");
        await foreach (var page in pages.WithCancellation(ct))
        {
            ct.ThrowIfCancellationRequested();
            number++;
            if (!hasHeading)
            {
                Flush();
                label = $"Page {number}";
            }
            if (page.Image is { } image)
                await AddPicture(image.Png, $"PDF page {number}", reflow ? $"figure-{++images:D4}" : $"page-{number:D4}");
            else if (page.EpubContent is { } content)
                foreach (var block in content)
                {
                    ct.ThrowIfCancellationRequested();
                    if (block.Heading == 1)
                    {
                        Flush();
                        label = block.Text;
                        hasHeading = true;
                    }
                    if (block.Picture is { } picture)
                        await AddPicture(picture, block.Text, $"figure-{++images:D4}");
                    else
                    {
                        object text = block.Text;
                        if (block.Bold) text = new XElement(Html + "strong", text);
                        if (block.Italic) text = new XElement(Html + "em", text);
                        body.Add(new XElement(Html + (block.Heading == 0 ? "p" : $"h{block.Heading}"), text));
                    }
                }
            else
                body.Add(page.Lines.Select(line => new XElement(Html + "p", line)));
        }
        Flush();

        ct.ThrowIfCancellationRequested();
        WriteXml(archive, "EPUB/nav.xhtml", HtmlDocument(title, "en", new XElement(Html + "body",
            new XElement(Html + "nav", new XAttribute(XNamespace.Xmlns + "epub", Epub.NamespaceName),
                new XAttribute(Epub + "type", "toc"), new XElement(Html + "h1", "Contents"), links))));
        WriteXml(archive, "EPUB/book.opf", new XElement(Package + "package", new XAttribute("version", "3.0"),
            new XAttribute("unique-identifier", "publication-id"),
            new XElement(Package + "metadata",
                new XElement(Dc + "identifier", new XAttribute("id", "publication-id"), $"urn:uuid:{Guid.NewGuid():D}"),
                new XElement(Dc + "title", title), new XElement(Dc + "language", "und"),
                author.Length == 0 ? null : new XElement(Dc + "creator", author),
                new XElement(Package + "meta", new XAttribute("property", "dcterms:modified"),
                    DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))),
            manifest, spine));

        async Task AddPicture(byte[] png, string alt, string id)
        {
            var path = $"images/{id}.png";
            await WriteBytesAsync(archive, "EPUB/" + path, png, ct);
            manifest.Add(ManifestItem(id + "-image", path, "image/png"));
            body.Add(new XElement(Html + "figure", new XElement(Html + "img",
                new XAttribute("src", path), new XAttribute("alt", alt))));
        }

        void Flush()
        {
            if (!body.HasElements) return;
            var id = $"page-{++sections:D4}";
            var href = id + ".xhtml";
            WriteXml(archive, "EPUB/" + href, HtmlDocument(label, "und", body));
            manifest.Add(ManifestItem(id, href, "application/xhtml+xml"));
            spine.Add(new XElement(Package + "itemref", new XAttribute("idref", id)));
            links.Add(new XElement(Html + "li", new XElement(Html + "a", new XAttribute("href", href), label)));
            body = new XElement(Html + "body");
        }
    }

    private static XElement HtmlDocument(string title, string language, XElement body) => new(Html + "html",
        new XAttribute("lang", language), new XAttribute(XNamespace.Xml + "lang", language),
        new XElement(Html + "head", new XElement(Html + "title", title),
            new XElement(Html + "style", "p { margin: 0 0 0.45em; } img { display: block; max-width: 100%; height: auto; margin: auto; }")),
        body);

    private static XElement ManifestItem(string id, string href, string type, string? properties = null) => new(Package + "item",
        new XAttribute("id", id), new XAttribute("href", href), new XAttribute("media-type", type),
        properties is null ? null : new XAttribute("properties", properties));

    private static XElement DefaultType(string extension, string type) => new(Types + "Default",
        new XAttribute("Extension", extension), new XAttribute("ContentType", type));

    private static XElement OverrideType(string path, string type) => new(Types + "Override",
        new XAttribute("PartName", path), new XAttribute("ContentType", type));

    private static XElement Relationship(string id, string type, string target) => new(Relations + "Relationship",
        new XAttribute("Id", id), new XAttribute("Type", type), new XAttribute("Target", target));

    private static XElement Extent(XName name, long width, long height) => new(name,
        new XAttribute("cx", width), new XAttribute("cy", height));

    private static void WriteXml(ZipArchive archive, string path, XElement root)
    {
        using var entry = archive.CreateEntry(path).Open();
        using var xml = XmlWriter.Create(entry, XmlSettings);
        new XDocument(root).WriteTo(xml);
    }

    private static async Task WriteBytesAsync(ZipArchive archive, string path, byte[] bytes, CancellationToken ct)
    {
        using var entry = archive.CreateEntry(path, CompressionLevel.NoCompression).Open();
        await entry.WriteAsync(bytes, ct);
    }
}
