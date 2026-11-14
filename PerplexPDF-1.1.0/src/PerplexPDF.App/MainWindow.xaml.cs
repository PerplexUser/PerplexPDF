using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using PerplexPDF.Infrastructure.Ocr;
using PerplexPDF.Infrastructure.Pdf;
using PerplexPDF.Infrastructure.Store;

namespace PerplexPDF.App;

public partial class MainWindow : Window
{
    private const int MaxTabs = 16;
    private const string PageDragFormat = "PerplexPDF.PageOrder";
    private readonly WorkspaceViewModel _view = new();
    private readonly string[] _paths;
    private readonly Dialogs _dialogs;
    private readonly LocalWorkspace _local;
    private readonly LocalWorkspace.LocalDirectory _draftSession;
    private readonly LocalDiagnostics _diagnostics;
    private readonly DispatcherTimer _draftTimer;
    private readonly bool _ocrAvailable;
    private CancellationTokenSource? _operation;
    private CancellationTokenSource? _textSelection;
    private bool _savingDrafts;
    private bool _allowClose;
    private bool _closing;
    private bool _started;
    private bool _suppressScroll;
    private bool _fitWidth;
    private bool _fitPage;
    private bool _defaultContinuous;
    private Point _dragStart;
    private PageTile? _dragPage;
    private PageTile? _draggingPage;
    internal PdfWorkspace Workspace { get; }
    internal bool IsBusy => _view.Busy || _closing;
    public string OcrToolTip => Strings.Get(_ocrAvailable ? "OcrDialog" : "OcrRuntimeUnavailable");
    private DocumentTab? Active => _view.Active;

    public MainWindow() : this([]) { }

    public MainWindow(string[] paths)
    {
        _paths = paths;
        var root = AppContext.BaseDirectory;
        _local = new LocalWorkspace();
        _draftSession = _local.CreateRecoverySession();
        _diagnostics = new LocalDiagnostics(_local.Root);
        var renderer = new PdfiumRenderer(Path.Combine(root, "tools", "pdfium", "pdfium.dll"));
        var structure = new QpdfStructure(Path.Combine(root, "tools", "qpdf", "qpdf.exe"), _local);
        var overlays = new SkiaOverlayEngine(Path.Combine(root, "assets", "fonts"));
        var tesseract = Path.Combine(root, "tools", "tesseract");
        var executable = Path.Combine(tesseract, "tesseract.exe");
        var tessdata = Path.Combine(tesseract, "tessdata");
        _ocrAvailable = File.Exists(executable) &&
            File.Exists(Path.Combine(tessdata, "eng.traineddata")) &&
            File.Exists(Path.Combine(tessdata, "deu.traineddata")) &&
            File.Exists(Path.Combine(tessdata, "pdf.ttf")) &&
            File.Exists(Path.Combine(tessdata, "configs", "pdf"));
        var ocr = new TesseractOcr(executable, tessdata, renderer, structure, _local);
        Workspace = new(renderer, structure, overlays, ocr, new ZipProjectStore(_local), _local);
        _dialogs = new Dialogs(this);
        _draftTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _draftTimer.Tick += SaveDrafts;
        InitializeComponent();
        Width = Math.Max(MinWidth, Math.Min(Width, SystemParameters.WorkArea.Width));
        Height = Math.Max(MinHeight, Math.Min(Height, SystemParameters.WorkArea.Height));
        RegisterCommands();
        DataContext = _view;
        Viewer.SizeChanged += (_, _) => { if (_fitPage || _fitWidth) Fit(_fitWidth); };
        PreviewKeyDown += CopyKeyDown;
    }

    private void RegisterCommands()
    {
        bool Ready() => !IsBusy;
        bool DocumentReady() => Ready() && Active is not null;
        bool HasPage() => DocumentReady() && Active?.Current is not null;
        void Add(string name, Action action, Func<bool>? enabled = null) =>
            _view.Commands.Add(name, new UiCommand(action, enabled));
        void Async(string name, Func<Task> action, Func<bool>? enabled = null) =>
            Add(name, async () =>
            {
                try { await action(); }
                catch (Exception error) { Report(error); }
            }, enabled);

        Async("NewPdf", NewPdfAsync, () => Ready() && _view.Tabs.Count < MaxTabs);
        Async("OpenPdf", () => OpenDialogAsync(false), Ready);
        Async("OpenProject", () => OpenDialogAsync(true), Ready);
        Async("SaveProject", () => SaveProjectAsync(Active!, false), DocumentReady);
        Async("SaveProjectAs", () => SaveProjectAsync(Active!, true), DocumentReady);
        Async("SavePdf", () => ExportPdfAsync(extract: false), DocumentReady);
        Async("ExportWord", () => ConvertDocumentAsync(DocumentExportFormat.Word), DocumentReady);
        Async("ExportEpub", () => ConvertDocumentAsync(DocumentExportFormat.Epub), DocumentReady);
        Async("Png", ExportPngAsync, HasPage);
        Async("Print", PrintAsync, HasPage);
        Async("Close", () => CloseTabAsync(Active!), DocumentReady);
        Add("Exit", Close, Ready);
        Add("Undo", () => History(redo: false), () => DocumentReady() && Active!.Document.History.CanUndo);
        Add("Redo", () => History(redo: true), () => DocumentReady() && Active!.Document.History.CanRedo);
        Add("Previous", () => StepPage(-1), () => Active?.Current?.Number > 1);
        Add("Next", () => StepPage(1), () => Active?.Current?.Number < Active?.Pages.Count);
        Add("Find", ShowSearch, () => Active is not null);
        Async("Search", SearchAsync, DocumentReady);
        Add("PreviousMatch", () => StepMatch(-1), () => Active?.Matches.Count > 0);
        Add("NextMatch", () => StepMatch(1), () => Active?.Matches.Count > 0);
        Add("CloseSearch", CloseSearch);
        Add("Copy", Copy, () => !string.IsNullOrEmpty(Active?.SelectedText));
        Add("ZoomIn", () => Zoom(1.25), () => Active is not null);
        Add("ZoomOut", () => Zoom(.8), () => Active is not null);
        Add("FitPage", () => Fit(false), () => Active is not null);
        Add("FitWidth", () => Fit(true), () => Active is not null);
        Add("ActualSize", () => { _fitPage = _fitWidth = false; if (Active is { } tab) tab.Scale = 96.0 / 72; }, () => Active is not null);
        Add("Single", () => SetContinuous(false), () => Active is not null);
        Add("Continuous", () => SetContinuous(true), () => Active is not null);
        Add("Thumbnails", () => NavigatorTabs.SelectedIndex = 0, () => Active is not null);
        Add("Bookmarks", () => NavigatorTabs.SelectedIndex = 1, () => Active is not null);
        Add("RotateRight", () => EditPage((project, id) => PageEdits.Rotate(project, id, 90)), HasPage);
        Add("RotateLeft", () => EditPage((project, id) => PageEdits.Rotate(project, id, -90)), HasPage);
        Add("MoveUp", () => MovePage(-1), () => HasPage() && Active!.Current!.Number > 1);
        Add("MoveDown", () => MovePage(1), () => HasPage() && Active!.Current!.Number < Active!.Pages.Count);
        Add("DeletePage", DeletePage, () => HasPage() && Active!.Pages.Count > 1);
        Add("DuplicatePage", DuplicatePage, () => HasPage() && Active!.Pages.Count < DocumentProject.MaxPages);
        Async("Extract", () => ExportPdfAsync(extract: true), HasPage);
        Async("Split", SplitAsync, HasPage);
        Async("Merge", MergeAsync, Ready);
        Async("Images", ImagesAsync, Ready);
        Async("Ocr", OcrAsync, () => HasPage() && _ocrAvailable);
        foreach (var kind in Enum.GetValues<OverlayKind>())
            Add(kind.ToString(), () => SetTool(kind), HasPage);
        Add("SignatureImage", () => SetTool(OverlayKind.Image, "SignatureImageHint"), HasPage);
        Add("EditPdfText", BeginPdfTextEdit, HasPage);
        Add("Select", () => SetTool(null), HasPage);
        Add("ApplyOverlay", ApplyOverlay, () => HasPage() && Active!.Selected is not null);
        Add("DeleteOverlay", DeleteOverlay, () => HasPage() && Active!.Selected is not null);
        Add("Cancel", Cancel);
        Add("Settings", Settings, Ready);
        Add("DocumentInfo", DocumentInfo, () => Active is not null);
        Add("Help", () => _dialogs.Text(Strings.Get("QuickGuide"), Strings.Get("HelpText")));
        Add("About", () => _dialogs.About(() => ShowBundledText("MitLicense", "LICENSE"),
            () => ShowBundledText("Notices", "THIRD_PARTY_NOTICES.txt")));
        Add("Notices", () => ShowBundledText("Notices", "THIRD_PARTY_NOTICES.txt"));
    }

    private async void Start(object sender, RoutedEventArgs e)
    {
        if (_started)
            return;
        _started = true;
        try
        {
            if (_paths.Length != 0)
                await OpenBatchAsync(_paths);
            var drafts = await Task.Run(_local.FindDrafts);
            if (!IsBusy && !_allowClose && _view.Tabs.Count < MaxTabs && drafts.Count != 0 && Ask("RecoverDrafts", drafts.Count))
                await OpenBatchAsync(drafts.Take(MaxTabs - _view.Tabs.Count), recovered: true);
            await Task.Run(_local.CleanStaleTemp);
        }
        catch (Exception error) { Report(error); }
        if (!_closing && !_allowClose)
            _draftTimer.Start();
    }

    private void AddTab(OpenDocument document, string name, string? recoveredFrom = null)
    {
        if (_view.Tabs.Count >= MaxTabs)
        {
            document.Dispose();
            throw new AppFailure("TabLimit");
        }
        var tab = new DocumentTab(document, name, recoveredFrom) { Continuous = _defaultContinuous };
        _view.Tabs.Add(tab);
        _view.Active = tab;
    }

    private async Task<bool> RunAsync(string label, Func<CancellationToken, Task> action, bool allowClosing = false)
    {
        if (_view.Busy || _closing && !allowClosing)
            return false;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        _view.Progress = Strings.Get(label);
        _view.Busy = true;
        try
        {
            await action(operation.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            _view.Status = Strings.Get("Canceled");
            return false;
        }
        catch (Exception error)
        {
            Report(error);
            return false;
        }
        finally
        {
            _operation = null;
            _view.Progress = "";
            _view.Busy = false;
        }
    }

    internal void Report(Exception error, bool dialog = true)
    {
        if (error is OperationCanceledException)
            return;
        var failure = error as AppFailure ?? new AppFailure(error switch
        {
            UnauthorizedAccessException => "PermissionError",
            FileNotFoundException or DirectoryNotFoundException => "FileMissing",
            IOException => "FileError",
            InvalidDataException => "InvalidProject",
            DllNotFoundException or BadImageFormatException or EntryPointNotFoundException => "DependencyMissing",
            _ => "UnexpectedError"
        }, error);
        _diagnostics.Record(failure);
        var message = Strings.Get(failure.Code);
        _view.Status = message;
        if (dialog)
            MessageBox.Show(this, $"{message}\n\n{Strings.Format("Diagnostic", failure.Code, failure.DiagnosticId.ToString("N"))}",
                "PerplexPDF", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool Ask(string key, params object[] values) => MessageBox.Show(this,
        Strings.Format(key, values), "PerplexPDF", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    private void Cancel()
    {
        if (_operation is { } operation)
        {
            operation.Cancel();
            _view.Progress = Strings.Get("Canceling");
            return;
        }
        _textSelection?.Cancel();
        foreach (var page in Children<PageSurface>(Viewer))
            page.CancelGesture();
        if (Active is { } tab)
        {
            if (tab.Tool is not null || tab.EditExistingText)
                SetTool(null);
            else if (SearchBar.Visibility == Visibility.Visible)
                CloseSearch();
            else
            {
                tab.SelectOverlay(null);
                tab.SetTextSelection(null, null, "");
            }
        }
    }

    private void Settings()
    {
        if (Ask(_defaultContinuous ? "UseSingleByDefault" : "UseContinuousByDefault"))
            _defaultContinuous = !_defaultContinuous;
    }

    private void DocumentInfo()
    {
        if (Active is not { } tab)
            return;
        var info = tab.Document.Pdf.Metadata;
        _dialogs.Text(Strings.Get("DocumentInfo"),
            Strings.Format("InfoText", info.Title, info.Author, info.Keywords, tab.Pages.Count,
                Strings.Get(info.Signed ? "SignaturePresent" : "SignatureAbsent")));
    }

    private void ShowBundledText(string title, string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        try { _dialogs.Text(Strings.Get(title), File.ReadAllText(path)); }
        catch (Exception error) { Report(error); }
    }

    private void TabSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, TabStrip))
            return;
        _textSelection?.Cancel();
        _fitPage = _fitWidth = false;
        if (TabStrip.SelectedItem is DocumentTab tab)
        {
            _view.Active = tab;
            _view.Status = Strings.Get("Ready");
            ScheduleScroll(tab.Current);
        }
    }

    internal void ActivatePage(PageTile tile, bool scroll)
    {
        if (!ReferenceEquals(Active, tile.Tab) || !tile.Tab.Pages.Contains(tile))
            return;
        tile.Tab.Current = tile;
        if (scroll)
        {
            if (_fitPage || _fitWidth)
                Fit(_fitWidth, scroll: false);
            ScheduleScroll(tile);
        }
    }

    private void ScheduleScroll(PageTile? tile)
    {
        if (tile is null)
            return;
        _suppressScroll = true;
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                if (ReferenceEquals(Active, tile.Tab) && ReferenceEquals(tile.Tab.Current, tile))
                {
                    if (tile.Tab.Continuous)
                        Viewer.ScrollIntoView(tile);
                    else if (Children<ScrollViewer>(Viewer).FirstOrDefault() is { } scroller)
                        scroller.ScrollToTop();
                    Thumbnails.ScrollIntoView(tile);
                }
            }
            finally { _suppressScroll = false; }
        }, DispatcherPriority.Loaded);
    }

    private void ThumbnailSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Thumbnails.SelectedItem is PageTile tile && !ReferenceEquals(Active?.Current, tile))
            ActivatePage(tile, scroll: true);
    }

    private void BookmarkSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (Active is { } tab && e.NewValue is PdfBookmark { SourcePage: { } index })
        {
            var page = tab.Pages.FirstOrDefault(item => item.Reference.SourcePage == index);
            if (page is not null)
                ActivatePage(page, scroll: true);
            else
                _view.Status = Strings.Get("BookmarkRemoved");
        }
    }

    private void CommentSelected(object sender, SelectionChangedEventArgs e)
    {
        if (Active is { } tab && Comments.SelectedItem is OverlayObject note &&
            tab.Pages.FirstOrDefault(page => page.Reference.Id == note.PageId) is { } tile)
        {
            ActivatePage(tile, scroll: true);
            tab.SelectOverlay(note.Id);
        }
    }

    private void StepPage(int delta)
    {
        if (Active?.Current is { } page)
        {
            var index = Math.Clamp(page.Number - 1 + delta, 0, page.Tab.Pages.Count - 1);
            ActivatePage(page.Tab.Pages[index], scroll: true);
        }
    }

    private void PageNumberKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Active is not { } tab)
            return;
        if (int.TryParse(PageNumberBox.Text, out var number) && number >= 1 && number <= tab.Pages.Count)
            ActivatePage(tab.Pages[number - 1], scroll: true);
        PageNumberBox.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
        e.Handled = true;
    }

    private void ViewerScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (_suppressScroll || Active is not { Continuous: true } tab || e.VerticalChange == 0 || e.OriginalSource is not ScrollViewer)
            return;
        var middle = Viewer.ActualHeight / 2;
        var visible = Children<PageSurface>(Viewer)
            .Where(surface => surface.Tile?.Tab == tab && surface.IsVisible)
            .MinBy(surface => Math.Abs(surface.TranslatePoint(new Point(0, surface.ActualHeight / 2), Viewer).Y - middle));
        if (visible?.Tile is { } tile)
            ActivatePage(tile, scroll: false);
    }

    private void ViewerWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Zoom(e.Delta > 0 ? 1.18 : 1 / 1.18);
            e.Handled = true;
        }
    }

    private void Zoom(double factor)
    {
        if (Active is not { } tab)
            return;
        _fitWidth = _fitPage = false;
        tab.Scale *= factor;
    }

    private void Fit(bool width, bool scroll = true)
    {
        if (Active?.Current is not { } page || Viewer.ActualWidth < 100 || Viewer.ActualHeight < 70)
            return;
        _fitWidth = width;
        _fitPage = !width;
        var geometry = page.Transform.Display;
        var horizontal = (Viewer.ActualWidth - 65) / geometry.Width;
        page.Tab.Scale = width ? horizontal : Math.Min(horizontal, (Viewer.ActualHeight - 48) / geometry.Height);
        if (scroll)
            ScheduleScroll(page);
    }

    private void SetContinuous(bool continuous)
    {
        if (Active is not { } tab)
            return;
        tab.Continuous = continuous;
        ScheduleScroll(tab.Current);
    }

    private void History(bool redo)
    {
        if (Active is not { } tab)
            return;
        if (redo)
            tab.Document.History.Redo();
        else
            tab.Document.History.Undo();
        tab.Refresh();
        ScheduleScroll(tab.Current);
    }

    private void EditPage(Func<DocumentProject, Guid, DocumentProject> edit)
    {
        if (Active?.Current is not { } page)
            return;
        try
        {
            page.Tab.Execute("F-006", edit(page.Tab.Project, page.Reference.Id));
            if (_fitPage || _fitWidth)
                Fit(_fitWidth, scroll: false);
            ScheduleScroll(page.Tab.Current);
        }
        catch (Exception error) { Report(error); }
    }

    private void MovePage(int delta) => EditPage((project, id) => PageEdits.Move(project, id, Active!.Current!.Number - 1 + delta));

    private void DeletePage()
    {
        if (Active?.Current is { } page && Ask("ConfirmDeletePage", page.Number))
            EditPage(PageEdits.Delete);
    }

    private void DuplicatePage()
    {
        if (Active is not { Current: { } page } tab)
            return;
        var copy = PageEdits.Duplicate(tab.Project, page.Reference.Id);
        if (ReferenceEquals(copy, tab.Project))
            return;
        if (copy.Overlays.Length > DocumentProject.MaxOverlays)
        {
            Report(new AppFailure("SizeLimit"));
            return;
        }
        tab.Execute("DuplicatePage", copy);
        ActivatePage(tab.Pages[page.Number], scroll: true);
        _view.Status = Strings.Get("PageDuplicated");
    }

    private void ThumbnailMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Thumbnails);
        _dragPage = ItemAt(e.OriginalSource as DependencyObject);
    }

    private void ThumbnailMouseMove(object sender, MouseEventArgs e)
    {
        if (IsBusy || e.LeftButton != MouseButtonState.Pressed || _dragPage is not { } page)
            return;
        var point = e.GetPosition(Thumbnails);
        if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragPage = null;
        _draggingPage = page;
        try
        {
            var data = new DataObject(PageDragFormat, page.Reference.Id.ToString("N"));
            DragDrop.DoDragDrop(Thumbnails, data, DragDropEffects.Move);
        }
        finally { _draggingPage = null; }
    }

    private void ThumbnailDragOver(object sender, DragEventArgs e)
    {
        if (_draggingPage is null || !e.Data.GetDataPresent(PageDragFormat, autoConvert: false))
            return;
        e.Effects = IsBusy ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    private void ThumbnailDrop(object sender, DragEventArgs e)
    {
        if (IsBusy || _draggingPage is not { } source || !e.Data.GetDataPresent(PageDragFormat, autoConvert: false) ||
            ItemAt(e.OriginalSource as DependencyObject) is not { } target ||
            !ReferenceEquals(source.Tab, target.Tab) || !ReferenceEquals(Active, source.Tab))
            return;
        ActivatePage(source, scroll: false);
        EditPage((project, id) => PageEdits.Move(project, id, target.Number - 1));
        e.Handled = true;
    }

    private PageTile? ItemAt(DependencyObject? source) =>
        source is not null && ItemsControl.ContainerFromElement(Thumbnails, source) is ListBoxItem item ? item.DataContext as PageTile : null;

    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed)
                yield return typed;
            foreach (var descendant in Children<T>(child))
                yield return descendant;
        }
    }
}
