using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using PerplexPDF.Infrastructure.Store;

namespace PerplexPDF.App;

public partial class MainWindow
{
    private Task NewPdfAsync() => RunAsync("CreatingPdf", async ct =>
    {
        var document = await Workspace.CreateBlankA4Async(ct);
        AddTab(document, Strings.Get("UntitledPdf"));
        _fitWidth = false;
        _fitPage = true;
        Fit(width: false);
        _view.Status = Strings.Get("NewDocumentHint");
    });

    private async Task OpenDialogAsync(bool project)
    {
        var picker = new OpenFileDialog
        {
            Filter = Strings.Get(project ? "ProjectFilter" : "PdfFilter"),
            CheckFileExists = true, Multiselect = true
        };
        if (picker.ShowDialog(this) == true)
            await OpenBatchAsync(picker.FileNames);
    }

    private void FilesDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        e.Effects = IsBusy ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void FilesDrop(object sender, DragEventArgs e)
    {
        if (IsBusy || e.Data.GetData(DataFormats.FileDrop) is not string[] paths)
            return;
        e.Handled = true;
        await OpenBatchAsync(paths);
    }

    private Task<bool> OpenBatchAsync(IEnumerable<string> paths, bool recovered = false) => RunAsync("Opening", async ct =>
    {
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (_view.Tabs.Count >= MaxTabs)
                throw new AppFailure("TabLimit");
            try
            {
                var document = await OpenWithPasswordAsync(path, recovered, ct);
                if (document is not null)
                {
                    var name = recovered ? Strings.Get("RecoveredDocument") : Path.GetFileName(path);
                    AddTab(document, name, recovered ? path : null);
                    if (recovered)
                        _view.Status = Strings.Get("RecoveryOpened");
                }
            }
            catch (Exception error) when (error is not OperationCanceledException) { Report(error); }
        }
    });

    private async Task<OpenDocument?> OpenWithPasswordAsync(string path, bool recovered, CancellationToken ct)
    {
        string? password = null;
        while (true)
        {
            try { return await Task.Run(() => Workspace.OpenAsync(path, password, recovered, ct), ct); }
            catch (AppFailure failure) when (failure.Code == "PdfPassword")
            {
                if (password is not null)
                    _view.Status = Strings.Get("IncorrectPassword");
                password = _dialogs.Password(recovered ? Strings.Get("RecoveredDocument") : Path.GetFileName(path));
                if (password is null)
                    return null;
            }
        }
    }

    private string? ChooseTarget(string extension, string title, string name)
    {
        var picker = new SaveFileDialog
        {
            DefaultExt = extension, AddExtension = true, OverwritePrompt = true,
            FileName = name, Title = Strings.Get(title), Filter = Strings.Get(extension switch
            {
                ".ppdf" => "ProjectFilter",
                ".png" => "PngFilter",
                ".docx" => "WordFilter",
                ".epub" => "EpubFilter",
                _ => "PdfFilter"
            })
        };
        if (picker.ShowDialog(this) != true)
            return null;
        if (!string.Equals(Path.GetExtension(picker.FileName), extension, StringComparison.OrdinalIgnoreCase))
            throw new AppFailure("OutputExtension");
        return picker.FileName;
    }

    private async Task<bool> SaveProjectAsync(DocumentTab tab, bool choose, bool allowClosing = false)
    {
        var target = choose || tab.Document.ProjectPath is null
            ? ChooseTarget(".ppdf", "SaveProjectAs", BaseName(tab)) : tab.Document.ProjectPath;
        if (target is null)
            return false;
        if (tab.Document.Encrypted && tab.Document.ProjectPath is null && !Ask("EncryptedProject"))
            return false;
        var snapshot = tab.Project;
        var saved = await RunAsync("SavingProject", async ct =>
        {
            await tab.DraftGate.WaitAsync(ct);
            try
            {
                await Task.Run(() => Workspace.SaveProjectAsync(tab.Document, snapshot, target, ct), ct);
                tab.Document.ProjectPath = target;
                tab.Document.History.MarkSaved(snapshot);
                tab.Name = Path.GetFileName(target);
                tab.Refresh();
                DeleteDraft(tab);
                _view.Status = Strings.Get("ProjectSaved");
            }
            finally { tab.DraftGate.Release(); }
        }, allowClosing);
        return saved;
    }

    private async Task ExportPdfAsync(bool extract)
    {
        if (Active is not { } tab)
            return;
        var project = tab.Project;
        if (extract)
        {
            var range = _dialogs.Input(Strings.Get("Extract"), Strings.Get("RangeHint"), tab.Current!.Number.ToString());
            if (range is null)
                return;
            project = PdfWorkspace.SelectPages(project, ParseRange(range, tab.Pages.Count));
        }
        if (!ConfirmExport(tab))
            return;
        var suffix = extract ? "-pages" : tab.Document.Original is null && tab.Document.ProjectPath is null ? "" : "-export";
        var target = ChooseTarget(".pdf", extract ? "Extract" : "SavePdfAs", BaseName(tab) + suffix);
        if (target is not null &&
            await RunAsync("Exporting", ct => Task.Run(() => Workspace.ExportAsync(tab.Document, project, target, ct), ct)))
            _view.Status = Strings.Get("PdfSaved");
    }

    private async Task ConvertDocumentAsync(DocumentExportFormat format)
    {
        if (Active is not { } tab)
            return;
        var word = format == DocumentExportFormat.Word;
        var title = word ? "ExportWord" : "ExportEpub";
        if (_dialogs.ExportOptions(Strings.Get(title), word) is not { } options)
            return;
        if (tab.Document.Encrypted && !Ask("ConversionEncryptionWarning"))
            return;
        if (tab.Document.Pdf.Metadata.Signed && !Ask("ConversionSignatureWarning"))
            return;
        var name = BaseName(tab);
        var target = ChooseTarget(word ? ".docx" : ".epub", title, name);
        if (target is null)
            return;

        var project = tab.Project;
        var progress = new Progress<WorkProgress>(item =>
        {
            if (_operation is { IsCancellationRequested: false })
                _view.Progress = Strings.Format("ConversionProgress", item.Completed, item.Total);
        });
        var pagesWithoutText = 0;
        if (await RunAsync("Exporting", ct => Task.Run(async () =>
        {
            if (options.Word is { } editable)
                pagesWithoutText = await Workspace.ExportWordAsync(tab.Document, project, target, name, editable, progress, ct);
            else
                await Workspace.ExportDocumentAsync(tab.Document, project, format, target, name, options.PageImages, progress, ct);
        }, ct)))
            _view.Status = pagesWithoutText > 0 ? Strings.Format("WordPagesWithoutText", pagesWithoutText)
                : Strings.Get(word ? "WordSaved" : "EpubSaved");
    }

    private bool ConfirmExport(DocumentTab tab)
    {
        if (tab.Document.Pdf.Metadata.Signed && !Ask("SignatureWarning"))
            return false;
        return !tab.Document.Encrypted || Ask("EncryptionWarning");
    }

    private async Task SplitAsync()
    {
        if (Active is not { } tab)
            return;
        var input = _dialogs.Input(Strings.Get("Split"), Strings.Get("SplitHint"), tab.Pages.Count == 1 ? "1" : $"1;2-{tab.Pages.Count}");
        if (input is null)
            return;
        var groups = input.Split(';').Select(range => ParseRange(range, tab.Pages.Count)).ToArray();
        if (groups.Length > 100)
            throw new AppFailure("InvalidRange");
        if (!ConfirmExport(tab))
            return;
        var picker = new OpenFolderDialog { Title = Strings.Get("SplitFolder") };
        if (picker.ShowDialog(this) != true)
            return;
        var folder = Path.Combine(picker.FolderName, BaseName(tab) + "-split");
        if (Directory.Exists(folder) || File.Exists(folder))
            throw new AppFailure("SplitExists");
        var project = tab.Project;
        if (await RunAsync("Exporting", ct => Task.Run(() => Workspace.SplitAsync(tab.Document, project, groups, folder, ct), ct)))
            _view.Status = Strings.Format("SplitSaved", groups.Length);
    }

    private async Task MergeAsync()
    {
        var picker = new OpenFileDialog { Filter = Strings.Get("PdfFilter"), CheckFileExists = true, Multiselect = true };
        if (picker.ShowDialog(this) != true)
            return;
        if (picker.FileNames.Length is < 2 or > 64)
            throw new AppFailure("MergeCount");
        var order = _dialogs.Order(Strings.Get("Merge"), Strings.Get("MergeHint"), picker.FileNames, images: false);
        if (order is null)
            return;
        await RunAsync("Merging", async ct =>
        {
            var inputs = new List<OpenDocument>();
            try
            {
                foreach (var item in order)
                {
                    var input = await OpenWithPasswordAsync(item.Path, false, ct);
                    if (input is null)
                        return;
                    inputs.Add(input);
                    if (input.Pdf.Metadata.Signed && !Ask("SignatureWarning") ||
                        input.Encrypted && !Ask("EncryptionWarning"))
                        return;
                }
                var merged = await Task.Run(() => Workspace.MergeAsync(inputs, ct), ct);
                AddTab(merged, Strings.Get("MergedDocument"));
                _view.Status = Strings.Get("NewDocumentHint");
            }
            finally { foreach (var input in inputs) input.Dispose(); }
        });
    }

    private async Task ImagesAsync()
    {
        var picker = new OpenFileDialog { Filter = Strings.Get("ImageFilter"), CheckFileExists = true, Multiselect = true };
        if (picker.ShowDialog(this) != true)
            return;
        if (picker.FileNames.Length > 200)
            throw new AppFailure("SizeLimit");
        var order = _dialogs.Order(Strings.Get("CreateImages"), Strings.Get("ImageOrderHint"), picker.FileNames, images: true);
        if (order is null)
            return;
        await RunAsync("ReadingImages", async ct =>
        {
            var images = new List<ImagePage>();
            long total = 0;
            foreach (var item in order)
            {
                var image = await Workspace.ReadImageAsync(item.Path, ct);
                total += image.Length;
                if (total > 128 * 1024 * 1024)
                    throw new AppFailure("SizeLimit");
                images.Add(new(image, item.Rotation, item.Landscape));
            }
            var document = await Task.Run(() => Workspace.CreateFromImagesAsync(images, ct), ct);
            AddTab(document, Strings.Get("ImagesDocument"));
            _view.Status = Strings.Get("NewDocumentHint");
        });
    }

    private async Task ExportPngAsync()
    {
        if (Active?.Current is not { } page)
            return;
        var target = ChooseTarget(".png", "ExportPng", $"{BaseName(page.Tab)}-page-{page.Number}");
        if (target is null)
            return;
        var snapshot = page.Tab.Project;
        if (await RunAsync("Exporting", ct => Task.Run(() =>
            Workspace.ExportPngAsync(page.Tab.Document, snapshot, page.Reference, 144, target, ct), ct)))
            _view.Status = Strings.Get("PngSaved");
    }

    private async Task OcrAsync()
    {
        if (Active is not { } tab)
            return;
        var options = _dialogs.Ocr(tab.Pages.Count);
        if (options is not { } settings)
            return;
        var pages = ParseRange(settings.Range, tab.Pages.Count);
        if (!ConfirmExport(tab))
            return;
        var target = ChooseTarget(".pdf", "OcrTitle", BaseName(tab) + "-searchable");
        if (target is null)
            return;
        var snapshot = tab.Project;
        var progress = new Progress<WorkProgress>(item =>
        {
            if (_operation is { IsCancellationRequested: false })
                _view.Progress = Strings.Format("OcrProgress", item.Completed, item.Total);
        });
        if (await RunAsync("OcrStarting", ct => Task.Run(() => Workspace.OcrAsync(tab.Document, snapshot, target, pages,
            settings.Language, settings.Dpi, progress, ct), ct)))
            _view.Status = Strings.Get("OcrSaved");
    }

    private static int[] ParseRange(string range, int pageCount)
    {
        try { return PageRanges.Parse(range, pageCount); }
        catch (FormatException error) { throw new AppFailure("InvalidRange", error); }
    }

    private static string BaseName(DocumentTab tab) => Path.GetFileNameWithoutExtension(tab.Name);

    private async void CloseTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DocumentTab tab } && !IsBusy)
        {
            try { await CloseTabAsync(tab); }
            catch (Exception error) { Report(error); }
        }
    }

    private async Task CloseTabAsync(DocumentTab tab)
    {
        if (tab.Closed || !await AllowDiscardAsync(tab))
            return;
        tab.Closed = true;
        _textSelection?.Cancel();
        var index = _view.Tabs.IndexOf(tab);
        var active = ReferenceEquals(tab, Active);
        _view.Tabs.Remove(tab);
        if (active)
            _view.Active = _view.Tabs.Count == 0 ? null : _view.Tabs[Math.Clamp(index, 0, _view.Tabs.Count - 1)];
        await CloseResourcesAsync(tab);
    }

    private async Task<bool> AllowDiscardAsync(DocumentTab tab)
    {
        if (!tab.Dirty)
            return true;
        var choice = MessageBox.Show(this, Strings.Format("UnsavedPrompt", tab.Name), "PerplexPDF",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        return choice switch
        {
            MessageBoxResult.Yes => await SaveProjectAsync(tab, choose: false, allowClosing: true),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private async Task CloseResourcesAsync(DocumentTab tab)
    {
        await tab.DraftGate.WaitAsync();
        try
        {
            DeleteDraft(tab);
            await Task.Run(tab.Document.Dispose);
        }
        finally { tab.DraftGate.Release(); }
    }

    private async void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;
        e.Cancel = true;
        if (_closing)
            return;
        if (_view.Busy)
        {
            Cancel();
            return;
        }
        _closing = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var tabs = _view.Tabs.ToArray();
            foreach (var tab in tabs)
                if (!await AllowDiscardAsync(tab))
                    return;
            _draftTimer.Stop();
            _textSelection?.Cancel();
            foreach (var tab in tabs)
                tab.Closed = true;
            _view.Tabs.Clear();
            _view.Active = null;
            foreach (var tab in tabs)
                await CloseResourcesAsync(tab);
            _draftSession.Dispose();
            _allowClose = true;
            // Closing may be entered synchronously when there are no tabs.
            await Dispatcher.InvokeAsync(Close);
        }
        catch (Exception error) { Report(error); }
        finally { _closing = false; CommandManager.InvalidateRequerySuggested(); }
    }

    private string DraftPath(DocumentTab tab) => _draftSession.File(tab.Id.ToString("N") + ".ppdf");

    private void DeleteDraft(DocumentTab tab)
    {
        SafeFiles.TryDelete(DraftPath(tab));
        tab.DraftSnapshot = null;
        DeleteRecovered(tab);
    }

    private static void DeleteRecovered(DocumentTab tab)
    {
        if (tab.RecoveredFrom is { } recovered)
            LocalWorkspace.RemoveDraft(recovered);
        tab.RecoveredFrom = null;
    }

    private async void SaveDrafts(object? sender, EventArgs e)
    {
        if (_savingDrafts || IsBusy)
            return;
        _savingDrafts = true;
        try
        {
            foreach (var tab in _view.Tabs.ToArray())
            {
                if (tab.Closed || !tab.Dirty || ReferenceEquals(tab.DraftSnapshot, tab.Project) || !tab.DraftGate.Wait(0))
                    continue;
                try
                {
                    var snapshot = tab.Project;
                    var path = DraftPath(tab);
                    await Task.Run(() => Workspace.SaveProjectAsync(tab.Document, snapshot, path, CancellationToken.None));
                    tab.DraftSnapshot = snapshot;
                    DeleteRecovered(tab);
                }
                catch (Exception error) { Report(new AppFailure("DraftFailed", error), dialog: false); }
                finally { tab.DraftGate.Release(); }
            }
        }
        finally { _savingDrafts = false; }
    }
}
