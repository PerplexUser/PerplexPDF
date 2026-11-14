using System.Runtime.InteropServices;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using SkiaSharp;

namespace PerplexPDF.Infrastructure.Pdf;

public sealed class SkiaOverlayEngine(string fontDirectory) : IPdfOverlayEngine
{
    private const long MaxImagePixels = 20_000_000;

    public Task WriteOverlayAsync(DocumentProject project, IReadOnlyList<PageGeometry> sourcePages, string output,
        CancellationToken ct) => Task.Run(() =>
    {
        using var pdf = SKDocument.CreatePdf(output) ?? throw new AppFailure("InvalidExport");
        var byPage = project.Overlays.ToLookup(overlay => overlay.PageId);
        foreach (var page in project.Pages)
        {
            ct.ThrowIfCancellationRequested();
            var transform = new PageTransform(sourcePages[page.SourcePage], page.Rotation);
            var canvas = pdf.BeginPage((float)transform.Display.Width, (float)transform.Display.Height);
            Draw(canvas, transform, byPage[page.Id], project, ct);
            pdf.EndPage();
        }
        pdf.Close();
    }, ct);

    public Task<byte[]> ComposePngAsync(RasterPage raster, PageTransform transform, DocumentProject project,
        Guid pageId, CancellationToken ct) => Task.Run(() =>
    {
        using var bitmap = new SKBitmap(new SKImageInfo(raster.Width, raster.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        if (bitmap.GetPixels() == 0)
            throw new AppFailure("RenderFailed");
        Marshal.Copy(raster.Bgra, 0, bitmap.GetPixels(), raster.Bgra.Length);
        using var canvas = new SKCanvas(bitmap);
        canvas.Scale((float)(raster.Width / transform.Display.Width), (float)(raster.Height / transform.Display.Height));
        Draw(canvas, transform, project.Overlays.Where(overlay => overlay.PageId == pageId), project, ct);
        canvas.Flush();
        ct.ThrowIfCancellationRequested();
        return Encode(bitmap);
    }, ct);

    public async Task<byte[]> NormalizeImageAsync(string path, CancellationToken ct)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg"))
            throw new AppFailure("InvalidImage");
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        SafeFiles.CheckLength(input.Length, DocumentProject.MaxImageBytes);
        var bytes = new byte[(int)input.Length];
        await input.ReadExactlyAsync(bytes, ct);
        return await Task.Run(() =>
        {
            using var bitmap = Decode(bytes, requirePng: extension == ".png", requireJpeg: extension != ".png", out var origin);
            using var normalized = Orient(bitmap, origin);
            var png = Encode(normalized ?? bitmap);
            SafeFiles.CheckLength(png.Length, DocumentProject.MaxImageBytes);
            return png;
        }, ct);
    }

    public void ValidateImage(ReadOnlySpan<byte> png)
    {
        using var bitmap = Decode(png.ToArray(), requirePng: true, requireJpeg: false, out _);
    }

    public Task CreateFromImagesAsync(IReadOnlyList<ImagePage> images, string output, CancellationToken ct) => Task.Run(() =>
    {
        if (images.Count is < 1 or > DocumentProject.MaxPages)
            throw new AppFailure("SizeLimit");
        using var pdf = SKDocument.CreatePdf(output) ?? throw new AppFailure("InvalidExport");
        foreach (var page in images)
        {
            ct.ThrowIfCancellationRequested();
            using var bitmap = Decode(page.Image, requirePng: true, requireJpeg: false, out _);
            var orientation = page.Rotation switch
            {
                90 => SKEncodedOrigin.RightTop,
                180 => SKEncodedOrigin.BottomRight,
                270 => SKEncodedOrigin.LeftBottom,
                _ => SKEncodedOrigin.TopLeft
            };
            using var rotated = Orient(bitmap, orientation);
            var image = rotated ?? bitmap;
            var width = page.Landscape ? 842f : 595f;
            var height = page.Landscape ? 595f : 842f;
            var canvas = pdf.BeginPage(width, height);
            canvas.Clear(SKColors.White);
            DrawImage(canvas, image, new SKRect(24, 24, width - 24, height - 24));
            pdf.EndPage();
        }
        pdf.Close();
    }, ct);

    private void Draw(SKCanvas canvas, PageTransform transform, IEnumerable<OverlayObject> overlays,
        DocumentProject project, CancellationToken ct)
    {
        foreach (var overlay in overlays)
        {
            ct.ThrowIfCancellationRequested();
            var creation = new PageTransform(transform.Source, overlay.CreationRotation);
            var bounds = creation.ToDisplay(overlay.Bounds);
            var rotation = (transform.Rotation - overlay.CreationRotation + 360) % 360;
            canvas.Save();
            switch (rotation)
            {
                case 90:
                    canvas.Translate((float)creation.Display.Height, 0);
                    break;
                case 180:
                    canvas.Translate((float)creation.Display.Width, (float)creation.Display.Height);
                    break;
                case 270:
                    canvas.Translate(0, (float)creation.Display.Width);
                    break;
            }
            canvas.RotateDegrees(rotation);
            try
            {
                var rect = SKRect.Create((float)bounds.X, (float)bounds.Y, (float)bounds.Width, (float)bounds.Height);
                using var paint = new SKPaint { Color = new SKColor(overlay.Color), IsAntialias = true };
                switch (overlay.Kind)
                {
                    case OverlayKind.Highlight:
                        canvas.DrawRect(rect, paint);
                        break;
                    case OverlayKind.Underline:
                        canvas.DrawRect(SKRect.Create(rect.Left, rect.Bottom - Math.Min(rect.Height, 2), rect.Width,
                            Math.Min(rect.Height, 2)), paint);
                        break;
                    case OverlayKind.Rectangle:
                    case OverlayKind.Ellipse:
                        paint.Style = SKPaintStyle.Stroke;
                        paint.StrokeWidth = 1.8f;
                        rect.Inflate(-Math.Min(.9f, rect.Width / 4), -Math.Min(.9f, rect.Height / 4));
                        if (overlay.Kind == OverlayKind.Rectangle)
                            canvas.DrawRect(rect, paint);
                        else
                            canvas.DrawOval(rect, paint);
                        break;
                    case OverlayKind.Image:
                        using (var image = Decode(project.Resources[overlay.ResourceHash!].ToArray(), true, false, out _))
                            DrawImage(canvas, image, rect);
                        break;
                    case OverlayKind.Text:
                    case OverlayKind.Note:
                        if (overlay.Kind == OverlayKind.Note)
                        {
                            using var fill = new SKPaint { Color = new SKColor(0xfffff3c4), IsAntialias = true };
                            canvas.DrawRect(rect, fill);
                            using var stripe = new SKPaint { Color = new SKColor(0xffdba919) };
                            canvas.DrawRect(SKRect.Create(rect.Left, rect.Top, Math.Min(3, rect.Width), rect.Height), stripe);
                            rect.Inflate(-Math.Min(5, rect.Width / 4), -Math.Min(5, rect.Height / 4));
                        }
                        DrawText(canvas, overlay, rect, paint);
                        break;
                }
            }
            finally { canvas.Restore(); }
        }
    }

    private void DrawText(SKCanvas canvas, OverlayObject overlay, SKRect rect, SKPaint paint)
    {
        var family = overlay.Font switch
        {
            OverlayFont.Serif => "DejaVuSerif",
            OverlayFont.Mono => "DejaVuSansMono",
            _ => "DejaVuSans"
        };
        var slant = overlay.Font == OverlayFont.Serif ? "Italic" : "Oblique";
        var cut = (overlay.Bold, overlay.Italic) switch
        {
            (true, true) => "-Bold" + slant,
            (true, false) => "-Bold",
            (false, true) => "-" + slant,
            _ => ""
        };
        var file = family + cut + ".ttf";
        using var typeface = LoadTypeface(overlay, file);
        using var font = new SKFont(typeface, (float)overlay.FontSize);
        var metrics = font.Metrics;
        var lineHeight = Math.Max(font.Size, metrics.Descent - metrics.Ascent + metrics.Leading);
        var baseline = rect.Top - metrics.Ascent;
        canvas.Save();
        try
        {
            canvas.ClipRect(rect);
            foreach (var paragraph in overlay.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                var line = "";
                foreach (var word in paragraph.Split(' '))
                {
                    var candidate = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && font.MeasureText(candidate) > rect.Width)
                    {
                        DrawLine(line);
                        line = word;
                    }
                    else
                        line = candidate;
                    if (baseline + metrics.Ascent > rect.Bottom)
                        return;
                }
                DrawLine(line);
                if (baseline + metrics.Ascent > rect.Bottom)
                    return;
            }
        }
        finally { canvas.Restore(); }

        void DrawLine(string line)
        {
            canvas.DrawText(line, rect.Left, baseline, SKTextAlign.Left, font, paint);
            if (overlay.Underlined && line.Length != 0)
            {
                var position = metrics.UnderlinePosition ?? font.Size * .1f;
                var thickness = Math.Max(.5f, metrics.UnderlineThickness ?? font.Size / 14f);
                canvas.DrawRect(SKRect.Create(rect.Left, baseline + position, font.MeasureText(line), thickness), paint);
            }
            baseline += lineHeight;
        }
    }

    private SKTypeface LoadTypeface(OverlayObject overlay, string bundled)
    {
        if (overlay.SystemFont is { } family)
        {
            using var style = new SKFontStyle(overlay.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal, overlay.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
            if (SKFontManager.Default.MatchFamily(family, style) is { } installed)
            {
                if (string.Equals(installed.FamilyName, family, StringComparison.OrdinalIgnoreCase))
                    return installed;
                installed.Dispose();
            }
        }
        return SKTypeface.FromFile(Path.Combine(fontDirectory, bundled)) ?? throw new AppFailure("DependencyMissing");
    }

    private static void DrawImage(SKCanvas canvas, SKBitmap bitmap, SKRect area)
    {
        var scale = Math.Min(area.Width / bitmap.Width, area.Height / bitmap.Height);
        var width = bitmap.Width * scale;
        var height = bitmap.Height * scale;
        using var image = SKImage.FromBitmap(bitmap);
        var target = SKRect.Create(area.MidX - width / 2, area.MidY - height / 2, width, height);
        canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
    }

    private static SKBitmap Decode(byte[] bytes, bool requirePng, bool requireJpeg, out SKEncodedOrigin origin)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new AppFailure("InvalidImage");
        if (requirePng && codec.EncodedFormat != SKEncodedImageFormat.Png ||
            requireJpeg && codec.EncodedFormat != SKEncodedImageFormat.Jpeg ||
            codec.Info.Width < 1 || codec.Info.Height < 1 || (long)codec.Info.Width * codec.Info.Height > MaxImagePixels)
            throw new AppFailure("InvalidImage");
        origin = codec.EncodedOrigin;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        if (bitmap.GetPixels() == 0 || codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success)
        {
            bitmap.Dispose();
            throw new AppFailure("InvalidImage");
        }
        return bitmap;
    }

    private static SKBitmap? Orient(SKBitmap bitmap, SKEncodedOrigin orientation)
    {
        if (orientation == SKEncodedOrigin.TopLeft)
            return null;
        var width = bitmap.Width;
        var height = bitmap.Height;
        var transposed = orientation is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or
            SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var output = new SKBitmap(transposed ? height : width, transposed ? width : height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Transparent);
        switch (orientation)
        {
            case SKEncodedOrigin.TopRight:
                canvas.Translate(width, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight:
                canvas.Translate(width, height);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft:
                canvas.Translate(0, height);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop:
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(height, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom:
                canvas.Translate(height, width);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, width);
                canvas.RotateDegrees(-90);
                break;
        }
        canvas.DrawBitmap(bitmap, 0, 0);
        canvas.Flush();
        return output;
    }

    private static byte[] Encode(SKBitmap bitmap)
    {
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100) ?? throw new AppFailure("RenderFailed");
        return encoded.ToArray();
    }
}
