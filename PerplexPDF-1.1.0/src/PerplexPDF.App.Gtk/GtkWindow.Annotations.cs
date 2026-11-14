using System.Collections.Immutable;
using System.Security.Cryptography;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using static PerplexPDF.Desktop.Gtk;
using static PerplexPDF.Desktop.Program;

namespace PerplexPDF.Desktop;

internal sealed partial class GtkWindow
{
    private void Tool(OverlayKind? kind, string? hint = null)
    {
        if (Active is not { } tab) return;
        tab.EditTextMode = false;
        tab.Tool = kind;
        tab.Selection = null;
        tab.Selected = null;
        tab.SelectedText = "";
        Status(L(hint ?? (kind is null ? "SelectHint" : "DrawHint")));
        UpdateActive();
    }

    private void BeginEditText()
    {
        if (Active is not { } tab) return;
        if (tab.Document.Encrypted || tab.Document.Pdf.Metadata.Signed)
            throw new AppFailure("TextEditProtected");
        Tool(null);
        tab.EditTextMode = true;
        Status(L("TextEditClick"));
    }

    internal async Task Search(DocumentTab tab, string query)
    {
        if (!CanInteract(tab)) return;
        if (string.IsNullOrWhiteSpace(query))
        {
            tab.SearchResults([]);
            return;
        }
        var pages = tab.Project.Pages;
        await Run("Searching…", async ct =>
            tab.SearchResults(await tab.Document.Pdf.SearchAsync(pages, query, ct)));
    }

    internal async Task Gesture(DocumentTab tab, DocumentTab.PageSlot slot, PdfPoint start, PdfPoint end)
    {
        if (!CanInteract(tab)) return;
        if (tab.EditTextMode)
        {
            await EditText(tab, slot, end);
            return;
        }

        var bounds = PdfRect.Between(start, end);
        if (tab.Tool is { } kind)
        {
            await AddOverlay(tab, slot, kind, start, bounds);
            return;
        }

        var source = slot.Transform.ToSource(start);
        var overlay = tab.Project.Overlays.LastOrDefault(item => item.PageId == slot.Page.Id && item.Bounds.Contains(source));
        tab.Selected = overlay?.Id;
        tab.SelectedText = "";
        tab.Selection = null;
        if (overlay is not null)
        {
            if (bounds.Width > 2 || bounds.Height > 2)
            {
                var display = slot.Transform.ToDisplay(overlay.Bounds);
                var size = slot.Transform.Display;
                display = display with
                {
                    X = Math.Clamp(display.X + end.X - start.X, 0, Math.Max(0, size.Width - display.Width)),
                    Y = Math.Clamp(display.Y + end.Y - start.Y, 0, Math.Max(0, size.Height - display.Height))
                };
                Apply(tab, tab.Project with { Overlays = tab.Project.Overlays.Replace(overlay,
                    overlay with { Bounds = slot.Transform.ToSource(display).Clamp(slot.Transform.Source) }) }, "Move overlay");
            }
            Status("Overlay selected. Use Annotate → Properties to edit it.");
        }
        else if (bounds.Width > 2 && bounds.Height > 2)
        {
            var selected = slot.Transform.ToSource(bounds);
            if (await Run("Selecting text…", async ct =>
                tab.SelectedText = await tab.Document.Pdf.ReadTextAsync(slot.Page, selected, ct)))
            {
                tab.Selection = (slot.Page.Id, selected);
                Status(string.IsNullOrWhiteSpace(tab.SelectedText) ? "No selectable text in this area." : "Press Ctrl+C to copy the selection.");
            }
        }
        UpdateActive();
        tab.Draw();
    }

    private async Task EditText(DocumentTab tab, DocumentTab.PageSlot slot, PdfPoint position)
    {
        if (tab.Document.Pdf is not IAdvancedPdfEditEngine editor)
            return;
        PdfTextObject? selected = null;
        var source = slot.Transform.ToSource(position);
        if (!await Run("Reading PDF text…", async ct =>
            selected = await editor.FindTextObjectAsync(slot.Page, source, ct)))
            return;
        if (selected is null)
        {
            Status(L("TextEditMiss"));
            return;
        }
        var replace = tab.IsTextCopy && !tab.Document.History.CanUndo && !tab.Document.History.CanRedo;
        var replacement = _dialogs.Input("Edit PDF text", L(replace ? "TextEditContinuePrompt" : "TextEditPrompt"), selected.Text);
        if (replacement is null || replacement == selected.Text) return;
        if (!replace && _tabs.Count >= MaxTabs) throw new AppFailure("TabLimit");

        await Run("Rewriting PDF text…", async ct =>
        {
            await tab.DraftGate.WaitAsync(ct);
            try
            {
                var previous = tab.Document;
                var edited = await Task.Run(() => Workspace.EditTextCopyAsync(previous, tab.Project, slot.Page, selected, replacement, ct), ct);
                if (replace)
                {
                    edited.ProjectPath = previous.ProjectPath;
                    tab.ReplaceDocument(edited);
                    await Task.Run(previous.Dispose);
                }
                else
                {
                    tab.EditTextMode = false;
                    var copy = AddTab(edited, tab.BaseName + "-edited.pdf");
                    copy.IsTextCopy = true;
                    copy.EditTextMode = true;
                    Post(() => copy.GoTo(slot.Index));
                }
                Status(L("TextEditReady"));
            }
            finally { tab.DraftGate.Release(); }
        });
    }

    private async Task AddOverlay(DocumentTab tab, DocumentTab.PageSlot slot, OverlayKind kind, PdfPoint start, PdfRect display)
    {
        var defaultBounds = display.Width < 3 || display.Height < 3;
        if (defaultBounds)
        {
            var height = kind switch
            {
                OverlayKind.Text => 40, OverlayKind.Highlight => 18, OverlayKind.Underline => 8, _ => 80
            };
            display = new PdfRect(start.X, start.Y, kind == OverlayKind.Note ? 160 : 130, height).Clamp(slot.Transform.Display);
        }
        var color = kind switch
        {
            OverlayKind.Highlight => 0x70ffdf32U,
            OverlayKind.Rectangle or OverlayKind.Ellipse or OverlayKind.Underline => 0xff164dbdU,
            _ => 0xff172638U
        };
        var overlay = new OverlayObject(Guid.NewGuid(), slot.Page.Id, kind, slot.Transform.ToSource(display), color,
            FontSize: kind == OverlayKind.Note ? 12 : 16, CreationRotation: slot.Page.Rotation);
        var resources = tab.Project.Resources;
        if (kind is OverlayKind.Text or OverlayKind.Note)
        {
            if (_dialogs.Overlay(overlay, geometry: false) is not { } styled) return;
            if (defaultBounds && styled.FontSize > overlay.FontSize)
            {
                var scale = styled.FontSize / overlay.FontSize;
                var scaled = (display with { Width = display.Width * scale, Height = display.Height * scale }).Clamp(slot.Transform.Display);
                styled = styled with { Bounds = slot.Transform.ToSource(scaled) };
            }
            overlay = styled;
        }
        else if (kind == OverlayKind.Image)
        {
            var path = _dialogs.Files("Select a PNG or JPEG image", [".png", ".jpg", ".jpeg"]).FirstOrDefault();
            if (path is null) return;
            byte[]? image = null;
            if (!await Run("Reading image…", async ct => image = await Workspace.ReadImageAsync(path, ct)))
                return;
            var hash = System.Convert.ToHexStringLower(SHA256.HashData(image!));
            resources = resources.SetItem(hash, ImmutableArray.CreateRange(image!));
            overlay = overlay with { ResourceHash = hash };
        }
        Apply(tab, tab.Project with { Overlays = tab.Project.Overlays.Add(overlay), Resources = resources }, "Add overlay");
        tab.Tool = null;
        tab.Selected = overlay.Id;
        Status(L("OverlayAdded"));
        UpdateActive();
    }

    private void EditOverlay()
    {
        var tab = Active!;
        var item = tab.Project.Overlays.FirstOrDefault(item => item.Id == tab.Selected);
        if (item is null || _dialogs.Overlay(item, geometry: true) is not { } updated) return;
        Apply(tab, tab.Project with { Overlays = tab.Project.Overlays.Replace(item, updated) }, "Edit overlay");
    }

    private void DeleteOverlay()
    {
        var tab = Active!;
        var items = tab.Project.Overlays.Where(item => item.Id != tab.Selected).ToImmutableArray();
        var retained = items.Select(item => item.ResourceHash).Where(hash => hash is not null).ToHashSet();
        var resources = tab.Project.Resources.RemoveRange(tab.Project.Resources.Keys.Where(hash => !retained.Contains(hash)));
        Apply(tab, tab.Project with { Overlays = items, Resources = resources }, "Delete overlay");
        Status("Overlay deleted.");
    }

    private void CopyText()
    {
        if (Active is not { } tab) return;
        var clipboard = gtk_clipboard_get(gdk_atom_intern("CLIPBOARD", 0));
        gtk_clipboard_set_text(clipboard, tab.SelectedText, -1);
        Status(L("Copied"));
    }

    private void PageChange(Func<DocumentProject, DocumentProject> change)
    {
        if (Active is { } tab)
            Apply(tab, change(tab.Project), "Edit pages");
    }

    private void DeletePage()
    {
        if (Active is not { } tab || !_dialogs.Ask("Delete page", Format("ConfirmDeletePage", tab.Index + 1)))
            return;
        PageChange(project => PageEdits.Delete(project, tab.Page.Id));
    }

    private void History(bool redo)
    {
        if (Active is not { } tab) return;
        if (redo) tab.Document.History.Redo();
        else tab.Document.History.Undo();
        tab.Refresh();
        UpdateActive();
        Status(redo ? "Redone." : "Undone.");
    }

    private void Apply(DocumentTab tab, DocumentProject project, string action)
    {
        project.Validate(tab.Document.Pdf.Pages);
        tab.Document.History.Execute(action, project);
        tab.Refresh();
        UpdateActive();
    }

    private void ShowComments()
    {
        var tab = Active!;
        var pageNumbers = tab.Project.Pages.Select((page, index) => (page.Id, Number: index + 1))
            .ToDictionary(page => page.Id, page => page.Number);
        _dialogs.Message("Comments", string.Join("\n\n", tab.Project.Overlays.Where(item => item.Kind == OverlayKind.Note)
            .Select(item => $"Page {pageNumbers[item.PageId]}\n{item.Text}")));
    }
}
