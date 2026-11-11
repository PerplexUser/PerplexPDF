using System.Runtime.InteropServices;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using static PerplexPDF.Desktop.Graphics;

namespace PerplexPDF.Desktop;

internal static class Graphics
{
    private const string Cairo = "libcairo.so.2";
    private const string Pixbuf = "libgdk_pixbuf-2.0.so.0";
    private const string Pango = "libpango-1.0.so.0";
    private const string PangoCairo = "libpangocairo-1.0.so.0";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int WritePng(nint data, nint buffer, uint length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SizePrepared(nint loader, int width, int height, nint data);

    internal sealed class Surface : IDisposable
    {
        internal nint Handle { get; }
        internal Surface(nint handle)
        {
            Handle = handle;
            if (handle == 0 || cairo_surface_status(handle) != 0)
            {
                Dispose();
                throw new AppFailure("RenderFailed");
            }
        }

        public void Dispose() { if (Handle != 0) cairo_surface_destroy(Handle); }
    }

    internal sealed class Bitmap(nint handle) : IDisposable
    {
        internal nint Handle { get; } = handle;
        internal int Width => gdk_pixbuf_get_width(Handle);
        internal int Height => gdk_pixbuf_get_height(Handle);
        public void Dispose() => Gtk.g_object_unref(Handle);
    }

    internal static unsafe void Raster(nint context, RasterPage raster, PdfRect bounds)
    {
        fixed (byte* pixels = raster.Bgra)
        {
            using var surface = new Surface(cairo_image_surface_create_for_data((nint)pixels, 0,
                raster.Width, raster.Height, raster.Width * 4));
            cairo_save(context);
            try
            {
                cairo_translate(context, bounds.X, bounds.Y);
                cairo_scale(context, bounds.Width / raster.Width, bounds.Height / raster.Height);
                cairo_set_source_surface(context, surface.Handle, 0, 0);
                cairo_paint(context);
            }
            finally { cairo_restore(context); }
        }
    }

    internal static void Fill(nint context, PdfRect bounds, uint argb)
    {
        Color(context, argb);
        cairo_rectangle(context, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        cairo_fill(context);
    }

    internal static void Color(nint context, uint argb) => cairo_set_source_rgba(context,
        ((argb >> 16) & 255) / 255.0, ((argb >> 8) & 255) / 255.0, (argb & 255) / 255.0, (argb >> 24) / 255.0);

    internal static void DrawBitmap(nint context, Bitmap image, PdfRect bounds)
    {
        var scale = Math.Min(bounds.Width / image.Width, bounds.Height / image.Height);
        cairo_save(context);
        try
        {
            cairo_translate(context, bounds.X + (bounds.Width - image.Width * scale) / 2,
                bounds.Y + (bounds.Height - image.Height * scale) / 2);
            cairo_scale(context, scale, scale);
            gdk_cairo_set_source_pixbuf(context, image.Handle, 0, 0);
            cairo_rectangle(context, 0, 0, image.Width, image.Height);
            cairo_fill(context);
        }
        finally { cairo_restore(context); }
    }

    internal static unsafe Bitmap Decode(ReadOnlySpan<byte> bytes, bool png)
    {
        if (bytes.Length == 0 || bytes.Length > DocumentProject.MaxImageBytes)
            throw new AppFailure("InvalidImage");
        var loader = gdk_pixbuf_loader_new_with_type(png ? "png" : "jpeg", out var error);
        if (error != 0) Gtk.g_error_free(error);
        if (loader == 0)
            throw new AppFailure("InvalidImage");
        var rejected = false;
        Gtk.Bind(loader, "size-prepared", new SizePrepared((instance, width, height, _) =>
        {
            if (width <= 0 || height <= 0 || (long)width * height > 20_000_000 || Math.Max(width, height) > 16_384)
            {
                rejected = true;
                gdk_pixbuf_loader_set_size(instance, 1, 1);
            }
        }));
        try
        {
            int written;
            fixed (byte* pointer = bytes)
                written = gdk_pixbuf_loader_write(loader, (nint)pointer, (nuint)bytes.Length, out error);
            if (error != 0) Gtk.g_error_free(error);
            var closed = gdk_pixbuf_loader_close(loader, out error);
            if (error != 0) Gtk.g_error_free(error);
            var pixbuf = gdk_pixbuf_loader_get_pixbuf(loader);
            if (written == 0 || closed == 0 || rejected || pixbuf == 0)
                throw new AppFailure("InvalidImage");
            return new Bitmap(g_object_ref(pixbuf));
        }
        finally { Gtk.g_object_unref(loader); }
    }

    internal static Bitmap Orient(Bitmap image, int rotation, bool embedded)
    {
        var rotated = embedded ? gdk_pixbuf_apply_embedded_orientation(image.Handle) :
            gdk_pixbuf_rotate_simple(image.Handle, (360 - rotation) % 360);
        if (rotated == 0)
            throw new AppFailure("InvalidImage");
        return new Bitmap(rotated);
    }

    internal static byte[] Png(Bitmap bitmap)
    {
        var ok = gdk_pixbuf_save_to_buffer(bitmap.Handle, out var buffer, out var length, "png", out var error, 0);
        if (error != 0) Gtk.g_error_free(error);
        try
        {
            if (ok == 0 || buffer == 0 || length > DocumentProject.MaxImageBytes)
                throw new AppFailure("InvalidImage");
            var bytes = new byte[(int)length];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { Gtk.g_free(buffer); }
    }

    internal static byte[] Png(nint surface)
    {
        using var bytes = new MemoryStream();
        var write = new WritePng((_, buffer, length) =>
        {
            try
            {
                unsafe { bytes.Write(new ReadOnlySpan<byte>((void*)buffer, checked((int)length))); }
                return 0;
            }
            catch (Exception) { return 11; }
        });
        if (cairo_surface_write_to_png_stream(surface, write, 0) != 0)
            throw new AppFailure("RenderFailed");
        return bytes.ToArray();
    }

    internal static void DrawText(nint context, OverlayObject overlay, PdfRect bounds)
    {
        var description = pango_font_description_new();
        var layout = pango_cairo_create_layout(context);
        try
        {
            pango_font_description_set_family(description, overlay.SystemFont ?? FontFamily(overlay.Font));
            pango_font_description_set_absolute_size(description, overlay.FontSize * 1024);
            pango_font_description_set_weight(description, overlay.Bold ? 700 : 400);
            pango_font_description_set_style(description, overlay.Italic ? 2 : 0);
            pango_layout_set_font_description(layout, description);
            pango_layout_set_text(layout, overlay.Text, -1);
            pango_layout_set_width(layout, (int)(bounds.Width * 1024));
            pango_layout_set_wrap(layout, 2);
            if (overlay.Underlined)
            {
                var attributes = pango_attr_list_new();
                pango_attr_list_insert(attributes, pango_attr_underline_new(1));
                pango_layout_set_attributes(layout, attributes);
                pango_attr_list_unref(attributes);
            }
            cairo_rectangle(context, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            cairo_clip(context);
            cairo_move_to(context, bounds.X, bounds.Y);
            Color(context, overlay.Color);
            pango_cairo_show_layout(context, layout);
        }
        finally
        {
            pango_font_description_free(description);
            Gtk.g_object_unref(layout);
        }
    }

    internal static string FontFamily(OverlayFont font) => font switch
    {
        OverlayFont.Serif => "DejaVu Serif",
        OverlayFont.Mono => "DejaVu Sans Mono",
        _ => "DejaVu Sans"
    };

    internal static (string Family, double Size) ReadFont(string name)
    {
        var desc = pango_font_description_from_string(name);
        try { return (Gtk.String(pango_font_description_get_family(desc)), pango_font_description_get_size(desc) / 1024.0); }
        finally { pango_font_description_free(desc); }
    }

    [DllImport(Cairo)] internal static extern nint cairo_create(nint surface);
    [DllImport(Cairo)] internal static extern void cairo_destroy(nint context);
    [DllImport(Cairo)] internal static extern int cairo_status(nint context);
    [DllImport(Cairo)] internal static extern nint cairo_image_surface_create(int format, int width, int height);
    [DllImport(Cairo)] internal static extern nint cairo_image_surface_create_for_data(nint pixels, int format, int width, int height, int stride);
    [DllImport(Cairo)] internal static extern nint cairo_pdf_surface_create(string path, double width, double height);
    [DllImport(Cairo)] internal static extern void cairo_pdf_surface_set_size(nint surface, double width, double height);
    [DllImport(Cairo)] internal static extern int cairo_surface_status(nint surface);
    [DllImport(Cairo)] internal static extern void cairo_surface_finish(nint surface);
    [DllImport(Cairo)] private static extern void cairo_surface_destroy(nint surface);
    [DllImport(Cairo)] private static extern int cairo_surface_write_to_png_stream(nint surface, WritePng write, nint data);
    [DllImport(Cairo)] internal static extern void cairo_save(nint context);
    [DllImport(Cairo)] internal static extern void cairo_restore(nint context);
    [DllImport(Cairo)] internal static extern void cairo_translate(nint context, double x, double y);
    [DllImport(Cairo)] internal static extern void cairo_scale(nint context, double x, double y);
    [DllImport(Cairo)] internal static extern void cairo_rotate(nint context, double radians);
    [DllImport(Cairo)] internal static extern void cairo_rectangle(nint context, double x, double y, double width, double height);
    [DllImport(Cairo)] internal static extern void cairo_move_to(nint context, double x, double y);
    [DllImport(Cairo)] internal static extern void cairo_arc(nint context, double x, double y, double radius, double a1, double a2);
    [DllImport(Cairo)] internal static extern void cairo_set_source_rgba(nint context, double r, double g, double b, double a);
    [DllImport(Cairo)] private static extern void cairo_set_source_surface(nint context, nint surface, double x, double y);
    [DllImport(Cairo)] internal static extern void cairo_set_line_width(nint context, double width);
    [DllImport(Cairo)] internal static extern void cairo_clip(nint context);
    [DllImport(Cairo)] internal static extern void cairo_clip_extents(nint context, out double x1, out double y1, out double x2, out double y2);
    [DllImport(Cairo)] internal static extern void cairo_paint(nint context);
    [DllImport(Cairo)] internal static extern void cairo_fill(nint context);
    [DllImport(Cairo)] internal static extern void cairo_stroke(nint context);
    [DllImport(Cairo)] internal static extern void cairo_show_page(nint context);

    [DllImport("libgdk-3.so.0")] private static extern void gdk_cairo_set_source_pixbuf(nint context, nint pixbuf, double x, double y);
    [DllImport("libgobject-2.0.so.0")] private static extern nint g_object_ref(nint instance);
    [DllImport(Pixbuf)] private static extern nint gdk_pixbuf_loader_new_with_type(string type, out nint error);
    [DllImport(Pixbuf)] private static extern int gdk_pixbuf_loader_write(nint loader, nint bytes, nuint length, out nint error);
    [DllImport(Pixbuf)] private static extern int gdk_pixbuf_loader_close(nint loader, out nint error);
    [DllImport(Pixbuf)] private static extern nint gdk_pixbuf_loader_get_pixbuf(nint loader);
    [DllImport(Pixbuf)] private static extern void gdk_pixbuf_loader_set_size(nint loader, int width, int height);
    [DllImport(Pixbuf)] private static extern int gdk_pixbuf_get_width(nint pixbuf);
    [DllImport(Pixbuf)] private static extern int gdk_pixbuf_get_height(nint pixbuf);
    [DllImport(Pixbuf)] private static extern nint gdk_pixbuf_apply_embedded_orientation(nint pixbuf);
    [DllImport(Pixbuf)] private static extern nint gdk_pixbuf_rotate_simple(nint pixbuf, int rotation);
    [DllImport(Pixbuf)] private static extern int gdk_pixbuf_save_to_buffer(nint pixbuf, out nint buffer,
        out nuint length, string type, out nint error, nint terminator);
    [DllImport(Pixbuf)] internal static extern nint gdk_pixbuf_new_from_file_at_scale(string file, int width, int height, int preserveAspect, out nint error);

    [DllImport(PangoCairo)] private static extern nint pango_cairo_create_layout(nint context);
    [DllImport(PangoCairo)] private static extern void pango_cairo_show_layout(nint context, nint layout);
    [DllImport(Pango)] private static extern nint pango_font_description_new();
    [DllImport(Pango)] private static extern nint pango_font_description_from_string(string text);
    [DllImport(Pango)] private static extern nint pango_font_description_get_family(nint font);
    [DllImport(Pango)] private static extern int pango_font_description_get_size(nint font);
    [DllImport(Pango)] private static extern void pango_font_description_set_family(nint font, string family);
    [DllImport(Pango)] private static extern void pango_font_description_set_absolute_size(nint font, double size);
    [DllImport(Pango)] private static extern void pango_font_description_set_weight(nint font, int weight);
    [DllImport(Pango)] private static extern void pango_font_description_set_style(nint font, int style);
    [DllImport(Pango)] private static extern void pango_font_description_free(nint font);
    [DllImport(Pango)] private static extern void pango_layout_set_font_description(nint layout, nint font);
    [DllImport(Pango)] private static extern void pango_layout_set_text(nint layout, string text, int length);
    [DllImport(Pango)] private static extern void pango_layout_set_width(nint layout, int width);
    [DllImport(Pango)] private static extern void pango_layout_set_wrap(nint layout, int wrap);
    [DllImport(Pango)] private static extern void pango_layout_set_attributes(nint layout, nint attributes);
    [DllImport(Pango)] private static extern nint pango_attr_list_new();
    [DllImport(Pango)] private static extern void pango_attr_list_insert(nint list, nint attribute);
    [DllImport(Pango)] private static extern void pango_attr_list_unref(nint list);
    [DllImport(Pango)] private static extern nint pango_attr_underline_new(int style);
}

internal sealed class CairoOverlayEngine : IPdfOverlayEngine
{
    public Task WriteOverlayAsync(DocumentProject project, IReadOnlyList<PageGeometry> sourcePages, string output,
        CancellationToken ct) => Task.Run(() =>
    {
        var byPage = project.Overlays.ToLookup(overlay => overlay.PageId);
        WritePdf(output, project.Pages.Select(page =>
        {
            var transform = new PageTransform(sourcePages[page.SourcePage], page.Rotation);
            return (transform.Display, (Action<nint>)(context => Paint(context, transform, byPage[page.Id], project, ct)));
        }), ct);
    }, ct);

    public Task<byte[]> ComposePngAsync(RasterPage raster, PageTransform transform, DocumentProject project,
        Guid pageId, CancellationToken ct) => Task.Run(() =>
    {
        var pixels = raster.Bgra.ToArray();
        unsafe
        {
            fixed (byte* pointer = pixels)
            {
                using var surface = new Surface(cairo_image_surface_create_for_data((nint)pointer, 0, raster.Width, raster.Height, raster.Width * 4));
                var context = cairo_create(surface.Handle);
                try
                {
                    cairo_scale(context, raster.Width / transform.Display.Width, raster.Height / transform.Display.Height);
                    Paint(context, transform, project.Overlays.Where(item => item.PageId == pageId), project, ct);
                    Check(context);
                    return Png(surface.Handle);
                }
                finally { cairo_destroy(context); }
            }
        }
    }, ct);

    public async Task<byte[]> NormalizeImageAsync(string path, CancellationToken ct)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg"))
            throw new AppFailure("InvalidImage");
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65_536, true);
        SafeFiles.CheckLength(input.Length, DocumentProject.MaxImageBytes);
        var bytes = new byte[(int)input.Length];
        await input.ReadExactlyAsync(bytes, ct);
        return await Task.Run(() =>
        {
            using var image = Decode(bytes, extension == ".png");
            using var normalized = Orient(image, 0, embedded: true);
            return Png(normalized);
        }, ct);
    }

    public void ValidateImage(ReadOnlySpan<byte> png) { using var image = Decode(png, png: true); }

    public Task CreateFromImagesAsync(IReadOnlyList<ImagePage> images, string output, CancellationToken ct) => Task.Run(() =>
    {
        if (images.Count is < 1 or > DocumentProject.MaxPages)
            throw new AppFailure("SizeLimit");
        WritePdf(output, images.Select(page =>
        {
            var size = page.Landscape ? new PageGeometry(842, 595) : new PageGeometry(595, 842);
            return (size, (Action<nint>)(context =>
            {
                using var source = Decode(page.Image, png: true);
                using var image = Orient(source, page.Rotation, embedded: false);
                Fill(context, new(0, 0, size.Width, size.Height), 0xffffffff);
                DrawBitmap(context, image, new(24, 24, size.Width - 48, size.Height - 48));
            }));
        }), ct);
    }, ct);

    private static void WritePdf(string output, IEnumerable<(PageGeometry Size, Action<nint> Draw)> pages, CancellationToken ct)
    {
        using var surface = new Surface(cairo_pdf_surface_create(output, 1, 1));
        var context = cairo_create(surface.Handle);
        try
        {
            foreach (var (size, draw) in pages)
            {
                ct.ThrowIfCancellationRequested();
                cairo_pdf_surface_set_size(surface.Handle, size.Width, size.Height);
                cairo_save(context);
                try { draw(context); }
                finally { cairo_restore(context); }
                cairo_show_page(context);
                Check(context);
            }
            cairo_surface_finish(surface.Handle);
            if (cairo_surface_status(surface.Handle) != 0)
                throw new AppFailure("InvalidExport");
        }
        finally { cairo_destroy(context); }
    }

    internal static void Paint(nint context, PageTransform transform, IEnumerable<OverlayObject> overlays,
        DocumentProject project, CancellationToken ct)
    {
        foreach (var overlay in overlays)
        {
            ct.ThrowIfCancellationRequested();
            var creation = new PageTransform(transform.Source, overlay.CreationRotation);
            var bounds = creation.ToDisplay(overlay.Bounds);
            var rotation = (transform.Rotation - overlay.CreationRotation + 360) % 360;
            cairo_save(context);
            try
            {
                switch (rotation)
                {
                    case 90: cairo_translate(context, creation.Display.Height, 0); break;
                    case 180: cairo_translate(context, creation.Display.Width, creation.Display.Height); break;
                    case 270: cairo_translate(context, 0, creation.Display.Width); break;
                }
                cairo_rotate(context, rotation * Math.PI / 180);
                switch (overlay.Kind)
                {
                    case OverlayKind.Highlight: Fill(context, bounds, overlay.Color); break;
                    case OverlayKind.Underline:
                        var thickness = Math.Min(bounds.Height, 2);
                        Fill(context, bounds with { Y = bounds.Y + bounds.Height - thickness, Height = thickness }, overlay.Color);
                        break;
                    case OverlayKind.Rectangle:
                    case OverlayKind.Ellipse: StrokeShape(context, bounds, overlay); break;
                    case OverlayKind.Image:
                        using (var image = Decode(project.Resources[overlay.ResourceHash!].AsSpan(), png: true))
                            DrawBitmap(context, image, bounds);
                        break;
                    case OverlayKind.Note:
                        Fill(context, bounds, 0xfffff3c4);
                        Fill(context, bounds with { Width = Math.Min(3, bounds.Width) }, 0xffdba919);
                        DrawText(context, overlay, Inset(bounds, 5));
                        break;
                    case OverlayKind.Text: DrawText(context, overlay, bounds); break;
                }
            }
            finally { cairo_restore(context); }
        }
    }

    private static PdfRect Inset(PdfRect bounds, double amount)
    {
        var x = Math.Min(amount, bounds.Width / 4);
        var y = Math.Min(amount, bounds.Height / 4);
        return new(bounds.X + x, bounds.Y + y, bounds.Width - 2 * x, bounds.Height - 2 * y);
    }

    private static void StrokeShape(nint context, PdfRect bounds, OverlayObject overlay)
    {
        var rect = Inset(bounds, .9);
        if (overlay.Kind == OverlayKind.Rectangle)
            cairo_rectangle(context, rect.X, rect.Y, rect.Width, rect.Height);
        else
        {
            cairo_save(context);
            cairo_translate(context, rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            cairo_scale(context, rect.Width / 2, rect.Height / 2);
            cairo_arc(context, 0, 0, 1, 0, Math.Tau);
            cairo_restore(context);
        }
        Color(context, overlay.Color);
        cairo_set_line_width(context, 1.8);
        cairo_stroke(context);
    }

    private static void Check(nint context)
    {
        if (cairo_status(context) != 0)
            throw new AppFailure("RenderFailed");
    }
}
