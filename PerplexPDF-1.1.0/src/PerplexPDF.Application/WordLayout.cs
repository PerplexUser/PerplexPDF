using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using PerplexPDF.Domain;

namespace PerplexPDF.Application;

public sealed record WordExportOptions(bool IncludeImages = true, bool DetectTables = true, bool PageBreaks = true,
    bool Reflow = true, bool DetectLists = true, bool MovePageNumbers = true);

internal readonly record struct WordStyle(double Size, bool Bold = false, bool Italic = false,
    uint? Foreground = null, uint? Background = null);
internal sealed record WordRun(string Text, WordStyle Style);
internal sealed record WordLine(double Baseline, PdfTextSpan[] Spans, WordStyle Style, string Text)
{
    internal double Top => Baseline - Style.Size;
    internal double Left => Spans.Length == 0 ? 0 : Spans[0].Left;
    internal double Right => Spans.Length == 0 ? 0 : Spans[^1].Right;
}
internal abstract record WordBlock(double Top);
internal sealed record WordParagraph(double Top, string Text, WordStyle Style, bool Heading = false,
    int? ListLevel = null, bool Bullet = false, string? TocPage = null, IReadOnlyList<WordRun>? Runs = null) : WordBlock(Top);
internal sealed record WordTable(double Top, WordLine[][] Rows) : WordBlock(Top);
internal sealed record WordPicture(double Top, RasterPage Raster, PageGeometry Geometry) : WordBlock(Top);
internal sealed record WordPageNumber(int Page, int Offset, WordLine Line, double Center);
internal sealed record WordFooter(int Start, string Alignment, bool BlankFirstPage, IReadOnlyDictionary<int, WordLine> Lines);

internal static class WordLayout
{
    private static readonly Regex Bullets = new(@"^(?:[•∙●○▪■◦‣⁃·\uF0B7\uF0A7\uF076\uF0D8]\s*|[–—-]\s+)(.+)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Numbered = new(@"^(?:[0-9]{1,4}|[A-Za-z]|[ivxlcdmIVXLCDM]{1,8})[.)]\s+.+$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex PageNumber = new(@"^(?:(?:page|seite)\s+|[-–—]\s*)?([0-9]{1,4})(?:\s*(?:of|von|/)\s*([0-9]{1,4})|\s*[-–—])?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking);
    private static readonly Regex Leader = new(@"[.·…](?:\s*[.·…]){2,}\s*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex TocAnchor = new(@"^[.·…\s]*([0-9]{1,4})(?:\s+(.+))?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex TocInline = new(@"(.+?)[.·…](?:\s*[.·…]){2,}\s*([0-9]{1,4})(?=\s|$)",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
    private static readonly string[] ContentsHeadings = ["inhaltsverzeichnis", "inhalt", "contents", "table of contents", "sommaire", "indice", "índice"];

    internal static WordLine[] Lines(PdfPageLayout layout)
    {
        if (layout.Spans.Count == 0)
            return layout.FallbackText.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(Clean).Where(text => text.Length != 0)
                .Select((text, index) => new WordLine(index * 13.0, [], new(11), text)).ToArray();

        var lines = new List<WordLine>();
        var items = new List<PdfTextSpan>();
        var baseline = 0d;
        foreach (var span in layout.Spans.OrderBy(item => item.Baseline).ThenBy(item => item.Left))
        {
            if (items.Count != 0 && Math.Abs(span.Baseline - baseline) > Math.Max(2.5, span.Size * .34))
                Flush();
            baseline = (baseline * items.Count + span.Baseline) / (items.Count + 1);
            items.Add(span);
        }
        Flush();
        return lines.ToArray();

        void Flush()
        {
            if (items.Count == 0)
                return;
            var line = Line(baseline, items.OrderBy(item => item.Left).ToArray());
            if (line.Text.Length != 0)
                lines.Add(line);
            items.Clear();
        }
    }

    internal static WordPageNumber? FooterCandidate(WordLine[] lines, PageGeometry geometry, int page, int pageCount)
    {
        foreach (var line in lines.Where(line => line.Top >= geometry.Height * .8 && line.Style.Size <= 20).OrderByDescending(line => line.Top))
        {
            if (line.Text.Length > 28)
                continue;
            var match = PageNumber.Match(line.Text);
            if (!match.Success)
                continue;
            var number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            if (match.Groups[2].Success && Math.Abs(int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) - pageCount) > 2)
                continue;
            return new WordPageNumber(page, number - page, line, (line.Left + line.Right) / (2 * geometry.Width));
        }
        return null;
    }

    internal static WordFooter? Footer(IEnumerable<WordPageNumber> candidates, int count)
    {
        var groups = candidates.GroupBy(item => item.Offset).OrderByDescending(group => group.Count()).ToArray();
        var minimum = count <= 3 ? 2 : Math.Max(2, (int)Math.Ceiling(count * .4));
        if (groups.Length == 0 || groups[0].Count() < minimum ||
            groups.Length > 1 && groups[1].Count() == groups[0].Count())
            return null;
        var group = groups[0].ToArray();
        var center = Median(group.Select(item => item.Center));
        return new(Math.Max(0, group[0].Offset + 1), center < .38 ? "left" : center > .62 ? "right" : "center",
            group.Min(item => item.Page) == 2, group.ToDictionary(item => item.Page, item => item.Line));
    }

    internal static IReadOnlyList<WordBlock> Blocks(WordLine[] lines, PdfPageLayout layout, WordExportOptions options)
    {
        var bodySize = BodyFontSize(lines);
        var images = layout.Images.Select(image => new WordPicture(image.Bounds.Y, image.Raster,
            new(image.Bounds.Width, image.Bounds.Height))).ToArray();
        if (TocBlocks(lines, layout.Geometry.Width, bodySize) is { } contents)
            return [.. contents, .. images];

        var blocks = new List<WordBlock>();
        var cells = options.DetectTables ? lines.Select(Cells).ToArray() : [];
        var listPositions = options.DetectLists ? ListIndents(lines) : [];
        for (var i = 0; i < lines.Length;)
        {
            var tableRows = options.DetectTables ? TableLength(lines, cells, i) : 0;
            if (tableRows != 0)
            {
                blocks.Add(new WordTable(lines[i].Top, cells[i..(i + tableRows)]));
                i += tableRows;
                continue;
            }

            var line = lines[i];
            var bullet = options.DetectLists ? Bullets.Match(line.Text) : Match.Empty;
            var numbered = options.DetectLists && Numbered.IsMatch(line.Text);
            var list = bullet.Success || numbered;
            var heading = !list && IsHeading(line, bodySize);
            var end = i + 1;
            while ((options.Reflow || list) && line.Spans.Length != 0 && !heading && end < lines.Length)
            {
                var previous = lines[end - 1];
                var next = lines[end];
                if (options.DetectTables && TableLength(lines, cells, end) != 0 ||
                    options.DetectLists && IsList(next.Text) || IsHeading(next, bodySize) ||
                    next.Top - previous.Top > Math.Max(previous.Style.Size * 1.75, 23) ||
                    Math.Abs(next.Style.Size - previous.Style.Size) > Math.Max(2.2, bodySize * .22) ||
                    next.Style.Foreground != previous.Style.Foreground || next.Style.Background != previous.Style.Background ||
                    images.Any(image => image.Top > previous.Top && image.Top < next.Top))
                    break;
                var indent = next.Left - (list ? line.Left : previous.Left);
                if (list ? indent < -2 || indent > Math.Max(70, bodySize * 5.5) : Math.Abs(indent) > Math.Max(28, bodySize * 2.2))
                    break;
                end++;
            }
            var text = bullet.Success ? bullet.Groups[1].Value : line.Text;
            if (end > i + 1)
                text += " " + string.Join(" ", lines[(i + 1)..end].Select(item => item.Text));
            var paragraphLines = lines[i..end];
            var style = ParagraphStyle(paragraphLines, heading);
            int? level = list ? Math.Min(8, Enumerable.Range(0, listPositions.Length)
                .MinBy(index => Math.Abs(listPositions[index] - line.Left))) : null;
            blocks.Add(new WordParagraph(line.Top, text, style, heading, level, bullet.Success,
                Runs: FormattedRuns(paragraphLines, style, bullet.Success ? bullet.Groups[1].Index : 0)));
            i = end;
        }
        return blocks.Concat(images).OrderBy(block => block.Top).ToArray();
    }

    private static WordLine Line(double baseline, PdfTextSpan[] spans)
    {
        var text = new StringBuilder();
        for (var i = 0; i < spans.Length; i++)
        {
            var span = spans[i];
            if (i != 0)
            {
                var previous = spans[i - 1];
                var gap = span.Left - previous.Right;
                if (gap > Math.Max(1.2, (span.Size + previous.Size) * .055) &&
                    !char.IsWhiteSpace(previous.Text[^1]) && !char.IsWhiteSpace(span.Text[0]))
                    text.Append(' ');
            }
            text.Append(span.Text);
        }
        var chars = Math.Max(1, spans.Sum(span => span.Text.Length));
        var color = spans.GroupBy(span => (span.Foreground, span.Background))
            .MaxBy(group => group.Sum(span => span.Text.Length))?.Key ?? default;
        var style = new WordStyle(Median(spans.Select(span => span.Size), 11),
            spans.Where(span => span.Bold).Sum(span => span.Text.Length) >= chars * .55,
            spans.Where(span => span.Italic).Sum(span => span.Text.Length) >= chars * .55, color.Foreground, color.Background);
        return new WordLine(baseline, spans, style, Clean(text.ToString()));
    }

    private static WordStyle ParagraphStyle(WordLine[] lines, bool heading)
    {
        var count = Math.Max(1, lines.Sum(line => line.Text.Length));
        return new(Median(lines.Select(line => line.Style.Size), 11),
            heading || lines.Where(line => line.Style.Bold).Sum(line => line.Text.Length) >= count * .55,
            lines.Where(line => line.Style.Italic).Sum(line => line.Text.Length) >= count * .55,
            lines[0].Style.Foreground, lines[0].Style.Background);
    }

    internal static IReadOnlyList<WordRun>? FormattedRuns(WordLine[] lines, WordStyle style, int firstLineStart = 0)
    {
        if (!lines.Any(line => line.Spans.Any(span => span.Foreground != style.Foreground ||
            span.Bold != line.Style.Bold || span.Italic != line.Style.Italic)))
            return null;

        var runs = new List<WordRun>();
        foreach (var line in lines)
        {
            if (runs.Count != 0)
                runs.Add(new(" ", style));
            if (line.Spans.Length == 0)
            {
                runs.Add(new(line.Text[firstLineStart..], line.Style));
                firstLineStart = 0;
                continue;
            }
            var offset = 0;
            foreach (var span in line.Spans)
            {
                var text = Clean(span.Text);
                if (text.Length == 0)
                    continue;
                var start = offset;
                if (offset < line.Text.Length && line.Text[offset] == ' ')
                    offset++;
                if (!line.Text.AsSpan(offset).StartsWith(text, StringComparison.Ordinal))
                    return null;
                offset += text.Length;
                if (offset > firstLineStart)
                    runs.Add(new(line.Text[Math.Max(start, firstLineStart)..offset],
                        style with { Foreground = span.Foreground, Bold = span.Bold, Italic = span.Italic }));
            }
            if (offset != line.Text.Length)
                return null;
            firstLineStart = 0;
        }
        return runs.Count == 0 ? null : runs;
    }

    private static WordLine[] Cells(WordLine line)
    {
        var cells = new List<WordLine>();
        var start = 0;
        for (var index = 1; index < line.Spans.Length; index++)
        {
            var previous = line.Spans[index - 1];
            var current = line.Spans[index];
            if (current.Left - previous.Right > Math.Max(22, (previous.Size + current.Size) * 1.075))
            {
                cells.Add(Line(line.Baseline, line.Spans[start..index]));
                start = index;
            }
        }
        if (line.Spans.Length != 0)
            cells.Add(Line(line.Baseline, line.Spans[start..]));
        return cells.ToArray();
    }

    private static int TableLength(WordLine[] lines, WordLine[][] cells, int start)
    {
        var first = cells[start];
        if (first.Length is < 2 or > 8)
            return 0;
        var end = start + 1;
        while (end < lines.Length && cells[end].Length == first.Length &&
            lines[end].Top - lines[end - 1].Top <= Math.Max(lines[start].Style.Size * 2.6, 34))
        {
            var delta = first.Zip(cells[end], (a, b) => Math.Abs(a.Left - b.Left)).Average();
            if (delta > Math.Max(16, Math.Min(lines[start].Style.Size, lines[end].Style.Size) * 1.5))
                break;
            end++;
        }
        var rows = end - start;
        return rows >= 3 || rows >= 2 && first.Length >= 3 ? rows : 0;
    }

    private static bool IsList(string text) => Bullets.IsMatch(text) || Numbered.IsMatch(text);

    private static double[] ListIndents(IEnumerable<WordLine> lines)
    {
        var levels = new List<double>();
        foreach (var x in lines.Where(line => IsList(line.Text)).Select(line => line.Left).Order())
            if (levels.Count == 0 || Math.Abs(x - levels[^1]) > 16)
                levels.Add(x);
        return levels.ToArray();
    }

    internal static double BodyFontSize(WordLine[] lines) => Median(lines.Select(line => line.Style.Size), 11);

    internal static bool IsHeading(WordLine line, double bodySize) =>
        line.Text.Length <= 120 && line.Style.Size >= Math.Max(bodySize * 1.25, bodySize + 2);

    private static IReadOnlyList<WordBlock>? TocBlocks(WordLine[] lines, double width, double bodySize)
    {
        var heading = lines.FirstOrDefault(line => ContentsHeadings.Contains(
            line.Text.TrimEnd(':', '.'), StringComparer.OrdinalIgnoreCase));
        var entries = new List<(WordParagraph Block, double X)>();
        var ordinary = new List<WordParagraph>();
        var leaderRows = 0;
        foreach (var line in lines)
        {
            if (ReferenceEquals(line, heading))
                continue;
            if (line.Text.Contains("...", StringComparison.Ordinal) || line.Text.Contains("…", StringComparison.Ordinal))
                leaderRows++;
            var start = 0;
            var before = entries.Count;
            string prefix = "";
            for (var i = 1; i < line.Spans.Length; i++)
            {
                var anchor = line.Spans[i];
                var number = TocAnchor.Match(Clean(anchor.Text));
                if (!number.Success || i <= start)
                    continue;
                var titleSpans = line.Spans[start..i];
                var title = Line(line.Baseline, titleSpans);
                var gap = anchor.Left - title.Right;
                if (!Leader.IsMatch(title.Text) && !anchor.Text.StartsWith('.') &&
                    (gap <= Math.Max(18, line.Style.Size * 1.6) || anchor.Left - title.Left < width * .18))
                    continue;
                var text = Clean(prefix + " " + Leader.Replace(title.Text, ""));
                if (text.Length is 0 or > 320)
                    continue;
                entries.Add((new WordParagraph(line.Top, text, title.Style, TocPage: number.Groups[1].Value), title.Left));
                prefix = number.Groups[2].Value;
                start = i + 1;
            }
            if (entries.Count == before && line.Text.Length <= 1500)
            {
                var end = 0;
                foreach (Match match in TocInline.Matches(line.Text))
                {
                    var x = entries.Count == before ? line.Left : Math.Max(width * .52, line.Left);
                    entries.Add((new WordParagraph(line.Top, Clean(match.Groups[1].Value), line.Style,
                        TocPage: match.Groups[2].Value), x));
                    end = match.Index + match.Length;
                }
                prefix = line.Text[end..];
                start = line.Spans.Length;
            }
            var remainder = Clean(prefix + " " + (start < line.Spans.Length
                ? Line(line.Baseline, line.Spans[start..]).Text : ""));
            if (remainder.Length != 0)
                ordinary.Add(new(line.Top, remainder, line.Style, IsHeading(line, bodySize)));
            else if (entries.Count == before)
                ordinary.Add(new(line.Top, line.Text, line.Style, IsHeading(line, bodySize)));
        }
        if (entries.Count < (heading is null ? 5 : 2) || heading is null && leaderRows < 4)
            return null;
        var ordered = entries.OrderBy(item => item.Block.Top).ThenBy(item => item.X).ToArray();
        var left = ordered.Where(item => item.X < width * .48).ToArray();
        var right = ordered.Where(item => item.X >= width * .48).ToArray();
        if (left.Length >= 3 && right.Length >= 3)
            ordered = [.. left, .. right];
        var blocks = new List<WordBlock>();
        if (heading is not null)
            blocks.Add(new WordParagraph(heading.Top, heading.Text, heading.Style with { Bold = true }, Heading: true));
        blocks.AddRange(ordered.Select(item => item.Block));
        blocks.AddRange(ordinary);
        return blocks;
    }

    internal static string Clean(string text)
    {
        var output = new StringBuilder(text.Length);
        var needsSpace = false;
        Span<char> pair = stackalloc char[2];
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune))
            {
                needsSpace = output.Length != 0;
                continue;
            }
            if (rune.IsBmp && !XmlConvert.IsXmlChar((char)rune.Value))
                continue;
            if (needsSpace)
                output.Append(' ');
            output.Append(pair[..rune.EncodeToUtf16(pair)]);
            needsSpace = false;
        }
        return output.ToString();
    }

    private static double Median(IEnumerable<double> values, double fallback = 0)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
            return fallback;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2 : ordered[middle];
    }
}
