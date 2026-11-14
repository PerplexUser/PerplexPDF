using System.Diagnostics;
using System.Runtime.InteropServices;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using PerplexPDF.Infrastructure.Store;
using static PerplexPDF.Desktop.Dialogs;
using static PerplexPDF.Desktop.Gtk;
using static PerplexPDF.Desktop.Program;

namespace PerplexPDF.Desktop;

internal sealed partial class GtkWindow
{
    private const int MaxTabs = 16;
    private readonly GtkServices _services;
    private readonly string[] _paths;
    private readonly nint _window, _notebook, _welcome, _status, _cancel;
    private readonly Dialogs _dialogs;
    private readonly LocalDiagnostics _diagnostics;
    private readonly LocalWorkspace.LocalDirectory _draftSession;
    private readonly List<DocumentTab> _tabs = [];
    private readonly Dictionary<string, Command> _commands = [];
    private readonly List<(nint Widget, Command Command)> _controls = [];
    private readonly Queue<string> _desktopOpen = new();
    private readonly uint _primaryModifier;
    private CancellationTokenSource? _operation;
    private bool _closing, _savingDrafts, _started, _openingFromDesktop;
    private uint _draftTimer;
    private DocumentTab? Active
    {
        get
        {
            var index = gtk_notebook_get_current_page(_notebook);
            return index >= 0 && index < _tabs.Count ? _tabs[index] : null;
        }
    }

    private PdfWorkspace Workspace => _services.Workspace;
    private bool Busy => _operation is not null;
    private sealed record Command(Func<Task> Action, Func<bool> Enabled);

    internal GtkWindow(GtkServices services, string[] paths, nint application = 0)
    {
        _services = services;
        _paths = paths;
        _diagnostics = new LocalDiagnostics(services.Local.Root);
        _draftSession = services.Local.CreateRecoverySession();
        _window = gtk_window_new(0);
        if (application != 0)
            gtk_window_set_application(_window, application);
        gtk_accelerator_parse("<Primary>o", out _, out _primaryModifier);
        gtk_window_set_title(_window, "PerplexPDF");
        if (OperatingSystem.IsLinux())
            gtk_window_set_wmclass(_window, "perplexpdf", "PerplexPDF");
        gtk_window_set_default_size(_window, 1260, 820);
        gtk_widget_set_size_request(_window, 660, 430);
        if (gtk_window_set_icon_from_file(_window, BrandPath, out var iconError) == 0 && iconError != 0)
            g_error_free(iconError);
        _dialogs = new Dialogs(_window);

        var root = gtk_box_new(Vertical, 0);
        gtk_container_add(_window, root);
        RegisterCommands();
        Pack(root, CreateMenu());
        Pack(root, CreateToolbar());
        _notebook = gtk_notebook_new();
        gtk_notebook_set_scrollable(_notebook, 1);
        Bind(_notebook, "switch-page", new SwitchSignal((_, _, _, _) => Post(UpdateActive)));
        Pack(root, _notebook, true);
        var welcome = gtk_box_new(Vertical, 13);
        gtk_widget_set_halign(welcome, 3);
        gtk_widget_set_valign(welcome, 3);
        Pack(welcome, Brand(54));
        Pack(welcome, Label("PerplexPDF"));
        Pack(welcome, Label("Open or drop a PDF or a PerplexPDF project here."));
        Pack(welcome, CommandWidget("Open", "Open PDF…", false));
        Pack(root, welcome, true);
        _welcome = welcome;

        var footer = gtk_box_new(Horizontal, 8);
        gtk_container_set_border_width(footer, 5);
        _status = Label(L("Ready"));
        gtk_label_set_ellipsize(_status, 3);
        Pack(footer, _status, true);
        _cancel = CommandWidget("Cancel", L("Cancel"), false);
        Pack(footer, _cancel);
        Pack(footer, gtk_link_button_new_with_label(Website, "© by www.perplex.click"));
        Pack(root, footer);

        Bind(_window, "key-press-event", new EventSignal((_, key, _) => Shortcut(key) ? 1 : 0));
        Bind(_window, "delete-event", new EventSignal((_, _, _) =>
        {
            RequestClose();
            return 1;
        }));
        if (application == 0)
            Bind(_window, "destroy", new Signal((_, _) => gtk_main_quit()));
        EnableDrop();
    }

    internal void Show()
    {
        gtk_widget_show_all(_window);
        UpdateActive();
        Post(() => Launch(Start));
    }

    internal void Present() => gtk_window_present(_window);

    internal void RequestClose()
    {
        if (Busy) _operation?.Cancel();
        else Launch(CloseWindow);
    }

    internal void OpenFromDesktop(IEnumerable<string> paths)
    {
        foreach (var path in paths.Take(MaxTabs - _desktopOpen.Count))
            _desktopOpen.Enqueue(path);
        OpenQueuedFiles();
    }

    private void OpenQueuedFiles()
    {
        if (!_started || Busy || _closing || _openingFromDesktop || _desktopOpen.Count == 0)
            return;
        var paths = _desktopOpen.ToArray();
        _desktopOpen.Clear();
        _openingFromDesktop = true;
        Launch(async () =>
        {
            try { await OpenFiles(paths); }
            finally { _openingFromDesktop = false; Post(OpenQueuedFiles); }
        });
    }

    private nint Brand(int size)
    {
        var pixbuf = Graphics.gdk_pixbuf_new_from_file_at_scale(BrandPath, size, size, 1, out var error);
        if (error != 0) g_error_free(error);
        if (pixbuf == 0)
            return Label("PDF");
        try { return gtk_image_new_from_pixbuf(pixbuf); }
        finally { g_object_unref(pixbuf); }
    }

    private void RegisterCommands()
    {
        bool Doc() => Active is not null;
        void Add(string name, Func<Task> action, Func<bool>? enabled = null) =>
            _commands.Add(name, new(action, enabled ?? (() => true)));
        void Sync(string name, Action action, Func<bool>? enabled = null) =>
            Add(name, () => { action(); return Task.CompletedTask; }, enabled);

        Add("Open", () => OpenFiles(_dialogs.Files("Open PDF or project", [".pdf", ".ppdf"], multiple: true)));
        Add("SaveProject", () => SaveProject(Active!, false), Doc);
        Add("SaveProjectAs", () => SaveProject(Active!, true), Doc);
        Add("SavePdf", () => ExportPdf(false), () => Doc() && _services.HasQpdf);
        Add("Extract", () => ExportPdf(true), () => Doc() && _services.HasQpdf);
        Add("ExportWord", () => Convert(DocumentExportFormat.Word), Doc);
        Add("ExportEpub", () => Convert(DocumentExportFormat.Epub), Doc);
        Add("Png", ExportPng, Doc);
        Add("Print", Print, Doc);
        Add("Close", () => CloseTab(Active!), Doc);
        Add("Exit", CloseWindow);
        Sync("Undo", () => History(false), () => Active?.Document.History.CanUndo == true);
        Sync("Redo", () => History(true), () => Active?.Document.History.CanRedo == true);
        Sync("Find", () => Active?.FocusSearch(), Doc);
        Sync("Copy", CopyText, () => !string.IsNullOrEmpty(Active?.SelectedText));
        Sync("Previous", () => Active!.GoTo(Active.Index - 1), Doc);
        Sync("Next", () => Active!.GoTo(Active.Index + 1), Doc);
        Sync("ZoomIn", () => Active!.ZoomBy(1.25), Doc);
        Sync("ZoomOut", () => Active!.ZoomBy(.8), Doc);
        Sync("ActualSize", () => Active!.SetZoom(96.0 / 72), Doc);
        Sync("FitPage", () => Active!.Fit(false), Doc);
        Sync("FitWidth", () => Active!.Fit(true), Doc);
        Sync("Single", () => Active!.SetContinuous(false), Doc);
        Sync("Continuous", () => Active!.SetContinuous(true), Doc);
        Sync("Thumbnails", () => Active!.ShowNavigator(0), Doc);
        Sync("Bookmarks", () => Active!.ShowNavigator(1), Doc);
        Sync("RotateRight", () => PageChange(project => PageEdits.Rotate(project, Active!.Page.Id, 90)), Doc);
        Sync("RotateLeft", () => PageChange(project => PageEdits.Rotate(project, Active!.Page.Id, -90)), Doc);
        Sync("MoveUp", () => PageChange(project => PageEdits.Move(project, Active!.Page.Id, Active.Index - 1)), () => Active?.Index > 0);
        Sync("MoveDown", () => PageChange(project => PageEdits.Move(project, Active!.Page.Id, Active.Index + 1)),
            () => Active is { } tab && tab.Index + 1 < tab.Project.Pages.Length);
        Sync("DuplicatePage", () => PageChange(project => PageEdits.Duplicate(project, Active!.Page.Id)),
            () => Active is { } tab && tab.Project.Pages.Length < DocumentProject.MaxPages);
        Sync("DeletePage", DeletePage, () => Active?.Project.Pages.Length > 1);
        Add("Split", Split, () => Doc() && _services.HasQpdf);
        Add("Merge", Merge, () => _services.HasQpdf);
        Add("Images", CreateFromImages, () => _services.HasQpdf);
        Add("Ocr", Ocr, () => Doc() && _services.HasOcr);
        foreach (var kind in Enum.GetValues<OverlayKind>())
            Sync(kind.ToString(), () => Tool(kind), Doc);
        Sync("Signature", () => Tool(OverlayKind.Image, "SignatureImageHint"), Doc);
        Sync("Select", () => Tool(null), Doc);
        Sync("EditPdfText", BeginEditText, () => Doc() && _services.HasQpdf);
        Sync("Properties", EditOverlay, () => Active?.Selected is not null);
        Sync("DeleteOverlay", DeleteOverlay, () => Active?.Selected is not null);
        Sync("Comments", ShowComments, () => Active?.Project.Overlays.Any(overlay => overlay.Kind == OverlayKind.Note) == true);
        Sync("Cancel", CancelOperation);
        Sync("DocumentInfo", DocumentInfo, Doc);
        Sync("Help", () => _dialogs.Message("Quick guide",
            "Open a PDF and use Edit text to replace existing text. To annotate, choose a tool and draw a box on the page. Save Project keeps overlays editable. Export to Word defaults to editable text and separate graphics. OCR uses local English and German models."));
        Sync("Notices", () => ShowFile("Third-Party Notices", "THIRD_PARTY_NOTICES.txt"));
        Sync("License", () => ShowFile("MIT License", "LICENSE"));
        Sync("About", About);
    }

    private nint CreateMenu()
    {
        var row = gtk_box_new(Horizontal, 10);
        gtk_container_set_border_width(row, 7);
        Pack(row, Brand(24));
        Pack(row, Label("PerplexPDF"));
        var bar = gtk_menu_bar_new();
        Pack(row, bar, true);
        Add("_File", ("Open PDF or project…", "Open"), ("Save Project", "SaveProject"), ("Save Project As…", "SaveProjectAs"),
            ("Save PDF As…", "SavePdf"), ("Export to Word…", "ExportWord"), ("Export to EPUB…", "ExportEpub"),
            ("Export page as PNG…", "Png"), ("Print…", "Print"), ("Close tab", "Close"), ("Quit", "Exit"));
        Add("_Edit", ("Undo", "Undo"), ("Redo", "Redo"), ("Find text", "Find"), ("Copy selected text", "Copy"),
            ("Add Text", "Text"), ("Edit PDF Text…", "EditPdfText"), ("Add Image", "Image"));
        Add("_View", ("Fit page", "FitPage"), ("Fit width", "FitWidth"), ("Actual size", "ActualSize"),
            ("Single page", "Single"), ("Continuous", "Continuous"), ("Page thumbnails", "Thumbnails"), ("Bookmarks", "Bookmarks"));
        Add("_Pages", ("Rotate clockwise", "RotateRight"), ("Rotate counterclockwise", "RotateLeft"), ("Move page up", "MoveUp"),
            ("Move page down", "MoveDown"), ("Duplicate page", "DuplicatePage"), ("Delete page", "DeletePage"),
            ("Extract pages…", "Extract"), ("Merge PDFs…", "Merge"), ("Split PDF…", "Split"));
        Add("_Annotate", ("Select", "Select"), ("Highlight", "Highlight"), ("Underline", "Underline"),
            ("Rectangle", "Rectangle"), ("Ellipse", "Ellipse"), ("Note", "Note"), ("Properties…", "Properties"),
            ("Delete overlay", "DeleteOverlay"), ("Comments…", "Comments"));
        Add("_Sign", ("Add signature image…", "Signature"));
        Add("_Tools", ("OCR…", "Ocr"), ("Create PDF from images…", "Images"), ("Document information…", "DocumentInfo"));
        Add("_Help", ("Quick guide", "Help"), ("Third-Party Notices", "Notices"), ("MIT License", "License"), ("About PerplexPDF", "About"));
        return row;

        void Add(string label, params (string Label, string Key)[] items)
        {
            var parent = gtk_menu_item_new_with_mnemonic(label);
            var menu = gtk_menu_new();
            gtk_menu_item_set_submenu(parent, menu);
            foreach (var item in items)
                gtk_menu_shell_append(menu, CommandWidget(item.Key, item.Label, true));
            gtk_menu_shell_append(bar, parent);
        }
    }

    private nint CreateToolbar()
    {
        var toolbar = gtk_flow_box_new();
        gtk_container_set_border_width(toolbar, 5);
        gtk_flow_box_set_selection_mode(toolbar, 0);
        gtk_flow_box_set_row_spacing(toolbar, 4);
        gtk_flow_box_set_column_spacing(toolbar, 4);
        gtk_flow_box_set_max_children_per_line(toolbar, 16);
        gtk_flow_box_set_min_children_per_line(toolbar, 3);
        foreach (var (key, label) in new[] { ("Open", "Open"), ("SavePdf", "Save PDF"), ("SaveProject", "Save Project"),
                     ("Undo", "↶"), ("Redo", "↷"), ("Find", "Find"), ("Text", "Add Text"),
                     ("EditPdfText", "Edit text"), ("Image", "Add Image"), ("Highlight", "Highlight"), ("Note", "Note"),
                     ("Ocr", "OCR"), ("ExportWord", "Export to Word"), ("ExportEpub", "Export to Epub") })
        {
            var button = CommandWidget(key, label, false);
            if (key == "Open") gtk_style_context_add_class(gtk_widget_get_style_context(button), "suggested-action");
            gtk_flow_box_insert(toolbar, button, -1);
        }
        return toolbar;
    }

    private nint CommandWidget(string key, string label, bool menu)
    {
        var command = _commands[key];
        var widget = menu ? gtk_menu_item_new_with_mnemonic(label) : gtk_button_new_with_label(label);
        _controls.Add((widget, command));
        Bind(widget, menu ? "activate" : "clicked", new Signal((_, _) => Invoke(key)));
        if (key is "Undo" or "Redo") gtk_widget_set_tooltip_text(widget, key);
        return widget;
    }

    internal void Invoke(string key)
    {
        if (_commands.TryGetValue(key, out var command) && (!Busy || key == "Cancel") && command.Enabled())
            Launch(command.Action);
    }

    internal void Launch(Func<Task> work) => _ = Observe(work);
    private async Task Observe(Func<Task> work)
    {
        try { await work(); }
        catch (Exception error) { Report(error); }
    }

    private bool Shortcut(nint key)
    {
        if (gdk_event_get_keyval(key, out var value) == 0 || gdk_event_get_state(key, out var modifiers) == 0)
            return false;
        if (value == 0xff1b) { Invoke("Cancel"); return true; }
        if ((modifiers & (_primaryModifier == 0 ? Control : _primaryModifier)) == 0)
            return false;
        var name = value switch
        {
            0x006f or 0x004f => "Open",
            0x0073 or 0x0053 => (modifiers & Shift) != 0 ? "SavePdf" : "SaveProject",
            0x0066 or 0x0046 => "Find",
            0x007a or 0x005a => (modifiers & Shift) != 0 ? "Redo" : "Undo",
            0x0079 or 0x0059 => "Redo",
            0x0070 or 0x0050 => "Print",
            0x0063 or 0x0043 when !string.IsNullOrEmpty(Active?.SelectedText) => "Copy",
            0x002b or 0x003d => "ZoomIn",
            0x002d => "ZoomOut",
            _ => null
        };
        if (name is null)
            return false;
        Invoke(name);
        return true;
    }

    private void EnableDrop()
    {
        var mime = Marshal.StringToCoTaskMemUTF8("text/uri-list");
        try
        {
            var target = new Target { Name = mime };
            gtk_drag_dest_set(_window, 7, ref target, 1, 1 << 1);
        }
        finally { Marshal.FreeCoTaskMem(mime); }
        Bind(_window, "drag-data-received", new DropSignal((_, context, _, _, selection, _, time, _) =>
        {
            var paths = new List<string>();
            var uris = gtk_selection_data_get_uris(selection);
            if (uris != 0)
            {
                try
                {
                    for (var index = 0; ; index++)
                    {
                        var item = Marshal.ReadIntPtr(uris, index * IntPtr.Size);
                        if (item == 0) break;
                        if (Uri.TryCreate(String(item), UriKind.Absolute, out var uri) && uri.IsFile && uri.Host.Length == 0)
                            paths.Add(uri.LocalPath);
                    }
                }
                finally { g_strfreev(uris); }
            }
            var accept = !Busy && !_closing && paths.Count > 0;
            gtk_drag_finish(context, accept ? 1 : 0, 0, time);
            if (accept) Launch(() => OpenFiles(paths));
        }));
    }

    private async Task Start()
    {
        try
        {
            await Task.Run(_services.Local.CleanStaleTemp);
            var drafts = await Task.Run(_services.Local.FindDrafts);
            if (drafts.Count > 0 && _dialogs.Ask("Recover projects", Format("RecoverDrafts", drafts.Count)))
                await OpenFiles(drafts, recovered: true);
            await OpenFiles(_paths);
            _draftTimer = Source(() => { Launch(SaveDrafts); return true; }, 15_000);
            if (!_services.HasQpdf)
                Status("Install qpdf for PDF export and page operations.");
        }
        finally
        {
            _started = true;
            OpenQueuedFiles();
        }
    }

    private async Task<bool> Run(string title, Func<CancellationToken, Task> work)
    {
        if (Busy)
            return false;
        using var operation = new CancellationTokenSource();
        _operation = operation;
        Status(title);
        UpdateActive();
        try { await work(operation.Token); return true; }
        catch (OperationCanceledException) { Status(L("Canceled")); return false; }
        catch (Exception error) { Report(error); return false; }
        finally
        {
            _operation = null;
            UpdateActive();
            if (_desktopOpen.Count != 0)
                Post(OpenQueuedFiles);
        }
    }

    internal void Status(string text)
    {
        var page = Active is { } tab ? $"Page {tab.Index + 1} / {tab.Project.Pages.Length}    " : "";
        gtk_label_set_text(_status, page + text);
    }

    internal void UpdateActive()
    {
        if (_closing && _tabs.Count == 0)
            return;
        var tab = Active;
        gtk_window_set_title(_window, tab is null ? "PerplexPDF" : $"{tab.Name}{(tab.Document.History.IsDirty ? " •" : "")} — PerplexPDF");
        if (tab is null)
        {
            gtk_widget_show(_welcome);
            gtk_widget_hide(_notebook);
        }
        else
        {
            gtk_widget_hide(_welcome);
            gtk_widget_show(_notebook);
            tab.UpdateTitle();
            tab.Draw();
        }
        foreach (var (widget, command) in _controls)
            gtk_widget_set_sensitive(widget, !Busy && command.Enabled() ? 1 : 0);
        gtk_widget_set_sensitive(_cancel, 1);
        if (Busy) gtk_widget_show(_cancel);
        else gtk_widget_hide(_cancel);
    }

    internal void Report(Exception error, bool modal = true)
    {
        if (error is OperationCanceledException)
            return;
        var failure = error as AppFailure ?? new AppFailure(error switch
        {
            UnauthorizedAccessException => "PermissionError",
            FileNotFoundException or DirectoryNotFoundException => "FileMissing",
            IOException => "FileError",
            FormatException => "InvalidRange",
            InvalidDataException => "InvalidProject",
            _ => "UnexpectedError"
        }, error);
        _diagnostics.Record(failure);
        Console.Error.WriteLine(error);
        Status(L(failure.Code));
        if (modal && !_closing)
            _dialogs.Message("PerplexPDF", $"{L(failure.Code)}\n\n{Format("Diagnostic", failure.Code, failure.DiagnosticId.ToString("N"))}");
    }

    private void CancelOperation()
    {
        if (Busy) { _operation?.Cancel(); return; }
        Tool(null);
    }

    internal bool CanInteract(DocumentTab tab) => !Busy && !_closing && ReferenceEquals(tab, Active);

    private void ShowFile(string title, string file)
    {
        _dialogs.Message(title, File.ReadAllText(Path.Combine(Root, file)));
    }

    private void About()
    {
        using var dialog = new Dialog(_window, "About PerplexPDF", null, "Close");
        Pack(dialog.Content, Brand(44));
        Pack(dialog.Content, Label($"PerplexPDF 1.0.0 · {Platform}"));
        Pack(dialog.Content, gtk_link_button_new_with_label(Website, L("AboutCopyright")));
        Pack(dialog.Content, Label("MIT License · Local PDF editor and organizer"));
        dialog.Run();
    }

    private void DocumentInfo()
    {
        var tab = Active!;
        var info = tab.Document.Pdf.Metadata;
        _dialogs.Message("Document information", Format("InfoText", info.Title, info.Author, info.Keywords,
            tab.Project.Pages.Length, L(info.Signed ? "SignaturePresent" : "SignatureAbsent")));
    }
}
