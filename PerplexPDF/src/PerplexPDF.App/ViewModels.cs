using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using PerplexPDF.Application;
using PerplexPDF.Domain;

namespace PerplexPDF.App;

public abstract class NotifyModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

internal sealed class UiCommand(Action execute, Func<bool>? canExecute) : ICommand
{
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter)
    {
        if (CanExecute(parameter))
            execute();
    }
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public sealed class WorkspaceViewModel : NotifyModel
{
    private DocumentTab? _active;
    private bool _busy;
    private string _status = Strings.Get("Ready");
    private string _progress = "";
    public Dictionary<string, ICommand> Commands { get; } = [];
    public ObservableCollection<DocumentTab> Tabs { get; } = [];

    public DocumentTab? Active
    {
        get => _active;
        set
        {
            if (ReferenceEquals(_active, value))
                return;
            _active = value;
            Notify();
            Notify(nameof(HasDocument));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasDocument => Active is not null;
    public bool Busy
    {
        get => _busy;
        set { _busy = value; Notify(); CommandManager.InvalidateRequerySuggested(); }
    }
    public string Status { get => _status; set { _status = value; Notify(); } }
    public string Progress { get => _progress; set { _progress = value; Notify(); } }
}

public sealed record PageTile(DocumentTab Tab, PageReference Reference, int Number)
{
    public PageTransform Transform => new(Tab.Document.Pdf.Pages[Reference.SourcePage], Reference.Rotation);
    public string Label => Strings.Format("PageNumber", Number);
}

public sealed class DocumentTab : NotifyModel
{
    private IReadOnlyList<PageTile> _pages = [];
    private PageTile? _current;
    private string _name;
    private bool _continuous;
    private double _scale = 96.0 / 72;
    private OverlayKind? _tool;
    private bool _editExistingText;
    private Guid? _selectedId;
    private IReadOnlyList<TextHit> _matches = [];
    private int _matchIndex = -1;
    private PdfRect? _textBounds;
    private Guid? _textPageId;
    private string _selectedText = "";
    private string _bookmarkQuery = "";
    private DocumentProject? _lastVisual;

    public DocumentTab(OpenDocument document, string name, string? recoveredFrom = null)
    {
        Document = document;
        _name = name;
        RecoveredFrom = recoveredFrom;
        Refresh();
    }

    public Guid Id { get; internal init; } = Guid.NewGuid();
    public OpenDocument Document { get; }
    public DocumentProject Project => Document.History.Current;
    public bool Dirty => Document.History.IsDirty;
    public string Title => Dirty ? _name + " •" : _name;
    public string Name { get => _name; set { _name = value; Notify(nameof(Title)); Notify(); } }
    public IReadOnlyList<PageTile> Pages => _pages;
    public IReadOnlyList<PdfBookmark> Bookmarks => FilteringBookmarks
        ? FilterBookmarks(Document.Pdf.Bookmarks, BookmarkQuery.Trim()).ToArray() : Document.Pdf.Bookmarks;
    public bool FilteringBookmarks => !string.IsNullOrWhiteSpace(BookmarkQuery);
    public string BookmarkQuery
    {
        get => _bookmarkQuery;
        set
        {
            _bookmarkQuery = value;
            Notify();
            Notify(nameof(Bookmarks));
            Notify(nameof(FilteringBookmarks));
        }
    }
    public IReadOnlyList<OverlayObject> Comments => Project.Overlays.Where(overlay => overlay.Kind == OverlayKind.Note).ToArray();
    public IReadOnlyList<PageTile> ViewerPages => _continuous ? Pages : Current is { } page ? [page] : [];
    public string PageStatus => Strings.Format("PageCount", Current?.Number ?? 0, Pages.Count);
    public string ChangeStatus => Dirty ? Strings.Get("Edited") : Strings.Get("NoChanges");
    public string ZoomStatus => $"{Scale * 75:0}%";
    public string SearchStatus => Matches.Count == 0
        ? Strings.Get("NoMatches") : Strings.Format("MatchCount", MatchIndex + 1, Matches.Count);
    public string ToolLabel => EditExistingText ? Strings.Get("EditPdfText") :
        Tool is { } kind ? Strings.Get(kind.ToString()) : Strings.Get("Select");
    public SemaphoreSlim DraftGate { get; } = new(1, 1);
    public DocumentProject? DraftSnapshot { get; set; }
    public string? RecoveredFrom { get; set; }
    public bool Closed { get; set; }
    public bool IsTextCopy { get; init; }
    public string SearchQuery { get; set; } = "";

    public PageTile? Current
    {
        get => _current;
        set
        {
            if (value is null || ReferenceEquals(value, _current))
                return;
            _current = value;
            Notify();
            Notify(nameof(PageStatus));
            if (!_continuous)
                Notify(nameof(ViewerPages));
            if (Selected is { } overlay && overlay.PageId != value.Reference.Id)
                SelectOverlay(null);
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public double Scale
    {
        get => _scale;
        set
        {
            value = Math.Clamp(value, .0125, 5.33333);
            if (Math.Abs(_scale - value) < .0001)
                return;
            _scale = value;
            Notify();
            Notify(nameof(ZoomStatus));
        }
    }
    public bool Continuous
    {
        get => _continuous;
        set
        {
            _continuous = value;
            Notify();
            Notify(nameof(ViewerPages));
        }
    }
    public OverlayKind? Tool
    {
        get => _tool;
        set { _tool = value; Notify(); Notify(nameof(ToolLabel)); }
    }
    public bool EditExistingText
    {
        get => _editExistingText;
        set { _editExistingText = value; Notify(); Notify(nameof(ToolLabel)); }
    }
    public OverlayObject? Selected => Project.Overlays.FirstOrDefault(overlay => overlay.Id == _selectedId);
    public OverlayInspector? Inspector { get; private set; }
    public IReadOnlyList<TextHit> Matches => _matches;
    public int MatchIndex => _matchIndex;
    public PdfRect? TextBounds => _textBounds;
    public Guid? TextPageId => _textPageId;
    public string SelectedText => _selectedText;

    public void SetSearch(IReadOnlyList<TextHit> matches, int index)
    {
        _matches = matches;
        _matchIndex = index;
        Notify(nameof(Matches));
        Notify(nameof(SearchStatus));
    }

    private static IEnumerable<PdfBookmark> FilterBookmarks(IEnumerable<PdfBookmark> bookmarks, string query)
    {
        foreach (var bookmark in bookmarks)
        {
            if (bookmark.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                yield return bookmark;
                continue;
            }
            var children = FilterBookmarks(bookmark.Children, query).ToArray();
            if (children.Length > 0)
                yield return bookmark with { Children = children };
        }
    }

    public void SetTextSelection(Guid? page, PdfRect? bounds, string text)
    {
        _textPageId = page;
        _textBounds = bounds;
        _selectedText = text;
        Notify(nameof(TextBounds));
        CommandManager.InvalidateRequerySuggested();
    }

    public void SelectOverlay(Guid? id)
    {
        _selectedId = id;
        Inspector = Selected is { } overlay ? new(overlay) : null;
        Notify(nameof(Selected));
        Notify(nameof(Inspector));
        CommandManager.InvalidateRequerySuggested();
    }

    public void Execute(string requirement, DocumentProject updated)
    {
        if (ReferenceEquals(Project, updated))
            return;
        updated.Validate(Document.Pdf.Pages);
        Document.History.Execute(requirement, updated);
        Refresh();
    }

    public void Refresh()
    {
        var project = Project;
        if (_lastVisual is null || !_lastVisual.Pages.Equals(project.Pages))
        {
            var id = _current?.Reference.Id;
            var index = Math.Clamp((_current?.Number ?? 1) - 1, 0, project.Pages.Length - 1);
            _pages = project.Pages.Select((page, order) => new PageTile(this, page, order + 1)).ToArray();
            _current = _pages.FirstOrDefault(page => page.Reference.Id == id) ?? _pages[index];
            SetSearch([], -1);
            SetTextSelection(null, null, "");
            Notify(nameof(Pages));
            Notify(nameof(Current));
            Notify(nameof(ViewerPages));
            Notify(nameof(PageStatus));
        }
        if (!ReferenceEquals(_lastVisual, project))
        {
            _lastVisual = project;
            Notify(nameof(Project));
            Notify(nameof(Comments));
            SelectOverlay(_selectedId);
        }
        Notify(nameof(Title));
        Notify(nameof(ChangeStatus));
        CommandManager.InvalidateRequerySuggested();
    }
}

public sealed class OverlayInspector(OverlayObject overlay) : NotifyModel
{
    private static readonly string[] BundledFonts = ["DejaVu Sans", "DejaVu Serif", "DejaVu Sans Mono"];
    private static readonly Lazy<string[]> FontNames = new(() => BundledFonts
        .Concat(System.Windows.Media.Fonts.SystemFontFamilies.Select(font => font.Source))
        .Where(DocumentProject.IsFontFamily).Distinct(StringComparer.OrdinalIgnoreCase)
        .Order(StringComparer.CurrentCultureIgnoreCase).ToArray());
    private string _color = $"#{overlay.Color:X8}";

    public string Kind => Strings.Get(overlay.Kind.ToString());
    public string X { get; set; } = $"{overlay.Bounds.X:0.###}";
    public string Y { get; set; } = $"{overlay.Bounds.Y:0.###}";
    public string Width { get; set; } = $"{overlay.Bounds.Width:0.###}";
    public string Height { get; set; } = $"{overlay.Bounds.Height:0.###}";
    public string Text { get; set; } = overlay.Text;
    public bool IsText => overlay.Kind is OverlayKind.Text or OverlayKind.Note;
    public bool HasShapeColor => !IsText && overlay.Kind != OverlayKind.Image;
    public string FontSize { get; set; } = $"{overlay.FontSize:0.##}";
    public IReadOnlyList<string> Fonts { get; } = AvailableFonts(overlay.SystemFont);
    public string FontName { get; set; } = overlay.SystemFont ?? BundledFonts[(int)overlay.Font];
    public bool Bold { get; set; } = overlay.Bold;
    public bool Italic { get; set; } = overlay.Italic;
    public bool Underlined { get; set; } = overlay.Underlined;
    public string Color { get => _color; set { _color = value; Notify(); } }

    internal uint CurrentColor => TryColor(out var color) ? color : overlay.Color;

    internal OverlayObject? ReadAppearance()
    {
        if (overlay.Kind == OverlayKind.Image)
            return overlay;
        if (!TryColor(out var color))
            return null;
        if (!IsText)
            return overlay with { Color = color };
        if (FontName is not { } name || !DocumentProject.IsFontFamily(name) ||
            !double.TryParse(FontSize.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ||
            !double.IsFinite(size) || size is < 4 or > 144)
            return null;
        var bundled = Array.FindIndex(BundledFonts, item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
        return overlay with
        {
            Color = color, Text = Text, FontSize = size, Bold = Bold, Italic = Italic, Underlined = Underlined,
            Font = bundled >= 0 ? (OverlayFont)bundled : overlay.Font,
            SystemFont = bundled >= 0 ? null : name
        };
    }

    private bool TryColor(out uint color)
    {
        color = 0;
        var hex = Color.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8) ||
            !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color))
            return false;
        if (hex.Length == 6)
            color |= 0xFF000000;
        return true;
    }

    private static string[] AvailableFonts(string? selected)
    {
        var names = FontNames.Value;
        return selected is not null && !names.Contains(selected, StringComparer.OrdinalIgnoreCase)
            ? [selected, .. names] : names;
    }
}
