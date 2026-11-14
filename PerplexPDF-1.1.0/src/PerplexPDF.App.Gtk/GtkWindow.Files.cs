using PerplexPDF.Application;
using PerplexPDF.Domain;
using PerplexPDF.Infrastructure.Store;
using static PerplexPDF.Desktop.Gtk;
using static PerplexPDF.Desktop.Graphics;
using static PerplexPDF.Desktop.Program;

namespace PerplexPDF.Desktop;

internal sealed partial class GtkWindow
{
    private async Task OpenFiles(IEnumerable<string> paths, bool recovered = false)
    {
        foreach (var path in paths)
        {
            if (_tabs.Count >= MaxTabs)
                throw new AppFailure("TabLimit");
            var document = await OpenWithPassword(path, recovered);
            if (document is not null)
                AddTab(document, recovered ? "Recovered project" : Path.GetFileName(path), recovered ? path : null);
        }
        Status(L("Ready"));
    }

    private async Task<OpenDocument?> OpenWithPassword(string path, bool recovered)
    {
        string? password = null;
        OpenDocument? document = null;
        while (true)
        {
            AppFailure? passwordFailure = null;
            var completed = await Run(L("Opening"), async ct =>
            {
                try { document = await Task.Run(() => Workspace.OpenAsync(path, password, recovered, ct), ct); }
                catch (AppFailure failure) when (failure.Code == "PdfPassword")
                { passwordFailure = failure; }
            });
            if (!completed)
                return null;
            if (passwordFailure is null)
                return document;
            password = _dialogs.Input("PDF password", $"{L(passwordFailure.Code)}\n{Path.GetFileName(path)}", secret: true);
            if (password is null)
                return null;
        }
    }

    private DocumentTab AddTab(OpenDocument document, string name, string? recovered = null)
    {
        if (_tabs.Count >= MaxTabs)
        {
            document.Dispose();
            throw new AppFailure("TabLimit");
        }
        var tab = new DocumentTab(this, document, name) { RecoveredFrom = recovered };
        _tabs.Add(tab);
        var index = gtk_notebook_append_page(_notebook, tab.Root, tab.TabLabel);
        gtk_widget_show_all(tab.Root);
        gtk_widget_show_all(tab.TabLabel);
        gtk_notebook_set_current_page(_notebook, index);
        UpdateActive();
        return tab;
    }

    private string? Target(string title, string name, string ext) =>
        _dialogs.Files(title, [ext], saveName: name + ext).FirstOrDefault();

    private async Task<bool> SaveProject(DocumentTab tab, bool choose)
    {
        var path = !choose ? tab.Document.ProjectPath : null;
        path ??= Target("Save PerplexPDF project", tab.BaseName, ".ppdf");
        if (path is null)
            return false;
        if (tab.Document.Encrypted && tab.Document.ProjectPath is null &&
            !_dialogs.Ask("Encrypted PDF", L("EncryptedProject")))
            return false;
        var snapshot = tab.Project;
        return await Run("Saving project…", async ct =>
        {
            await tab.DraftGate.WaitAsync(ct);
            try
            {
                await Task.Run(() => Workspace.SaveProjectAsync(tab.Document, snapshot, path, ct), ct);
                tab.Document.ProjectPath = path;
                tab.Document.History.MarkSaved(snapshot);
                tab.Name = Path.GetFileName(path);
                DeleteDraft(tab);
                Status(L("ProjectSaved"));
            }
            finally { tab.DraftGate.Release(); }
        });
    }

    private bool ConfirmExport(DocumentTab tab)
    {
        if (tab.Document.Pdf.Metadata.Signed && !_dialogs.Ask("Signed PDF", L("SignatureWarning")))
            return false;
        return !tab.Document.Encrypted || _dialogs.Ask("Encrypted PDF", L("EncryptionWarning"));
    }

    private async Task ExportPdf(bool extract)
    {
        var tab = Active!;
        var project = tab.Project;
        if (extract)
        {
            var range = _dialogs.Input("Extract pages", "Enter pages or ranges in output order.", $"{tab.Index + 1}");
            if (range is null) return;
            project = PdfWorkspace.SelectPages(project, PageRanges.Parse(range, project.Pages.Length));
        }
        if (!ConfirmExport(tab)) return;
        var target = Target(extract ? "Extract pages" : "Save PDF As", tab.BaseName + (extract ? "-pages" : "-export"), ".pdf");
        if (target is null) return;
        if (await Run("Exporting PDF…", ct => Task.Run(() => Workspace.ExportAsync(tab.Document, project, target, ct), ct)))
            Status(L("PdfSaved"));
    }

    private async Task Convert(DocumentExportFormat format)
    {
        var tab = Active!;
        var word = format == DocumentExportFormat.Word;
        if (_dialogs.Conversion(word) is not { } options)
            return;
        if (tab.Document.Encrypted && !_dialogs.Ask("Encrypted PDF", L("ConversionEncryptionWarning")))
            return;
        if (tab.Document.Pdf.Metadata.Signed && !_dialogs.Ask("Signed PDF", L("ConversionSignatureWarning")))
            return;
        var target = Target(L(word ? "ExportWord" : "ExportEpub"), tab.BaseName, word ? ".docx" : ".epub");
        if (target is null) return;

        var project = tab.Project;
        var missing = 0;
        var progress = new Progress<WorkProgress>(progress =>
        {
            if (Busy) Status(Format("ConversionProgress", progress.Completed, progress.Total));
        });
        if (await Run("Exporting document…", ct => Task.Run(async () =>
        {
            if (options.Word is { } editable)
                missing = await Workspace.ExportWordAsync(tab.Document, project, target, tab.BaseName, editable, progress, ct);
            else
                await Workspace.ExportDocumentAsync(tab.Document, project, format, target, tab.BaseName, options.Images, progress, ct);
        }, ct)))
            Status(missing > 0 ? Format("WordPagesWithoutText", missing) : L(word ? "WordSaved" : "EpubSaved"));
    }

    private async Task ExportPng()
    {
        var tab = Active!;
        var target = Target("Export page as PNG", $"{tab.BaseName}-page-{tab.Index + 1}", ".png");
        if (target is null) return;
        if (await Run("Rendering PNG…", ct => Task.Run(() =>
            Workspace.ExportPngAsync(tab.Document, tab.Project, tab.Page, 144, target, ct), ct)))
            Status(L("PngSaved"));
    }

    private async Task Split()
    {
        var tab = Active!;
        var range = _dialogs.Input("Split PDF", "Separate groups of pages with semicolons.", tab.Project.Pages.Length == 1 ? "1" : $"1;2-{tab.Project.Pages.Length}");
        if (range is null) return;
        var groups = range.Split(';').Select(part => PageRanges.Parse(part, tab.Project.Pages.Length)).ToArray();
        if (groups.Length > 100)
            throw new AppFailure("InvalidRange");
        if (!ConfirmExport(tab)) return;
        var parent = _dialogs.Files("Select the destination folder", [], folder: true).FirstOrDefault();
        if (parent is null) return;
        var folder = Path.Combine(parent, tab.BaseName + "-split");
        if (Directory.Exists(folder) || File.Exists(folder))
            throw new AppFailure("SplitExists");
        if (await Run("Splitting PDF…", ct => Task.Run(() => Workspace.SplitAsync(tab.Document, tab.Project, groups, folder, ct), ct)))
            Status(Format("SplitSaved", groups.Length));
    }

    private string[]? OrderFiles(string[] files, string title)
    {
        if (files.Length <= 1) return files;
        var list = string.Join('\n', files.Select((path, index) => $"{index + 1}. {Path.GetFileName(path)}"));
        var order = _dialogs.Input(title, "Enter file numbers in output order.\n\n" + list, $"1-{files.Length}");
        if (order is null) return null;
        var indices = PageRanges.Parse(order, files.Length);
        if (indices.Length != files.Length || indices.Distinct().Count() != files.Length)
            throw new AppFailure("InvalidRange");
        return indices.Select(index => files[index]).ToArray();
    }

    private async Task Merge()
    {
        var files = _dialogs.Files("Select PDFs to merge", [".pdf"], multiple: true);
        if (files.Length == 0) return;
        if (files.Length is < 2 or > 64)
            throw new AppFailure("MergeCount");
        if (_tabs.Count >= MaxTabs)
            throw new AppFailure("TabLimit");
        if (OrderFiles(files, "Merge order") is not { } order) return;
        var inputs = new List<OpenDocument>();
        try
        {
            foreach (var path in order)
            {
                if (await OpenWithPassword(path, false) is not { } input) return;
                inputs.Add(input);
            }
            if (inputs.Any(input => input.Pdf.Metadata.Signed) && !_dialogs.Ask("Signed PDF", L("SignatureWarning")))
                return;
            if (inputs.Any(input => input.Encrypted) && !_dialogs.Ask("Encrypted PDF", L("EncryptionWarning")))
                return;
            await Run("Merging PDFs…", async ct =>
            {
                var document = await Task.Run(() => Workspace.MergeAsync(inputs, ct), ct);
                AddTab(document, "Merged document");
                Status("PDFs merged.");
            });
        }
        finally { foreach (var input in inputs) input.Dispose(); }
    }

    private async Task CreateFromImages()
    {
        var files = _dialogs.Files("Select JPG or PNG images", [".jpg", ".jpeg", ".png"], multiple: true);
        if (files.Length == 0) return;
        if (files.Length > DocumentProject.MaxPages || _tabs.Count >= MaxTabs)
            throw new AppFailure("SizeLimit");
        if (OrderFiles(files, "Image order") is not { } order) return;
        bool landscape;
        using (var dialog = new Dialogs.Dialog(_window, "Create PDF from images", "Create PDF"))
        {
            var orientation = Dialogs.Combo(["Portrait A4", "Landscape A4"]);
            Dialogs.Pack(dialog.Content, orientation);
            if (dialog.Run() != Accept) return;
            landscape = gtk_combo_box_get_active(orientation) == 1;
        }
        await Run("Reading images…", async ct =>
        {
            var result = await Task.Run(async () =>
            {
                var pages = new List<ImagePage>();
                long bytes = 0;
                foreach (var path in order)
                {
                    var image = await Workspace.ReadImageAsync(path, ct);
                    bytes += image.Length;
                    SafeFiles.CheckLength(bytes, DocumentProject.MaxSourceBytes);
                    pages.Add(new ImagePage(image, 0, landscape));
                }
                return await Workspace.CreateFromImagesAsync(pages, ct);
            }, ct);
            AddTab(result, "Images");
            Status("PDF created from images.");
        });
    }

    private async Task Ocr()
    {
        var tab = Active!;
        if (_dialogs.Ocr(tab.Project.Pages.Length, _services.Languages) is not { } options || !ConfirmExport(tab))
            return;
        var target = Target("Save searchable PDF", tab.BaseName + "-searchable", ".pdf");
        if (target is null) return;
        var progress = new Progress<WorkProgress>(progress =>
        {
            if (Busy) Status($"OCR {progress.Completed} / {progress.Total}");
        });
        if (await Run("Running offline OCR…", ct => Task.Run(() =>
            Workspace.OcrAsync(tab.Document, tab.Project, target, options.Pages, options.Language, options.Dpi, progress, ct), ct)))
            Status("Searchable PDF saved.");
    }

    private Task Print()
    {
        var tab = Active!;
        var project = tab.Project;
        return Run("Printing…", ct =>
        {
            var operation = gtk_print_operation_new();
            Exception? failure = null;
            try
            {
                gtk_print_operation_set_n_pages(operation, project.Pages.Length);
                Bind(operation, "draw-page", new SwitchSignal((_, printContext, index, _) =>
                {
                    try
                    {
                        var page = project.Pages[(int)index];
                        var transform = new PageTransform(tab.Document.Pdf.Pages[page.SourcePage], page.Rotation);
                        var size = transform.Display;
                        var dpi = Math.Clamp(72 * 2_900 / Math.Max(size.Width, size.Height), 6, 200);
                        var raster = tab.Document.Pdf.RenderAsync(page, dpi, ct).GetAwaiter().GetResult();
                        var scale = Math.Min(gtk_print_context_get_width(printContext) / size.Width,
                            gtk_print_context_get_height(printContext) / size.Height);
                        var context = gtk_print_context_get_cairo_context(printContext);
                        cairo_save(context);
                        try
                        {
                            cairo_scale(context, scale, scale);
                            Raster(context, raster, new(0, 0, size.Width, size.Height));
                            CairoOverlayEngine.Paint(context, transform, project.Overlays.Where(item => item.PageId == page.Id), project, ct);
                        }
                        finally { cairo_restore(context); }
                    }
                    catch (Exception error) { failure ??= error; gtk_print_operation_cancel(operation); }
                }));
                var result = gtk_print_operation_run(operation, 0, _window, out var error);
                if (error != 0) g_error_free(error);
                if (failure is not null) throw failure;
                if (result == 0) throw new AppFailure("PrintFailed");
                Status(result == 2 ? L("Canceled") : "Print job submitted.");
            }
            finally { g_object_unref(operation); }
            return Task.CompletedTask;
        });
    }

    internal async Task CloseTab(DocumentTab tab)
    {
        if (Busy || tab.Closed || !await ConfirmClose(tab))
            return;
        await RemoveTab(tab);
        UpdateActive();
        Status(L("Ready"));
    }

    private async Task CloseWindow()
    {
        if (_closing || Busy) return;
        _closing = true;
        try
        {
            foreach (var tab in _tabs.ToArray())
                if (!await ConfirmClose(tab)) return;
            if (_draftTimer != 0) g_source_remove(_draftTimer);
            foreach (var tab in _tabs.ToArray())
                await RemoveTab(tab);
            _draftSession.Dispose();
            gtk_widget_destroy(_window);
        }
        finally { _closing = false; }
    }

    private async Task<bool> ConfirmClose(DocumentTab tab)
    {
        if (!tab.Document.History.IsDirty) return true;
        return _dialogs.AskSave(tab.Name) switch
        {
            No => true,
            Accept => await SaveProject(tab, false),
            _ => false
        };
    }

    private async Task RemoveTab(DocumentTab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;
        tab.Stop();
        _tabs.RemoveAt(index);
        gtk_notebook_remove_page(_notebook, index);
        await tab.DraftGate.WaitAsync();
        try { DeleteDraft(tab); await Task.Run(tab.Document.Dispose); }
        finally { tab.DraftGate.Release(); }
    }

    private string DraftPath(DocumentTab tab) => _draftSession.File($"{tab.Id:N}.ppdf");

    private void DeleteDraft(DocumentTab tab)
    {
        SafeFiles.TryDelete(DraftPath(tab));
        if (tab.RecoveredFrom is { } path) LocalWorkspace.RemoveDraft(path);
        tab.RecoveredFrom = null;
        tab.DraftSnapshot = null;
    }

    private async Task SaveDrafts()
    {
        if (_savingDrafts || Busy || _closing)
            return;
        _savingDrafts = true;
        try
        {
            foreach (var tab in _tabs.ToArray())
            {
                if (tab.Closed || !tab.Document.History.IsDirty || ReferenceEquals(tab.Project, tab.DraftSnapshot))
                    continue;
                await tab.DraftGate.WaitAsync();
                try
                {
                    if (tab.Closed || _closing) continue;
                    var snapshot = tab.Project;
                    var path = DraftPath(tab);
                    await Task.Run(() => Workspace.SaveProjectAsync(tab.Document, snapshot, path, CancellationToken.None));
                    tab.DraftSnapshot = snapshot;
                    if (tab.RecoveredFrom is { } original)
                    {
                        LocalWorkspace.RemoveDraft(original);
                        tab.RecoveredFrom = null;
                    }
                }
                catch (Exception error) { Report(error, modal: false); }
                finally { tab.DraftGate.Release(); }
            }
        }
        finally { _savingDrafts = false; }
    }
}
