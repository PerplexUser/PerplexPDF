using System.Collections.Immutable;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using static PerplexPDF.Desktop.Dialogs;
using static PerplexPDF.Desktop.Graphics;
using static PerplexPDF.Desktop.Gtk;

namespace PerplexPDF.Desktop;

internal sealed class DocumentTab
{
    internal readonly record struct PageSlot(int Index, PageReference Page, PageTransform Transform, double Top)
    {
        internal PdfRect Bounds(double scale, int areaWidth) => new(Math.Max(12, (areaWidth - Transform.Display.Width * scale) / 2),
            Top, Transform.Display.Width * scale, Transform.Display.Height * scale);
    }

    private readonly GtkWindow _owner;
    private readonly nint _canvas, _scroll, _navigator, _pages, _bookmarks, _bookmarkFilter, _number, _find, _title, _vertical;
    private readonly CancellationTokenSource _life = new();
    private readonly Dictionary<Guid, RasterPage> _rasters = [];
    private readonly HashSet<(Guid Page, int Version)> _pending = [];
    private readonly HashSet<Guid> _failed = [];
    private readonly HashSet<Guid> _thumbnails = [];
    private readonly List<int> _bookmarkPages = [];
    private ImmutableArray<PageReference> _pageOrder;
    private nint[] _thumbWidgets = [];
    private PageSlot[] _slots = [];
    private PageSlot? _pressed;
    private PdfPoint _start, _current;
    private bool _rebuilding, _scrolling;
    private int _revision, _thumbnailRevision;
    private double _scale = 1;
    private int _fit = 1;
    private IReadOnlyList<TextHit> _matches = [];
    private int _match = -1;

    internal nint Root { get; }
    internal nint TabLabel { get; }
    internal Guid Id { get; } = Guid.NewGuid();
    internal OpenDocument Document { get; private set; }
    internal DocumentProject Project => Document.History.Current;
    internal string Name { get; set; }
    internal string BaseName => Path.GetFileNameWithoutExtension(Name);
    internal int Index { get; private set; }
    internal PageReference Page => Project.Pages[Index];
    internal bool Closed { get; private set; }
    internal bool Continuous { get; private set; }
    internal OverlayKind? Tool { get; set; }
    internal bool EditTextMode { get; set; }
    internal bool IsTextCopy { get; set; }
    internal string? RecoveredFrom { get; set; }
    internal DocumentProject? DraftSnapshot { get; set; }
    internal SemaphoreSlim DraftGate { get; } = new(1);
    internal Guid? Selected { get; set; }
    internal string SelectedText { get; set; } = "";
    internal (Guid Page, PdfRect Bounds)? Selection { get; set; }

    internal DocumentTab(GtkWindow owner, OpenDocument document, string name)
    {
        _owner = owner;
        Document = document;
        Name = name;

        TabLabel = gtk_box_new(Horizontal, 5);
        _title = Label(name);
        gtk_label_set_ellipsize(_title, 3);
        gtk_widget_set_size_request(_title, 85, -1);
        Pack(TabLabel, _title, true);
        var close = Button("×", () => owner.Launch(() => owner.CloseTab(this)));
        gtk_widget_set_tooltip_text(close, "Close tab");
        Pack(TabLabel, close);

        Root = gtk_box_new(Vertical, 0);
        var nav = gtk_box_new(Horizontal, 5);
        gtk_container_set_border_width(nav, 5);
        Pack(nav, Button("‹", () => owner.Invoke("Previous")));
        _number = Entry("1", 6);
        gtk_entry_set_width_chars(_number, 5);
        Bind(_number, "activate", new Signal((_, _) =>
        {
            if (int.TryParse(String(gtk_entry_get_text(_number)), out var index)) GoTo(index - 1);
        }));
        Pack(nav, _number);
        Pack(nav, Button("›", () => owner.Invoke("Next")));
        Pack(nav, Button("−", () => owner.Invoke("ZoomOut")));
        Pack(nav, Button("+", () => owner.Invoke("ZoomIn")));
        Pack(nav, Button("Fit page", () => owner.Invoke("FitPage")));
        Pack(nav, Button("Fit width", () => owner.Invoke("FitWidth")));
        _find = Entry("", 512);
        gtk_entry_set_placeholder_text(_find, "Find text");
        Bind(_find, "activate", new Signal((_, _) => owner.Launch(() => owner.Search(this, String(gtk_entry_get_text(_find))))));
        Pack(nav, _find, true);
        Pack(nav, Button("Find", () => owner.Launch(() => owner.Search(this, String(gtk_entry_get_text(_find))))));
        Pack(nav, Button("↑", () => StepMatch(-1)));
        Pack(nav, Button("↓", () => StepMatch(1)));
        Pack(Root, nav);

        var pane = gtk_paned_new(Horizontal);
        _navigator = gtk_notebook_new();
        _pages = gtk_list_box_new();
        gtk_notebook_append_page(_navigator, Scroll(_pages, 146, -1), Label("Pages"));
        Bind(_pages, "row-activated", new ObjectSignal((_, row, _) =>
        {
            if (!_rebuilding) GoTo(gtk_list_box_row_get_index(row));
        }));
        var outline = gtk_box_new(Vertical, 5);
        _bookmarkFilter = Entry("", 512);
        gtk_entry_set_placeholder_text(_bookmarkFilter, "Filter bookmarks");
        Pack(outline, _bookmarkFilter);
        _bookmarks = gtk_list_box_new();
        Pack(outline, Scroll(_bookmarks, 146, -1), true);
        gtk_notebook_append_page(_navigator, outline, Label("Bookmarks"));
        Bind(_bookmarkFilter, "changed", new Signal((_, _) => RefreshBookmarks()));
        Bind(_bookmarks, "row-activated", new ObjectSignal((_, row, _) =>
        {
            var index = gtk_list_box_row_get_index(row);
            if (index >= 0 && index < _bookmarkPages.Count && _bookmarkPages[index] >= 0) GoTo(_bookmarkPages[index]);
        }));
        gtk_paned_pack1(pane, _navigator, 0, 0);

        _canvas = gtk_drawing_area_new();
        gtk_widget_add_events(_canvas, PointerMotion | ButtonPress | ButtonRelease);
        Bind(_canvas, "draw", new EventSignal((_, context, _) => OnDraw(context)));
        Bind(_canvas, "button-press-event", new EventSignal((_, pointer, _) => Press(pointer) ? 1 : 0));
        Bind(_canvas, "motion-notify-event", new EventSignal((_, pointer, _) => Motion(pointer) ? 1 : 0));
        Bind(_canvas, "button-release-event", new EventSignal((_, pointer, _) => Release(pointer) ? 1 : 0));
        _scroll = Scroll(_canvas);
        _vertical = gtk_scrolled_window_get_vadjustment(_scroll);
        Bind(_scroll, "size-allocate", new ObjectSignal((_, _, _) => { if (_fit != 0) ApplyFit(); }));
        Bind(_vertical, "value-changed", new Signal((_, _) => UpdateScrollPage()));
        gtk_paned_pack2(pane, _scroll, 1, 0);
        gtk_paned_set_position(pane, 176);
        Pack(Root, pane, true);
        Refresh();
    }

    private static nint Button(string label, Action action)
    {
        var button = gtk_button_new_with_label(label);
        Bind(button, "clicked", new Signal((_, _) => action()));
        return button;
    }

    internal void ShowNavigator(int tab) => gtk_notebook_set_current_page(_navigator, tab);
    internal void FocusSearch() => gtk_widget_grab_focus(_find);
    internal void Draw() { if (!Closed) gtk_widget_queue_draw(_canvas); }
    internal void UpdateTitle() => gtk_label_set_text(_title, Name + (Document.History.IsDirty ? " •" : ""));

    internal void Refresh()
    {
        if (!_pageOrder.Equals(Project.Pages))
        {
            var previous = !_pageOrder.IsDefault && Index < _pageOrder.Length ? _pageOrder[Index].Id : Guid.Empty;
            _pageOrder = Project.Pages;
            var found = Array.FindIndex(_pageOrder.ToArray(), page => page.Id == previous);
            Index = found < 0 ? Math.Clamp(Index, 0, _pageOrder.Length - 1) : found;
            InvalidateRasters();
            _matches = [];
            _match = -1;
            Selection = null;
            SelectedText = "";
            RebuildThumbnails();
            RefreshBookmarks();
        }
        if (Selected is { } id && !Project.Overlays.Any(item => item.Id == id))
            Selected = null;
        UpdateTitle();
        UpdatePageIndicator();
        Layout();
        Draw();
    }

    internal void ReplaceDocument(OpenDocument document)
    {
        Document = document;
        _pageOrder = default;
        Refresh();
    }

    internal void GoTo(int index)
    {
        if (Closed || !_owner.CanInteract(this)) return;
        SelectPage(index, moveScroll: true);
    }

    private void SelectPage(int index, bool moveScroll)
    {
        Index = Math.Clamp(index, 0, Project.Pages.Length - 1);
        Selected = null;
        Selection = null;
        SelectedText = "";
        UpdatePageIndicator();
        if (!Continuous)
        {
            if (_fit != 0) ApplyFit();
            Layout();
        }
        if (moveScroll) ScrollToPage();
        _owner.Status("");
        _owner.UpdateActive();
        Draw();
    }

    internal void SetContinuous(bool value)
    {
        Continuous = value;
        Layout();
        ScrollToPage();
    }

    internal void Fit(bool width)
    {
        _fit = width ? 2 : 1;
        ApplyFit();
        ScrollToPage();
    }

    private void ApplyFit()
    {
        var width = gtk_widget_get_allocated_width(_scroll) - 31;
        var height = gtk_widget_get_allocated_height(_scroll) - 30;
        if (Closed || width < 60 || height < 60)
            return;
        var geometry = Transform(Page).Display;
        var scale = width / geometry.Width;
        if (_fit == 1) scale = Math.Min(scale, height / geometry.Height);
        AdjustScale(scale);
    }

    internal void ZoomBy(double factor) => SetZoom(_scale * factor);

    internal void SetZoom(double scale)
    {
        _fit = 0;
        AdjustScale(scale);
        ScrollToPage();
    }

    private void AdjustScale(double scale)
    {
        scale = Math.Clamp(scale, .0125, 5.3333);
        if (Math.Abs(_scale - scale) < .002)
            return;
        _scale = scale;
        InvalidateRasters();
        Layout();
    }

    private PageTransform Transform(PageReference page) => new(Document.Pdf.Pages[page.SourcePage], page.Rotation);

    private void Layout()
    {
        if (Closed) return;
        var y = 12.0;
        var width = 1.0;
        var slots = new List<PageSlot>();
        for (var index = Continuous ? 0 : Index; index < (Continuous ? Project.Pages.Length : Index + 1); index++)
        {
            var page = Project.Pages[index];
            var transform = Transform(page);
            slots.Add(new(index, page, transform, y));
            width = Math.Max(width, transform.Display.Width * _scale);
            y += transform.Display.Height * _scale + 12;
        }
        _slots = slots.ToArray();
        gtk_widget_set_size_request(_canvas, (int)Math.Ceiling(width + 24), (int)Math.Ceiling(y));
        Draw();
    }

    private void ScrollToPage()
    {
        var y = Continuous ? _slots.First(slot => slot.Index == Index).Top - 12 : 0;
        _scrolling = true;
        Post(() =>
        {
            try { if (!Closed) gtk_adjustment_set_value(_vertical, y); }
            finally { _scrolling = false; }
        });
    }

    private void UpdateScrollPage()
    {
        if (!Continuous || _scrolling || _rebuilding || !_owner.CanInteract(this) || _slots.Length == 0)
            return;
        var top = gtk_adjustment_get_value(_vertical);
        var center = top + gtk_adjustment_get_page_size(_vertical) / 2;
        var index = _slots.TakeWhile(slot => slot.Top <= center).LastOrDefault().Index;
        if (index != Index) SelectPage(index, moveScroll: false);
    }

    private void RebuildThumbnails()
    {
        _rebuilding = true;
        _thumbnailRevision++;
        _thumbnails.Clear();
        ClearRows(_pages);
        _thumbWidgets = new nint[Project.Pages.Length];
        for (var index = 0; index < _thumbWidgets.Length; index++)
        {
            var row = gtk_box_new(Vertical, 4);
            gtk_container_set_border_width(row, 6);
            var image = gtk_image_new();
            _thumbWidgets[index] = image;
            Pack(row, image);
            Pack(row, Label($"Page {index + 1}"));
            gtk_list_box_insert(_pages, row, -1);
        }
        gtk_widget_show_all(_pages);
        _rebuilding = false;
    }

    private void UpdatePageIndicator()
    {
        gtk_entry_set_text(_number, (Index + 1).ToString());
        if (gtk_list_box_get_row_at_index(_pages, Index) is var row && row != 0)
            gtk_list_box_select_row(_pages, row);
        for (var index = Math.Max(0, Index - 2); index < Math.Min(Project.Pages.Length, Index + 5); index++)
        {
            var page = Project.Pages[index];
            if (_thumbnails.Add(page.Id))
                _ = Thumbnail(page, index, _thumbnailRevision);
        }
    }

    private async Task Thumbnail(PageReference page, int index, int revision)
    {
        try
        {
            var size = Transform(page).Display;
            var raster = await Document.Pdf.RenderAsync(page, Math.Clamp(72 * 96 / Math.Max(size.Width, size.Height), 6, 150), _life.Token);
            if (Closed || revision != _thumbnailRevision) return;
            using var surface = new Surface(cairo_image_surface_create(0, raster.Width, raster.Height));
            var context = cairo_create(surface.Handle);
            try { Raster(context, raster, new(0, 0, raster.Width, raster.Height)); }
            finally { cairo_destroy(context); }
            gtk_image_set_from_surface(_thumbWidgets[index], surface.Handle);
        }
        catch (Exception error) when (error is OperationCanceledException || Closed) { }
        catch (Exception error) { _owner.Report(error, modal: false); }
    }

    private static void ClearRows(nint list)
    {
        while (gtk_list_box_get_row_at_index(list, 0) is var row && row != 0)
            gtk_widget_destroy(row);
    }

    private void RefreshBookmarks()
    {
        ClearRows(_bookmarks);
        _bookmarkPages.Clear();
        var filter = String(gtk_entry_get_text(_bookmarkFilter)).Trim();
        var destination = Project.Pages.Select((page, index) => (page.SourcePage, index))
            .GroupBy(item => item.SourcePage).ToDictionary(group => group.Key, group => group.First().index);
        Add(Document.Pdf.Bookmarks, 0, filter.Length == 0);
        if (_bookmarkPages.Count == 0)
            gtk_list_box_insert(_bookmarks, Label(filter.Length == 0 ? "No bookmarks" : "No matches"), -1);
        gtk_widget_show_all(_bookmarks);

        void Add(IEnumerable<PdfBookmark> nodes, int depth, bool showAll)
        {
            foreach (var bookmark in nodes)
            {
                var matches = showAll || bookmark.Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
                if (matches)
                {
                    var page = bookmark.SourcePage is { } source && destination.TryGetValue(source, out var index) ? index : -1;
                    var label = Label(new string(' ', Math.Min(depth * 3, 30)) + bookmark.Title);
                    gtk_label_set_ellipsize(label, 3);
                    gtk_widget_set_tooltip_text(label, bookmark.Title);
                    gtk_list_box_insert(_bookmarks, label, -1);
                    _bookmarkPages.Add(page);
                }
                Add(bookmark.Children, depth + 1, matches);
            }
        }
    }

    internal void SearchResults(IReadOnlyList<TextHit> matches)
    {
        _matches = matches;
        _match = -1;
        if (matches.Count == 0)
            _owner.Status("No matches.");
        else
            StepMatch(1, duringSearch: true);
        Draw();
    }

    private void StepMatch(int delta, bool duringSearch = false)
    {
        if (_matches.Count == 0 || (!duringSearch && !_owner.CanInteract(this))) return;
        _match = (_match + delta + _matches.Count) % _matches.Count;
        var match = _matches[_match];
        var index = Array.FindIndex(Project.Pages.ToArray(), page => page.Id == match.PageId);
        if (index >= 0) SelectPage(index, moveScroll: true);
        _owner.Status($"Match {_match + 1} / {_matches.Count}");
    }

    private int OnDraw(nint context)
    {
        if (Closed) return 0;
        try
        {
            Color(context, 0xffdfe4ea);
            cairo_paint(context);
            cairo_clip_extents(context, out _, out var top, out _, out var bottom);
            var width = gtk_widget_get_allocated_width(_canvas);
            var byPage = Project.Overlays.ToLookup(overlay => overlay.PageId);
            foreach (var slot in _slots)
            {
                var bounds = slot.Bounds(_scale, width);
                if (bounds.Y > bottom) break;
                if (bounds.Y + bounds.Height < top) continue;
                Fill(context, bounds with { X = bounds.X + 2, Y = bounds.Y + 3 }, 0x22000000);
                Fill(context, bounds, 0xffffffff);
                if (_rasters.TryGetValue(slot.Page.Id, out var raster))
                    Raster(context, raster, bounds);
                else if (!_failed.Contains(slot.Page.Id) && _pending.Add((slot.Page.Id, _revision)))
                    _ = LoadPage(slot, _revision);

                cairo_save(context);
                try
                {
                    cairo_rectangle(context, bounds.X, bounds.Y, bounds.Width, bounds.Height);
                    cairo_clip(context);
                    cairo_translate(context, bounds.X, bounds.Y);
                    cairo_scale(context, _scale, _scale);
                    CairoOverlayEngine.Paint(context, slot.Transform, byPage[slot.Page.Id], Project, CancellationToken.None);
                    foreach (var match in _matches.Where(match => match.PageId == slot.Page.Id))
                        foreach (var rect in match.Bounds) Fill(context, slot.Transform.ToDisplay(rect), 0x60ffce32);
                    if (Selection is { } selection && selection.Page == slot.Page.Id)
                        Fill(context, slot.Transform.ToDisplay(selection.Bounds), 0x604f8ad5);
                    var selected = byPage[slot.Page.Id].FirstOrDefault(item => item.Id == Selected);
                    if (selected is not null)
                    {
                        var rect = slot.Transform.ToDisplay(selected.Bounds);
                        Color(context, 0xff1e59bf);
                        cairo_set_line_width(context, 1.2 / _scale);
                        cairo_rectangle(context, rect.X, rect.Y, rect.Width, rect.Height);
                        cairo_stroke(context);
                    }
                    if (_pressed is { } pressed && pressed.Page.Id == slot.Page.Id)
                        Fill(context, PdfRect.Between(_start, _current), 0x604f8ad5);
                }
                finally { cairo_restore(context); }
            }
        }
        catch (Exception error) { _owner.Report(error, modal: false); }
        return 1;
    }

    private async Task LoadPage(PageSlot slot, int revision)
    {
        try
        {
            var size = slot.Transform.Display;
            var dpi = Math.Clamp(Math.Min(_scale * 72, 72 * 2_800 / Math.Max(size.Width, size.Height)), 6, 300);
            var raster = await Document.Pdf.RenderAsync(slot.Page, dpi, _life.Token);
            if (Closed || revision != _revision) return;
            while (_rasters.Count > 0 && (_rasters.Count >= 24 ||
                _rasters.Values.Sum(value => (long)value.Bgra.Length) + raster.Bgra.Length > 64L * 1024 * 1024))
                _rasters.Remove(_rasters.Keys.First());
            _rasters[slot.Page.Id] = raster;
            Draw();
        }
        catch (Exception error) when (error is OperationCanceledException || Closed) { }
        catch (Exception error)
        {
            if (revision == _revision) _failed.Add(slot.Page.Id);
            _owner.Report(error, modal: false);
        }
        finally { _pending.Remove((slot.Page.Id, revision)); }
    }

    private void InvalidateRasters()
    {
        _revision++;
        _rasters.Clear();
        _failed.Clear();
    }

    private bool Press(nint pointer)
    {
        if (!_owner.CanInteract(this) || gdk_event_get_button(pointer, out var button) == 0 || button != 1 ||
            gdk_event_get_coords(pointer, out var x, out var y) == 0)
            return false;
        var width = gtk_widget_get_allocated_width(_canvas);
        foreach (var slot in _slots)
        {
            if (!slot.Bounds(_scale, width).Contains(new(x, y))) continue;
            _pressed = slot;
            _start = _current = Point(slot, x, y);
            return true;
        }
        return false;
    }

    private bool Motion(nint pointer)
    {
        if (_pressed is not { } slot || gdk_event_get_coords(pointer, out var x, out var y) == 0)
            return false;
        _current = Point(slot, x, y);
        Draw();
        return true;
    }

    private bool Release(nint pointer)
    {
        if (_pressed is not { } slot || gdk_event_get_button(pointer, out var button) == 0 || button != 1)
            return false;
        _pressed = null;
        if (!_owner.CanInteract(this) || gdk_event_get_coords(pointer, out var x, out var y) == 0)
            return false;
        var start = _start;
        var end = Point(slot, x, y);
        Index = slot.Index;
        UpdatePageIndicator();
        _owner.Launch(() => _owner.Gesture(this, slot, start, end));
        Draw();
        return true;
    }

    private PdfPoint Point(PageSlot slot, double x, double y)
    {
        var bounds = slot.Bounds(_scale, gtk_widget_get_allocated_width(_canvas));
        return new(Math.Clamp((x - bounds.X) / _scale, 0, slot.Transform.Display.Width),
            Math.Clamp((y - bounds.Y) / _scale, 0, slot.Transform.Display.Height));
    }

    internal void Stop()
    {
        Closed = true;
        _life.Cancel();
        _rasters.Clear();
        _life.Dispose();
    }
}
