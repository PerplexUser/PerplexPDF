using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using PerplexPDF.Application;
using PerplexPDF.Domain;

namespace PerplexPDF.App;

public partial class MainWindow
{
    private void SetTool(OverlayKind? kind, string? hint = null)
    {
        if (Active is not { } tab)
            return;
        _textSelection?.Cancel();
        tab.EditExistingText = false;
        tab.Tool = kind;
        tab.SetTextSelection(null, null, "");
        _view.Status = Strings.Get(hint ?? (kind is null ? "SelectHint" : "DrawHint"));
    }

    private void BeginPdfTextEdit()
    {
        if (Active is not { } tab)
            return;
        if (tab.Document.Encrypted || tab.Document.Pdf.Metadata.Signed)
        {
            Report(new AppFailure("TextEditProtected"));
            return;
        }
        SetTool(null);
        tab.EditExistingText = true;
        tab.SelectOverlay(null);
        _view.Status = Strings.Get("TextEditClick");
    }

    internal async Task EditPdfTextAtAsync(PageTile tile, PdfPoint position)
    {
        if (IsBusy || !ReferenceEquals(Active, tile.Tab) || tile.Tab.Document.Pdf is not IAdvancedPdfEditEngine editor)
            return;
        var tab = tile.Tab;
        PdfTextObject? selected = null;
        if (!await RunAsync("TextEditReading", async ct =>
            selected = await editor.FindTextObjectAsync(tile.Reference, tile.Transform.ToSource(position), ct)))
            return;
        if (selected is null)
        {
            _view.Status = Strings.Get("TextEditMiss");
            return;
        }
        var replaceTab = tab.IsTextCopy && !tab.Document.History.CanUndo && !tab.Document.History.CanRedo;
        var replacement = _dialogs.Input(Strings.Get("EditPdfText"),
            Strings.Get(replaceTab ? "TextEditContinuePrompt" : "TextEditPrompt"), selected.Text);
        if (replacement is null || replacement == selected.Text)
            return;
        if (!replaceTab && _view.Tabs.Count >= MaxTabs)
            throw new AppFailure("TabLimit");
        var project = tab.Project;
        await RunAsync("TextEditSaving", async ct =>
        {
            await tab.DraftGate.WaitAsync(ct);
            try
            {
                var edited = await Workspace.EditTextCopyAsync(tab.Document, project, tile.Reference, selected, replacement, ct);
                var copy = new DocumentTab(edited, replaceTab ? tab.Name : BaseName(tab) + "-edited.pdf",
                    replaceTab ? tab.RecoveredFrom : null)
                {
                    Id = replaceTab ? tab.Id : Guid.NewGuid(), IsTextCopy = true,
                    EditExistingText = true, Continuous = tab.Continuous, Scale = tab.Scale
                };
                tab.EditExistingText = false;
                if (replaceTab)
                {
                    edited.ProjectPath = tab.Document.ProjectPath;
                    tab.Closed = true;
                    _view.Tabs[_view.Tabs.IndexOf(tab)] = copy;
                }
                else
                {
                    _view.Tabs.Add(copy);
                }
                _view.Active = copy;
                ActivatePage(copy.Pages[tile.Number - 1], scroll: true);
                _view.Status = Strings.Get("TextEditReady");
                if (replaceTab)
                    await Task.Run(tab.Document.Dispose);
            }
            finally { tab.DraftGate.Release(); }
        });
    }

    internal async Task AddOverlayAsync(PageTile tile, OverlayKind kind, PdfPoint start, PdfRect display)
    {
        if (IsBusy || !ReferenceEquals(Active, tile.Tab))
            return;
        var tab = tile.Tab;
        var useDefaultBounds = display.Width < 3 || display.Height < 3;
        if (useDefaultBounds)
        {
            var height = kind switch
            {
                OverlayKind.Text => 40,
                OverlayKind.Note => 80,
                OverlayKind.Highlight => 18,
                OverlayKind.Underline => 8,
                _ => 80
            };
            display = new PdfRect(start.X, start.Y, kind == OverlayKind.Note ? 160 : 130, height).Clamp(tile.Transform.Display);
        }
        var bounds = tile.Transform.ToSource(display).Clamp(tile.Transform.Source);
        var color = kind switch
        {
            OverlayKind.Highlight => 0x70ffdf32U,
            OverlayKind.Underline or OverlayKind.Rectangle or OverlayKind.Ellipse => 0xff164dbdU,
            _ => 0xff172638U
        };
        var overlay = new OverlayObject(Guid.NewGuid(), tile.Reference.Id, kind, bounds, color,
            FontSize: kind == OverlayKind.Note ? 12 : 16, CreationRotation: tile.Reference.Rotation);
        var resources = tab.Project.Resources;
        if (kind is OverlayKind.Text or OverlayKind.Note)
        {
            if (_dialogs.AddText(overlay) is not { } styled)
                return;
            if (useDefaultBounds && styled.FontSize > overlay.FontSize)
            {
                var scale = styled.FontSize / overlay.FontSize;
                var resized = new PdfRect(start.X, start.Y, display.Width * scale, display.Height * scale).Clamp(tile.Transform.Display);
                styled = styled with { Bounds = tile.Transform.ToSource(resized).Clamp(tile.Transform.Source) };
            }
            overlay = styled;
        }
        else if (kind == OverlayKind.Image)
        {
            var picker = new OpenFileDialog { Filter = Strings.Get("ImageFilter"), CheckFileExists = true };
            if (picker.ShowDialog(this) != true)
                return;
            byte[]? bytes = null;
            if (!await RunAsync("ReadingImages", async ct => bytes = await Workspace.ReadImageAsync(picker.FileName, ct)))
                return;
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes!));
            if (!resources.ContainsKey(hash))
                resources = resources.Add(hash, ImmutableArray.CreateRange(bytes!));
            overlay = overlay with { ResourceHash = hash };
        }
        tab.Execute(kind is OverlayKind.Highlight or OverlayKind.Underline or OverlayKind.Note ? "F-009" : "F-008",
            tab.Project with { Overlays = tab.Project.Overlays.Add(overlay), Resources = resources });
        tab.Tool = null;
        SelectOverlay(tab, overlay.Id);
        _view.Status = Strings.Get("OverlayAdded");
    }

    internal void SelectOverlay(DocumentTab tab, Guid? overlayId)
    {
        tab.SelectOverlay(overlayId);
        if (overlayId is not null)
            InspectorTabs.SelectedIndex = 0;
    }

    internal void MoveOverlay(PageTile tile, OverlayObject overlay, PdfRect bounds)
    {
        if (!IsBusy && ReferenceEquals(Active, tile.Tab))
            ReplaceOverlay(tile.Tab, overlay with { Bounds = bounds });
    }

    private void ApplyOverlay()
    {
        if (Active is not { Inspector: { } input } tab)
            return;
        try
        {
            if (!ParseNumber(input.X, out var x) || !ParseNumber(input.Y, out var y) ||
                !ParseNumber(input.Width, out var width) || !ParseNumber(input.Height, out var height) ||
                x < 0 || y < 0 || width <= 0 || height <= 0 ||
                input.ReadAppearance() is not { } overlay)
                throw new AppFailure("InvalidOverlay");
            var page = tab.Project.Pages.First(page => page.Id == overlay.PageId);
            var geometry = tab.Document.Pdf.Pages[page.SourcePage];
            if (x + width > geometry.Width + .01 || y + height > geometry.Height + .01)
                throw new AppFailure("InvalidOverlay");
            ReplaceOverlay(tab, overlay with { Bounds = new(x, y, width, height) });
        }
        catch (Exception error) { Report(error); }
    }

    private static bool ParseNumber(string text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && double.IsFinite(number);

    private void PickOverlayColor(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: OverlayInspector input } control &&
            Window.GetWindow(control) is { } owner && WindowsColorPicker.Choose(owner, input.CurrentColor) is { } color)
            input.Color = $"#{color:X8}";
    }

    private static void ReplaceOverlay(DocumentTab tab, OverlayObject overlay)
    {
        var existing = tab.Project.Overlays.FirstOrDefault(item => item.Id == overlay.Id);
        if (existing is null || existing == overlay)
            return;
        tab.Execute("F-008", tab.Project with { Overlays = tab.Project.Overlays.Replace(existing, overlay) });
    }

    private void DeleteOverlay()
    {
        if (Active is { Selected: { } overlay } tab)
        {
            tab.Execute("F-009", tab.Project with { Overlays = tab.Project.Overlays.Remove(overlay) });
            tab.SelectOverlay(null);
        }
    }

    internal async Task SelectTextAsync(PageTile tile, PdfRect bounds)
    {
        if (IsBusy || !ReferenceEquals(Active, tile.Tab))
            return;
        _textSelection?.Cancel();
        using var selection = new CancellationTokenSource();
        _textSelection = selection;
        tile.Tab.SetTextSelection(tile.Reference.Id, bounds, "");
        try
        {
            var text = await tile.Tab.Document.Pdf.ReadTextAsync(tile.Reference, bounds, selection.Token);
            if (!selection.IsCancellationRequested)
            {
                tile.Tab.SetTextSelection(tile.Reference.Id, bounds, text);
                _view.Status = Strings.Get(text.Length == 0 ? "NoSelectedText" : "CopyHint");
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (selection.IsCancellationRequested || tile.Tab.Closed) { }
        finally { if (ReferenceEquals(_textSelection, selection)) _textSelection = null; }
    }

    private void CopyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control &&
            Keyboard.FocusedElement is not TextBoxBase && !string.IsNullOrEmpty(Active?.SelectedText))
        {
            Copy();
            e.Handled = true;
        }
    }

    private void Copy()
    {
        if (Active?.SelectedText is { Length: > 0 } text)
        {
            try { Clipboard.SetText(text); _view.Status = Strings.Get("Copied"); }
            catch (System.Runtime.InteropServices.ExternalException error) { Report(new AppFailure("ClipboardFailed", error)); }
        }
    }

    private void ShowSearch()
    {
        SearchBar.Visibility = Visibility.Visible;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void CloseSearch()
    {
        SearchBar.Visibility = Visibility.Collapsed;
        Active?.SetSearch([], -1);
        Viewer.Focus();
    }

    private async void SearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SearchAsync();
        }
    }

    private async Task SearchAsync()
    {
        if (Active is not { } tab || string.IsNullOrWhiteSpace(tab.SearchQuery))
            return;
        var term = tab.SearchQuery.Trim();
        IReadOnlyList<TextHit> hits = [];
        if (await RunAsync("Searching", async ct => hits = await tab.Document.Pdf.SearchAsync(tab.Project.Pages, term, ct)))
        {
            tab.SetSearch(hits, hits.Count == 0 ? -1 : 0);
            if (ReferenceEquals(tab, Active))
            {
                NavigateHit(tab);
                _view.Status = Strings.Get(hits.Count == 2000 ? "SearchCap" : hits.Count == 0 ? "NoMatches" : "SearchComplete");
                SearchBox.Focus();
            }
        }
    }

    private void StepMatch(int delta)
    {
        if (Active is not { Matches.Count: > 0 } tab)
            return;
        tab.SetSearch(tab.Matches, (tab.MatchIndex + delta + tab.Matches.Count) % tab.Matches.Count);
        NavigateHit(tab);
    }

    private void NavigateHit(DocumentTab tab)
    {
        if (tab.MatchIndex < 0)
            return;
        var page = tab.Pages.FirstOrDefault(item => item.Reference.Id == tab.Matches[tab.MatchIndex].PageId);
        if (page is not null)
            ActivatePage(page, scroll: true);
    }
}
