using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PerplexPDF.Domain;

namespace PerplexPDF.Application;

internal sealed record WordExportPage(PageGeometry Geometry, IReadOnlyList<WordBlock> Blocks, bool HasEditableText);

internal static class WordDocx
{
    private const string OfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const int Margin = 720;
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace R = OfficeRel.TrimEnd('/');
    private static readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Relations = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Core = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Inline = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private static readonly XNamespace Picture = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private static readonly XmlWriterSettings Xml = new() { Encoding = new UTF8Encoding(false) };

    internal static async Task<int> WriteAsync(string output, string documentXml, string title, string author, WordExportOptions options,
        WordFooter? footer, IAsyncEnumerable<WordExportPage> pages, CancellationToken ct)
    {
        using var archive = ZipFile.Open(output, ZipArchiveMode.Create);
        var relations = new XElement(Relations + "Relationships", Relation("styles", OfficeRel + "styles", "styles.xml"));
        var types = new XElement(Types + "Types",
            Default("rels", "application/vnd.openxmlformats-package.relationships+xml"), Default("xml", "application/xml"),
            Override("/word/document.xml", "document.main"),
            Override("/word/styles.xml", "styles"),
            new XElement(Types + "Override", new XAttribute("PartName", "/docProps/core.xml"),
                new XAttribute("ContentType", "application/vnd.openxmlformats-package.core-properties+xml")));
        var width = 12240;
        var height = 15840;
        var imageIndex = 0;
        var pageCount = 0;
        var pagesWithoutText = 0;
        var usesBullets = false;
        using (var xml = XmlWriter.Create(documentXml, Xml))
        {
            xml.WriteStartDocument();
            xml.WriteStartElement("w", "document", W.NamespaceName);
            foreach (var (prefix, ns) in new[] { ("r", R), ("a", Drawing), ("wp", Inline), ("pic", Picture) })
                xml.WriteAttributeString("xmlns", prefix, null, ns.NamespaceName);
            xml.WriteStartElement("w", "body", W.NamespaceName);
            await foreach (var page in pages.WithCancellation(ct))
            {
                if (pageCount == 0)
                {
                    var scale = Math.Min(20, 31_680 / Math.Max(page.Geometry.Width, page.Geometry.Height));
                    width = (int)Math.Clamp(Math.Round(page.Geometry.Width * scale), 2_880, 31_680);
                    height = (int)Math.Clamp(Math.Round(page.Geometry.Height * scale), 2_880, 31_680);
                }
                else if (options.PageBreaks)
                {
                    new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "spacing", Attr("after", 0))),
                        new XElement(W + "r", new XElement(W + "br", Attr("type", "page")))).WriteTo(xml);
                }
                pageCount++;
                if (!page.HasEditableText)
                    pagesWithoutText++;
                if (page.Blocks.Count == 0)
                    new XElement(W + "p").WriteTo(xml);
                foreach (var block in page.Blocks)
                {
                    ct.ThrowIfCancellationRequested();
                    switch (block)
                    {
                        case WordParagraph paragraph:
                            usesBullets |= paragraph.Bullet;
                            Paragraph(paragraph, width).WriteTo(xml);
                            break;
                        case WordTable table:
                            Table(table, width).WriteTo(xml);
                            break;
                        case WordPicture picture:
                            imageIndex++;
                            var name = $"media/image-{imageIndex:D5}.png";
                            var id = $"image{imageIndex}";
                            using (var image = archive.CreateEntry("word/" + name, CompressionLevel.NoCompression).Open())
                                await RasterPng.WriteAsync(picture.Raster, 72 * picture.Raster.Width / picture.Geometry.Width, image, ct);
                            relations.Add(Relation(id, OfficeRel + "image", name));
                            Image(picture.Geometry, id, imageIndex, width, height).WriteTo(xml);
                            break;
                    }
                }
            }

            new XElement(W + "sectPr",
                footer is null ? null : new XElement(W + "footerReference", Attr("type", "default"), new XAttribute(R + "id", "footer")),
                new XElement(W + "pgSz", Attr("w", width), Attr("h", height), width > height ? Attr("orient", "landscape") : null),
                new XElement(W + "pgMar", Attr("top", Margin), Attr("bottom", Margin), Attr("left", Margin), Attr("right", Margin),
                    Attr("header", 360), Attr("footer", 360), Attr("gutter", 0)),
                footer is null ? null : new XElement(W + "pgNumType", Attr("fmt", "decimal"), Attr("start", footer.Start)),
                footer?.BlankFirstPage == true ? new XElement(W + "titlePg") : null).WriteTo(xml);
            xml.WriteEndDocument();
        }

        ct.ThrowIfCancellationRequested();
        using (var entry = archive.CreateEntry("word/document.xml").Open())
        await using (var file = File.OpenRead(documentXml))
            await file.CopyToAsync(entry, ct);
        if (usesBullets)
        {
            types.Add(Override("/word/numbering.xml", "numbering"));
            relations.Add(Relation("numbering", OfficeRel + "numbering", "numbering.xml"));
            Part(archive, "word/numbering.xml", Numbering());
        }
        if (footer is not null)
        {
            types.Add(Override("/word/footer1.xml", "footer"));
            relations.Add(Relation("footer", OfficeRel + "footer", "footer1.xml"));
            Part(archive, "word/footer1.xml", new XElement(W + "ftr",
                new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "jc", Attr("val", footer.Alignment))),
                    new XElement(W + "fldSimple", Attr("instr", "PAGE"), Run("1", new(10))))));
        }
        if (imageIndex != 0)
            types.Add(Default("png", "image/png"));
        Part(archive, "word/styles.xml", new XElement(W + "styles",
            new XElement(W + "docDefaults", new XElement(W + "rPrDefault", new XElement(W + "rPr",
                new XElement(W + "rFonts", Attr("ascii", "Calibri"), Attr("hAnsi", "Calibri")),
                new XElement(W + "sz", Attr("val", 22)))))));
        Part(archive, "word/_rels/document.xml.rels", relations);
        Part(archive, "[Content_Types].xml", types);
        Part(archive, "_rels/.rels", new XElement(Relations + "Relationships",
            Relation("document", OfficeRel + "officeDocument", "word/document.xml"),
            Relation("properties", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml")));
        Part(archive, "docProps/core.xml", new XElement(Core + "coreProperties", new XElement(Dc + "title", title),
            author.Length == 0 ? null : new XElement(Dc + "creator", author)));
        return pagesWithoutText;
    }

    private static XElement Paragraph(WordParagraph paragraph, int pageWidth)
    {
        var properties = new XElement(W + "pPr",
            paragraph.Heading ? new XElement(W + "keepNext") : null,
            paragraph.TocPage is not null ? new XElement(W + "keepLines") : null,
            paragraph.Bullet ? new XElement(W + "numPr",
                new XElement(W + "ilvl", Attr("val", paragraph.ListLevel ?? 0)),
                new XElement(W + "numId", Attr("val", 1))) : null,
            paragraph.TocPage is not null ? new XElement(W + "tabs",
                new XElement(W + "tab", Attr("val", "right"), Attr("leader", "dot"), Attr("pos", pageWidth - 2 * Margin))) : null,
            Shade(paragraph.Style.Background),
            new XElement(W + "spacing", Attr("before", paragraph.Heading ? 140 : 0),
                Attr("after", paragraph.TocPage is not null ? 35 : paragraph.Heading ? 110 : paragraph.ListLevel.HasValue ? 45 : 90),
                Attr("line", paragraph.TocPage is not null ? 250 : 276), Attr("lineRule", "auto")),
            !paragraph.Bullet && paragraph.ListLevel is { } level
                ? new XElement(W + "ind", Attr("left", 420 + level * 360), Attr("hanging", 300)) : null,
            paragraph.Heading ? new XElement(W + "outlineLvl", Attr("val", 0)) : null);
        var style = paragraph.Heading ? paragraph.Style with { Size = Math.Max(14, paragraph.Style.Size), Bold = true } : paragraph.Style;
        return new XElement(W + "p", properties, Runs(paragraph.Text, style, paragraph.Runs, paragraph.Heading),
            paragraph.TocPage is null ? null : new XElement(W + "r", new XElement(W + "tab")),
            paragraph.TocPage is null ? null : Run(paragraph.TocPage, style with { Bold = false, Italic = false }));
    }

    private static IEnumerable<XElement> Runs(string text, WordStyle style, IReadOnlyList<WordRun>? runs, bool heading = false) =>
        runs is null ? [Run(text, style)] : runs.Select(run => Run(run.Text, run.Style with
        {
            Size = style.Size, Bold = heading || run.Style.Bold, Background = style.Background
        }));

    private static XElement Run(string text, WordStyle style) => new(W + "r",
        new XElement(W + "rPr", style.Bold ? new XElement(W + "b") : null, style.Italic ? new XElement(W + "i") : null,
            style.Foreground is { } color && (color != 0xFFFFFF || style.Background.HasValue)
                ? new XElement(W + "color", Attr("val", $"{color:X6}")) : null,
            new XElement(W + "sz", Attr("val", (int)Math.Round(Math.Clamp(style.Size, 8, 28) * 2)))),
        new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));

    private static XElement? Shade(uint? color) => color is { } fill
        ? new XElement(W + "shd", Attr("val", "clear"), Attr("color", "auto"), Attr("fill", $"{fill:X6}")) : null;

    private static XElement Table(WordTable table, int pageWidth)
    {
        var columns = table.Rows[0].Length;
        var cellWidth = (pageWidth - 2 * Margin) / columns;
        return new XElement(W + "tbl",
            new XElement(W + "tblPr", new XElement(W + "tblW", Attr("w", 5000), Attr("type", "pct")),
                new XElement(W + "tblBorders", new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(edge =>
                    new XElement(W + edge, Attr("val", "single"), Attr("sz", 4), Attr("color", "B7B7B7")))),
                new XElement(W + "tblCellMar", new[] { ("top", 70), ("left", 90), ("bottom", 70), ("right", 90) }.Select(margin =>
                    new XElement(W + margin.Item1, Attr("w", margin.Item2), Attr("type", "dxa"))))),
            new XElement(W + "tblGrid", Enumerable.Range(0, columns).Select(_ => new XElement(W + "gridCol", Attr("w", cellWidth)))),
            table.Rows.Select(row => new XElement(W + "tr", row.Select(cell => new XElement(W + "tc",
                new XElement(W + "tcPr", new XElement(W + "tcW", Attr("w", cellWidth), Attr("type", "dxa")), Shade(cell.Style.Background)),
                new XElement(W + "p", new XElement(W + "pPr", new XElement(W + "spacing", Attr("after", 0))),
                    Runs(cell.Text, cell.Style, WordLayout.FormattedRuns([cell], cell.Style))))))));
    }

    private static XElement Image(PageGeometry geometry, string id, int index, int pageWidth, int pageHeight)
    {
        const int emuPerTwip = 635;
        const int emuPerPoint = 12_700;
        var scale = Math.Min(emuPerPoint, Math.Min((pageWidth - 2 * Margin) * emuPerTwip / geometry.Width,
            (pageHeight - 2 * Margin - 520) * emuPerTwip / geometry.Height));
        var width = (long)Math.Round(geometry.Width * scale);
        var height = (long)Math.Round(geometry.Height * scale);
        var label = $"PDF image {index}";
        var picture = new XElement(Picture + "pic",
            new XElement(Picture + "nvPicPr",
                new XElement(Picture + "cNvPr", new XAttribute("id", index), new XAttribute("name", label)),
                new XElement(Picture + "cNvPicPr")),
            new XElement(Picture + "blipFill", new XElement(Drawing + "blip", new XAttribute(R + "embed", id)),
                new XElement(Drawing + "stretch", new XElement(Drawing + "fillRect"))),
            new XElement(Picture + "spPr",
                new XElement(Drawing + "xfrm", new XElement(Drawing + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                    Extent(Drawing + "ext", width, height)),
                new XElement(Drawing + "prstGeom", new XAttribute("prst", "rect"), new XElement(Drawing + "avLst"))));
        return new XElement(W + "p", new XElement(W + "pPr",
                new XElement(W + "spacing", Attr("before", 100), Attr("after", 120)), new XElement(W + "jc", Attr("val", "center"))),
            new XElement(W + "r", new XElement(W + "drawing", new XElement(Inline + "inline",
                Extent(Inline + "extent", width, height),
                new XElement(Inline + "docPr", new XAttribute("id", index), new XAttribute("name", label), new XAttribute("descr", label)),
                new XElement(Drawing + "graphic", new XElement(Drawing + "graphicData",
                    new XAttribute("uri", Picture.NamespaceName), picture))))));
    }

    private static XElement Numbering() => new(W + "numbering",
        new XElement(W + "abstractNum", Attr("abstractNumId", 0), new XElement(W + "multiLevelType", Attr("val", "multilevel")),
            Enumerable.Range(0, 9).Select(level => new XElement(W + "lvl", Attr("ilvl", level),
                new XElement(W + "start", Attr("val", 1)), new XElement(W + "numFmt", Attr("val", "bullet")),
                new XElement(W + "lvlText", Attr("val", level % 2 == 0 ? "•" : "◦")),
                new XElement(W + "lvlJc", Attr("val", "left")),
                new XElement(W + "pPr", new XElement(W + "ind", Attr("left", 420 + level * 360), Attr("hanging", 300)))))),
        new XElement(W + "num", Attr("numId", 1), new XElement(W + "abstractNumId", Attr("val", 0))));

    private static XAttribute Attr(string name, object value) => new(W + name, value);
    private static XElement Extent(XName name, long width, long height) => new(name, new XAttribute("cx", width), new XAttribute("cy", height));
    private static XElement Default(string ext, string contentType) => new(Types + "Default",
        new XAttribute("Extension", ext), new XAttribute("ContentType", contentType));
    private static XElement Override(string path, string kind) => new(Types + "Override", new XAttribute("PartName", path),
        new XAttribute("ContentType", $"application/vnd.openxmlformats-officedocument.wordprocessingml.{kind}+xml"));
    private static XElement Relation(string id, string type, string target) => new(Relations + "Relationship",
        new XAttribute("Id", id), new XAttribute("Type", type), new XAttribute("Target", target));

    private static void Part(ZipArchive archive, string path, XElement root)
    {
        using var entry = archive.CreateEntry(path).Open();
        using var xml = XmlWriter.Create(entry, Xml);
        new XDocument(root).WriteTo(xml);
    }
}
