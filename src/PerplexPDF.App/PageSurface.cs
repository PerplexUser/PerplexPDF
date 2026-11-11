using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PerplexPDF.Application;
using PerplexPDF.Domain;

namespace PerplexPDF.App;

public sealed class PageSurface : FrameworkElement
{
    private PageTile? _tile;
    private BitmapSource? _image;
    private CancellationTokenSource? _render;
    private PdfPoint? _start;
    private PdfPoint _end;
    private OverlayObject? _moving;
    private OverlayKind? _creating;

    public PageTile? Tile => _tile;

    public PageSurface()
    {
        SnapsToDevicePixels = true;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
        LostMouseCapture += (_, _) => ClearGesture();
    }

    private void Attach()
    {
        if (ReferenceEquals(_tile, DataContext))
            return;
        Detach();
        if (DataContext is not PageTile tile)
            return;
        _tile = tile;
        tile.Tab.PropertyChanged += OnTabChanged;
        Resize();
        RenderPage();
    }

    private void Detach()
    {
        if (_tile is { } tile)
            tile.Tab.PropertyChanged -= OnTabChanged;
        CancelGesture();
        _tile = null;
        _image = null;
        _render?.Cancel();
        InvalidateVisual();
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DocumentTab.Scale):
                Resize();
                RenderPage();
                break;
            case nameof(DocumentTab.Project):
                RenderPage();
                break;
            case nameof(DocumentTab.Tool):
                Cursor = _tile?.Tab.Tool is null ? Cursors.IBeam : Cursors.Cross;
                break;
            case nameof(DocumentTab.Selected):
            case nameof(DocumentTab.Matches):
            case nameof(DocumentTab.TextBounds):
                InvalidateVisual();
                break;
        }
    }

    private void Resize()
    {
        var tile = _tile!;
        Width = tile.Transform.Display.Width * tile.Tab.Scale;
        Height = tile.Transform.Display.Height * tile.Tab.Scale;
        Cursor = tile.Tab.Tool is null ? Cursors.IBeam : Cursors.Cross;
        InvalidateVisual();
    }

    private async void RenderPage()
    {
        _render?.Cancel();
        if (_tile is not { } tile || Window.GetWindow(this) is not MainWindow window)
            return;
        using var request = new CancellationTokenSource();
        _render = request;
        try
        {
            var project = tile.Tab.Project;
            var points = tile.Transform.Display;
            var dpi = Math.Min(600, tile.Tab.Scale * 72 * VisualTreeHelper.GetDpi(this).DpiScaleX);
            dpi = Math.Max(6, Math.Min(dpi, 72 * Math.Min(15_000 / Math.Max(points.Width, points.Height),
                Math.Sqrt(20_000_000 / (points.Width * points.Height)))));
            if (project.Overlays.Any(overlay => overlay.PageId == tile.Reference.Id))
            {
                var bytes = await window.Workspace.RenderPngAsync(tile.Tab.Document, project, tile.Reference, dpi, request.Token);
                request.Token.ThrowIfCancellationRequested();
                _image = Decode(bytes);
            }
            else
            {
                var raster = await tile.Tab.Document.Pdf.RenderAsync(tile.Reference, dpi, request.Token);
                request.Token.ThrowIfCancellationRequested();
                _image = Decode(raster);
            }
            InvalidateVisual();
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (request.IsCancellationRequested || tile.Tab.Closed) { }
        catch (Exception error) { if (!request.IsCancellationRequested) window.Report(error, dialog: false); }
        finally { if (ReferenceEquals(_render, request)) _render = null; }
    }

    internal static BitmapSource Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    internal static BitmapSource Decode(RasterPage raster)
    {
        var bitmap = BitmapSource.Create(raster.Width, raster.Height, 96, 96, PixelFormats.Bgra32, null, raster.Bgra, raster.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        if (_image is { } image)
            dc.DrawImage(image, new Rect(RenderSize));
        if (_tile is not { } tile)
        {
            dc.Pop();
            return;
        }

        for (var index = 0; index < tile.Tab.Matches.Count; index++)
        {
            var hit = tile.Tab.Matches[index];
            if (hit.PageId != tile.Reference.Id)
                continue;
            var brush = index == tile.Tab.MatchIndex ? ActiveMatchBrush : MatchBrush;
            foreach (var bound in hit.Bounds)
                dc.DrawRectangle(brush, null, Pixels(tile, tile.Transform.ToDisplay(bound)));
        }
        if (tile.Tab.TextPageId == tile.Reference.Id && tile.Tab.TextBounds is { } selectedText)
            dc.DrawRectangle(SelectionBrush, null, Pixels(tile, tile.Transform.ToDisplay(selectedText)));
        if (tile.Tab.Selected is { } selected && selected.PageId == tile.Reference.Id)
            dc.DrawRectangle(null, SelectionPen, Pixels(tile, tile.Transform.ToDisplay(selected.Bounds)));
        if (_start is { } start)
        {
            var rect = _moving is { } moving
                ? tile.Transform.ToDisplay(MoveBounds(tile, moving, start, _end))
                : PdfRect.Between(start, _end);
            dc.DrawRectangle(SelectionBrush, SelectionPen, Pixels(tile, rect));
        }
        dc.Pop();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_tile is not { } tile || Window.GetWindow(this) is not MainWindow window || window.IsBusy)
            return;
        window.ActivatePage(tile, scroll: false);
        _start = _end = Position(tile, e.GetPosition(this));
        _creating = tile.Tab.Tool;
        _moving = _creating is null && !tile.Tab.EditExistingText
            ? tile.Tab.Project.Overlays.LastOrDefault(overlay => overlay.PageId == tile.Reference.Id &&
                overlay.Bounds.Contains(tile.Transform.ToSource(_end)))
            : null;
        window.SelectOverlay(tile.Tab, _moving?.Id);
        tile.Tab.SetTextSelection(null, null, "");
        CaptureMouse();
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_tile is not { } tile || _start is null)
            return;
        _end = Position(tile, e.GetPosition(this));
        InvalidateVisual();
    }

    protected override async void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_tile is not { } tile || _start is not { } start || Window.GetWindow(this) is not MainWindow window)
            return;
        var end = Position(tile, e.GetPosition(this));
        var moving = _moving;
        var creating = _creating;
        var rectangle = PdfRect.Between(start, end);
        CancelGesture();
        e.Handled = true;
        try
        {
            if (tile.Tab.EditExistingText)
                await window.EditPdfTextAtAsync(tile, end);
            else if (moving is not null)
            {
                if (rectangle.Width > 1 || rectangle.Height > 1)
                    window.MoveOverlay(tile, moving, MoveBounds(tile, moving, start, end));
            }
            else if (creating is { } kind)
                await window.AddOverlayAsync(tile, kind, start, rectangle);
            else if (rectangle.Width > 1 && rectangle.Height > 1)
                await window.SelectTextAsync(tile, tile.Transform.ToSource(rectangle));
        }
        catch (Exception error) { window.Report(error); }
    }

    internal void CancelGesture()
    {
        ClearGesture();
        if (IsMouseCaptured)
            ReleaseMouseCapture();
    }

    private void ClearGesture()
    {
        _start = null;
        _moving = null;
        _creating = null;
        InvalidateVisual();
    }

    private static PdfRect MoveBounds(PageTile tile, OverlayObject overlay, PdfPoint from, PdfPoint to)
    {
        var first = tile.Transform.ToSource(from);
        var last = tile.Transform.ToSource(to);
        var bounds = overlay.Bounds with { X = overlay.Bounds.X + last.X - first.X, Y = overlay.Bounds.Y + last.Y - first.Y };
        return bounds.Clamp(tile.Transform.Source);
    }

    private static PdfPoint Position(PageTile tile, Point point) => new(
        Math.Clamp(point.X / tile.Tab.Scale, 0, tile.Transform.Display.Width),
        Math.Clamp(point.Y / tile.Tab.Scale, 0, tile.Transform.Display.Height));

    private static Rect Pixels(PageTile tile, PdfRect rect)
    {
        var scale = tile.Tab.Scale;
        return new Rect(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale);
    }

    private static readonly Brush MatchBrush = FrozenBrush(110, 255, 222, 64);
    private static readonly Brush ActiveMatchBrush = FrozenBrush(150, 255, 154, 40);
    private static readonly Brush SelectionBrush = FrozenBrush(80, 54, 128, 237);
    private static readonly Pen SelectionPen = MakePen();

    private static Brush FrozenBrush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static Pen MakePen()
    {
        var pen = new Pen(FrozenBrush(255, 22, 77, 189), 1.3) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        return pen;
    }
}

public sealed class ThumbnailImage : Image
{
    private CancellationTokenSource? _request;

    public ThumbnailImage()
    {
        Loaded += (_, _) => UpdateThumbnail();
        DataContextChanged += (_, _) => { if (IsLoaded) UpdateThumbnail(); };
        Unloaded += (_, _) => { _request?.Cancel(); Source = null; };
    }

    private async void UpdateThumbnail()
    {
        _request?.Cancel();
        Source = null;
        if (DataContext is not PageTile tile)
            return;
        using var request = new CancellationTokenSource();
        _request = request;
        try
        {
            var size = tile.Transform.Display;
            var dpi = Math.Clamp(72 * 130 / Math.Max(size.Width, size.Height), 6, 150);
            var raster = await tile.Tab.Document.Pdf.RenderAsync(tile.Reference, dpi, request.Token);
            request.Token.ThrowIfCancellationRequested();
            Source = PageSurface.Decode(raster);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (tile.Tab.Closed || request.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!request.IsCancellationRequested && Window.GetWindow(this) is MainWindow window)
                window.Report(error, dialog: false);
        }
        finally { if (ReferenceEquals(_request, request)) _request = null; }
    }
}
