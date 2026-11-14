using System.Runtime.InteropServices;
using System.Text;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using static PerplexPDF.Infrastructure.Pdf.PdfiumNative;

namespace PerplexPDF.Infrastructure.Pdf;

public sealed partial class PdfiumRenderer
{
    private sealed partial class PdfiumDocument
    {
        private const int MaxLayoutText = 250_000;
        private const int MaxPageObjects = 20_000;

        public Task<PdfPageLayout> ReadPageLayoutAsync(PageReference page, bool includeImages, CancellationToken ct, bool readHeadings = false) =>
            WithPageAsync(page, handle =>
            {
                var rotation = FPDFPage_GetRotation(handle);
                if (rotation != 0)
                    FPDFPage_SetRotation(handle, 0);
                try { return ReadLayout(handle, includeImages, readHeadings, ct); }
                finally
                {
                    if (rotation != 0)
                        FPDFPage_SetRotation(handle, rotation);
                }
            }, ct);

        private static PdfPageLayout ReadLayout(nint page, bool includeImages, bool readHeadings, CancellationToken ct)
        {
            if (FPDF_GetPageBoundingBox(page, out var crop) == 0)
                throw new AppFailure("InvalidPdf");
            var geometry = new PageGeometry(crop.Right - crop.Left, crop.Top - crop.Bottom);
            if (geometry.Width is < 1 or > 20_000 || geometry.Height is < 1 or > 20_000 ||
                !double.IsFinite(geometry.Width) || !double.IsFinite(geometry.Height))
                throw new AppFailure("InvalidPdf");
            var spans = new List<PdfTextSpan>();
            var (tagged, headings) = readHeadings ? ReadHeadings(page, ct) : (false, new Dictionary<int, PdfHeading>());
            var graphics = new List<(nint Object, PdfRect Bounds)>();
            var textObjects = new List<nint>();
            var backgrounds = new List<(nint Object, PdfRect Bounds, uint Color)>();
            var shaded = new HashSet<nint>();
            var fonts = new Dictionary<nint, (bool Bold, bool Italic)>();
            var visited = 0;
            var length = 0;
            var plainOrder = false;
            var textPage = FPDFText_LoadPage(page);
            var fallback = "";
            try
            {
                for (var i = 0; i < FPDFPage_CountObjects(page); i++)
                    Inspect(FPDFPage_GetObject(page, i), Matrix.Identity, 0);
                // PDFium's text layer covers OCR and complex writing directions.
                if (textPage != 0 && (spans.Count == 0 || plainOrder && !spans.Any(span => span.Heading.Level != 0)))
                {
                    fallback = ReadFullText(textPage, crop);
                    spans.Clear();
                    shaded.Clear();
                }
            }
            finally { if (textPage != 0) FPDFText_ClosePage(textPage); }
            var textLength = Math.Max(length, fallback.Length);
            // A scan or a page made entirely of paths can use the existing image fallback.
            var boxes = textLength == 0 ? [] : GroupGraphics(graphics.Where(item => !shaded.Contains(item.Object))
                .Select(item => item.Bounds), geometry, readHeadings, ct);
            var images = boxes.Count == 0 ? [] : CropImages(page, geometry, boxes, textObjects, ct);
            return new PdfPageLayout(geometry, spans, images, fallback, tagged);

            void Inspect(nint item, Matrix parent, int depth)
            {
                ct.ThrowIfCancellationRequested();
                if (++visited > MaxPageObjects || depth > 16)
                    throw new AppFailure("SizeLimit");
                var kind = FPDFPageObj_GetType(item);
                if (kind == FormObject && FPDFPageObj_GetMatrix(item, out var form) != 0)
                {
                    var frame = parent.Then(form);
                    for (var i = 0; i < FPDFFormObj_CountObjects(item); i++)
                        Inspect(FPDFFormObj_GetObject(item, (uint)i), frame, depth + 1);
                    return;
                }
                if (kind != PathObject && (kind != TextObject || textPage == 0) &&
                    (!includeImages || kind is not (ImageObject or ShadingObject)))
                    return;
                if (FPDFPageObj_GetBounds(item, out var left, out var bottom, out var right, out var top) == 0)
                    return;
                var box = ClipBounds(parent, crop, left, bottom, right, top);
                if (!(box.Width > 0 && box.Height > 0) || !double.IsFinite(box.X) || !double.IsFinite(box.Y))
                    return;
                if (kind == PathObject)
                {
                    if (FPDFPath_GetDrawMode(item, out var fillMode, out var stroke) == 0)
                        return;
                    var color = FPDFPageObj_GetFillColor(item, out var red, out var green, out var blue, out var alpha) != 0 &&
                        fillMode != 0 && alpha > 0 ? (uint?)Rgb(red, green, blue) : null;
                    if (color is { } solid && alpha == 255 && box.Width >= 12 && box.Height >= 6 && IsRectangle(item, parent))
                        backgrounds.Add((item, box, solid));
                    if (includeImages && (stroke != 0 || color is { } painted && painted != 0xFFFFFF) && IsGraphic(box))
                        graphics.Add((item, box));
                    return;
                }
                if (kind is ImageObject or ShadingObject)
                {
                    if (IsGraphic(box))
                        graphics.Add((item, box));
                    return;
                }
                if (ReadObjectString(item, textPage, MaxLayoutText) is not { } text)
                {
                    plainOrder = true;
                    return;
                }
                if (string.IsNullOrWhiteSpace(text))
                    return;
                if (includeImages && FPDFTextObj_GetTextRenderMode(item) is >= 0 and <= 2)
                    textObjects.Add(item);
                length += text.Length;
                if (length > MaxLayoutText)
                    throw new AppFailure("SizeLimit");

                double size = FPDFTextObj_GetFontSize(item, out var fontSize) == 0 ? 11 : fontSize;
                var baseline = box.Y + Math.Min(size, box.Height);
                if (FPDFPageObj_GetMatrix(item, out var matrix) != 0)
                {
                    var placed = parent.Then(matrix);
                    var scale = Math.Sqrt(placed.C * placed.C + placed.D * placed.D);
                    size *= scale;
                    baseline = crop.Top - placed.F;
                    plainOrder |= Math.Abs(placed.B) > Math.Abs(placed.A) * .2;
                }
                if (!double.IsFinite(size) || !double.IsFinite(baseline))
                    return;
                plainOrder |= text.Any(ch => ch is >= '\u0590' and <= '\u08FF');
                var font = FPDFTextObj_GetFont(item);
                if (!fonts.TryGetValue(font, out var style))
                    fonts.Add(font, style = FontStyle(font));
                var background = FindBackground(box, backgrounds);
                if (background is { } paintedBackground)
                    shaded.Add(paintedBackground.Object);
                var heading = headings.Count == 0 ? default : headings.GetValueOrDefault(FPDFPageObj_GetMarkedContentID(item));
                spans.Add(new(text, box.X, box.X + box.Width, baseline, Math.Clamp(size, 4, 96), style.Bold, style.Italic,
                    TextColor(item), background?.Color, heading));
            }
        }

        private static bool IsRectangle(nint path, Matrix parent)
        {
            if (FPDFPageObj_GetMatrix(path, out var matrix) == 0)
                return false;
            var count = FPDFPath_CountSegments(path);
            if (count is not (4 or 5))
                return false;
            var transform = parent.Then(matrix);
            Span<PdfPoint> points = stackalloc PdfPoint[5];
            const int moveTo = 2;
            const int lineTo = 0;
            for (var index = 0; index < count; index++)
            {
                var segment = FPDFPath_GetPathSegment(path, index);
                if (segment == 0 || FPDFPathSegment_GetType(segment) != (index == 0 ? moveTo : lineTo) ||
                    FPDFPathSegment_GetPoint(segment, out var x, out var y) == 0)
                    return false;
                points[index] = transform.At(x, y);
            }
            const double tolerance = .1;
            if (count == 5 && (Math.Abs(points[0].X - points[4].X) > tolerance || Math.Abs(points[0].Y - points[4].Y) > tolerance))
                return false;
            var horizontal = Math.Abs(points[0].Y - points[1].Y) < tolerance;
            for (var index = 0; index < 4; index++)
            {
                var next = points[(index + 1) % 4];
                var fixedAxis = horizontal ? next.Y - points[index].Y : next.X - points[index].X;
                var movingAxis = horizontal ? next.X - points[index].X : next.Y - points[index].Y;
                if (!(Math.Abs(fixedAxis) < tolerance && Math.Abs(movingAxis) > tolerance))
                    return false;
                horizontal = !horizontal;
            }
            return true;
        }

        private static uint? TextColor(nint text)
        {
            var found = FPDFTextObj_GetTextRenderMode(text) == 1
                ? FPDFPageObj_GetStrokeColor(text, out var red, out var green, out var blue, out var alpha)
                : FPDFPageObj_GetFillColor(text, out red, out green, out blue, out alpha);
            return found != 0 && alpha == 255 ? Rgb(red, green, blue) : null;
        }

        private static uint Rgb(uint red, uint green, uint blue) => (red << 16) | (green << 8) | blue;

        private static (nint Object, uint Color)? FindBackground(PdfRect text, List<(nint Object, PdfRect Bounds, uint Color)> backgrounds)
        {
            var earliest = Math.Max(0, backgrounds.Count - 512);
            for (var i = backgrounds.Count - 1; i >= earliest; i--)
            {
                var (item, box, color) = backgrounds[i];
                if (box.X <= text.X + 1 && box.Y <= text.Y + 1 && box.X + box.Width >= text.X + text.Width - 1 &&
                    box.Y + box.Height >= text.Y + text.Height - 1)
                    return color == 0xFFFFFF ? null : (item, color);
            }
            return null;
        }

        private static string ReadFullText(nint textPage, FloatRect crop)
        {
            var length = FPDFText_GetBoundedText(textPage, crop.Left, crop.Top, crop.Right, crop.Bottom, 0, 0);
            if (length > MaxLayoutText)
                throw new AppFailure("SizeLimit");
            if (length <= 0)
                return "";
            var buffer = Marshal.AllocHGlobal((length + 1) * 2);
            try
            {
                var written = FPDFText_GetBoundedText(textPage, crop.Left, crop.Top, crop.Right, crop.Bottom, buffer, length + 1);
                return written <= 0 ? "" : Marshal.PtrToStringUni(buffer, Math.Min(written, length + 1))!.TrimEnd('\0');
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        private static (bool Bold, bool Italic) FontStyle(nint font)
        {
            if (font == 0)
                return default;
            var buffer = new byte[256];
            var length = FPDFFont_GetBaseFontName(font, buffer, (uint)buffer.Length);
            var name = length is > 1 and <= 256 ? Encoding.UTF8.GetString(buffer, 0, (int)length - 1) : "";
            var flags = FPDFFont_GetFlags(font);
            return (FPDFFont_GetWeight(font) >= 600 || name.Contains("Bold", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Black", StringComparison.OrdinalIgnoreCase),
                flags >= 0 && (flags & 64) != 0 || name.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Oblique", StringComparison.OrdinalIgnoreCase));
        }

        private static PdfRect ClipBounds(Matrix transform, FloatRect crop, double left, double bottom, double right, double top)
        {
            var corners = new[] { transform.At(left, bottom), transform.At(right, bottom),
                transform.At(left, top), transform.At(right, top) };
            var l = Math.Max(crop.Left, corners.Min(p => p.X));
            var r = Math.Min(crop.Right, corners.Max(p => p.X));
            var t = Math.Min(crop.Top, corners.Max(p => p.Y));
            var b = Math.Max(crop.Bottom, corners.Min(p => p.Y));
            return new(l - crop.Left, crop.Top - t, r - l, t - b);
        }

        private static bool IsGraphic(PdfRect box) => box.Width >= 1 && box.Height >= 1 && box.Width * box.Height >= 36;

        private static List<PdfRect> GroupGraphics(IEnumerable<PdfRect> candidates, PageGeometry geometry, bool includeBackdrops, CancellationToken ct)
        {
            var boxes = new List<PdfRect>();
            // Large backdrop images may be scans with the recognized text already painted into the image.
            var maxArea = (includeBackdrops ? 1.0001 : .82) * geometry.Width * geometry.Height;
            var graphics = candidates.Where(box => box.Width * box.Height <= maxArea).ToArray();
            foreach (var candidate in graphics)
            {
                ct.ThrowIfCancellationRequested();
                var box = candidate;
                for (var i = 0; i < boxes.Count;)
                {
                    var other = boxes[i];
                    var combined = Union(box, other);
                    var touches = Math.Max(box.X, other.X) <= Math.Min(box.X + box.Width, other.X + other.Width) + 1 &&
                        Math.Max(box.Y, other.Y) <= Math.Min(box.Y + box.Height, other.Y + other.Height) + 1;
                    if (touches && combined.Width * combined.Height <= maxArea)
                    {
                        box = combined;
                        boxes.RemoveAt(i);
                        i = 0;
                    }
                    else
                        i++;
                }
                if (boxes.Count >= 100)
                {
                    // Group dense drawings into a bounded number of picture regions.
                    return graphics.GroupBy(box => ((int)(8 * (box.X + box.Width / 2) / geometry.Width),
                            (int)(8 * (box.Y + box.Height / 2) / geometry.Height)))
                        .Select(group => group.Aggregate(Union)).ToList();
                }
                boxes.Add(box);
            }
            return boxes;
        }

        private static PdfRect Union(PdfRect a, PdfRect b)
        {
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            return new(x, y, Math.Max(a.X + a.Width, b.X + b.Width) - x,
                Math.Max(a.Y + a.Height, b.Y + b.Height) - y);
        }

        private static List<PdfImageSpan> CropImages(nint page, PageGeometry geometry, List<PdfRect> boxes,
            List<nint> textObjects, CancellationToken ct)
        {
            var scale = Math.Min(1.55, 2_200 / Math.Max(geometry.Width, geometry.Height));
            var width = Math.Max(1, (int)Math.Round(geometry.Width * scale));
            var height = Math.Max(1, (int)Math.Round(geometry.Height * scale));
            var images = new List<PdfImageSpan>();
            var raster = RenderWithoutText(page, textObjects, width, height, ct);
            long pixels = 0;
            foreach (var box in boxes)
            {
                ct.ThrowIfCancellationRequested();
                var x = Math.Clamp((int)Math.Floor(box.X * width / geometry.Width), 0, width - 1);
                var y = Math.Clamp((int)Math.Floor(box.Y * height / geometry.Height), 0, height - 1);
                var w = Math.Clamp((int)Math.Ceiling((box.X + box.Width) * width / geometry.Width) - x, 1, width - x);
                var h = Math.Clamp((int)Math.Ceiling((box.Y + box.Height) * height / geometry.Height) - y, 1, height - y);
                pixels += (long)w * h;
                if (pixels > 20_000_000)
                    throw new AppFailure("SizeLimit");
                var bytes = GC.AllocateUninitializedArray<byte>(w * h * 4);
                for (var row = 0; row < h; row++)
                    raster.Bgra.AsSpan(((y + row) * width + x) * 4, w * 4).CopyTo(bytes.AsSpan(row * w * 4));
                images.Add(new(box, new(w, h, bytes)));
            }
            return images;
        }

        private static RasterPage RenderWithoutText(nint page, List<nint> textObjects, int width, int height, CancellationToken ct)
        {
            var hidden = new List<nint>();
            try
            {
                foreach (var text in textObjects)
                {
                    ct.ThrowIfCancellationRequested();
                    if (FPDFPageObj_GetIsActive(text, out var active) == 0)
                        throw new AppFailure("RenderFailed");
                    if (active == 0)
                        continue;
                    if (FPDFPageObj_SetIsActive(text, 0) == 0)
                        throw new AppFailure("RenderFailed");
                    hidden.Add(text);
                }
                return Render(page, 0, width, height);
            }
            finally
            {
                foreach (var text in hidden)
                    FPDFPageObj_SetIsActive(text, 1);
            }
        }

        private readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
        {
            internal static Matrix Identity => new(1, 0, 0, 1, 0, 0);
            internal PdfPoint At(double x, double y) => new(A * x + C * y + E, B * x + D * y + F);
            internal Matrix Then(FloatMatrix m) => new(A * m.A + C * m.B, B * m.A + D * m.B,
                A * m.C + C * m.D, B * m.C + D * m.D, A * m.E + C * m.F + E, B * m.E + D * m.F + F);
        }
    }
}
