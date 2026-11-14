using System.Globalization;
using System.Runtime.InteropServices;
using PerplexPDF.Application;
using PerplexPDF.Domain;
using static PerplexPDF.Desktop.Gtk;
using static PerplexPDF.Desktop.Program;

namespace PerplexPDF.Desktop;

internal sealed class Dialogs(nint window)
{
    internal sealed class Dialog : IDisposable
    {
        internal nint Handle { get; }
        internal nint Content { get; }
        internal Dialog(nint owner, string title, string? accept, string cancel = "Cancel")
        {
            Handle = gtk_dialog_new();
            gtk_window_set_title(Handle, title);
            gtk_window_set_transient_for(Handle, owner);
            gtk_window_set_modal(Handle, 1);
            gtk_window_set_position(Handle, 4);
            gtk_window_set_default_size(Handle, 480, -1);
            var area = gtk_dialog_get_content_area(Handle);
            Content = gtk_box_new(Vertical, 9);
            gtk_container_set_border_width(Content, 16);
            gtk_container_add(area, Content);
            gtk_dialog_add_button(Handle, cancel, Cancel);
            if (accept is not null)
            {
                gtk_dialog_add_button(Handle, accept, Accept);
                gtk_dialog_set_default_response(Handle, Accept);
            }
        }
        internal int Run()
        {
            gtk_widget_show_all(Handle);
            return gtk_dialog_run(Handle);
        }
        public void Dispose() => gtk_widget_destroy(Handle);
    }

    internal static void Pack(nint box, nint widget, bool expand = false) =>
        gtk_box_pack_start(box, widget, expand ? 1 : 0, 1, 0);

    internal static nint Label(string text, bool wrap = false)
    {
        var label = gtk_label_new(text);
        gtk_label_set_xalign(label, 0);
        if (wrap) gtk_label_set_line_wrap(label, 1);
        return label;
    }

    internal static nint Entry(string initial, int maxLength = 4000, bool secret = false)
    {
        var field = gtk_entry_new();
        gtk_entry_set_max_length(field, maxLength);
        gtk_entry_set_text(field, initial);
        gtk_entry_set_activates_default(field, 1);
        if (secret) gtk_entry_set_visibility(field, 0);
        return field;
    }

    internal static nint Check(string label, bool active)
    {
        var check = gtk_check_button_new_with_label(label);
        gtk_toggle_button_set_active(check, active ? 1 : 0);
        return check;
    }

    internal static bool Checked(nint check) => gtk_toggle_button_get_active(check) != 0;

    internal static nint Combo(IEnumerable<string> entries, int initial = 0)
    {
        var combo = gtk_combo_box_text_new();
        foreach (var entry in entries)
            gtk_combo_box_text_append_text(combo, entry);
        gtk_combo_box_set_active(combo, initial);
        return combo;
    }

    internal static nint Scroll(nint widget, int width = -1, int height = -1)
    {
        var scroll = gtk_scrolled_window_new(0, 0);
        gtk_scrolled_window_set_policy(scroll, Automatic, Automatic);
        gtk_widget_set_size_request(scroll, width, height);
        gtk_container_add(scroll, widget);
        return scroll;
    }

    internal void Message(string title, string text)
    {
        using var dialog = new Dialog(window, title, null, "Close");
        var label = Label(text, wrap: true);
        gtk_label_set_selectable(label, 1);
        Pack(dialog.Content, text.Length > 1100 ? Scroll(label, 520, 400) : label, text.Length > 1100);
        dialog.Run();
    }

    internal bool Ask(string title, string text)
    {
        using var dialog = new Dialog(window, title, "Continue");
        Pack(dialog.Content, Label(text, wrap: true));
        return dialog.Run() == Accept;
    }

    internal int AskSave(string name)
    {
        using var dialog = new Dialog(window, "Unsaved changes", "Save");
        gtk_dialog_add_button(dialog.Handle, "Discard", No);
        Pack(dialog.Content, Label(Format("UnsavedPrompt", name), wrap: true));
        return dialog.Run();
    }

    internal string? Input(string title, string description, string value = "", bool secret = false)
    {
        using var dialog = new Dialog(window, title, "Continue");
        var input = Entry(value, secret ? 256 : 4000, secret);
        Pack(dialog.Content, Label(description, wrap: true));
        Pack(dialog.Content, input);
        gtk_widget_grab_focus(input);
        return dialog.Run() == Accept ? String(gtk_entry_get_text(input)) : null;
    }

    internal string[] Files(string title, string[] extensions, bool multiple = false, string? saveName = null, bool folder = false)
    {
        var save = saveName is not null;
        var dialog = gtk_file_chooser_dialog_new(title, window, folder ? 2 : save ? 1 : 0, 0);
        try
        {
            gtk_dialog_add_button(dialog, "Cancel", Cancel);
            gtk_dialog_add_button(dialog, folder ? "Select folder" : save ? "Save" : "Open", Accept);
            gtk_dialog_set_default_response(dialog, Accept);
            if (save)
            {
                gtk_file_chooser_set_current_name(dialog, saveName!);
                gtk_file_chooser_set_do_overwrite_confirmation(dialog, 1);
            }
            else if (multiple)
                gtk_file_chooser_set_select_multiple(dialog, 1);
            if (extensions.Length != 0)
            {
                var filter = gtk_file_filter_new();
                gtk_file_filter_set_name(filter, string.Join(", ", extensions.Select(ext => "*" + ext)));
                foreach (var extension in extensions)
                {
                    gtk_file_filter_add_pattern(filter, "*" + extension);
                    gtk_file_filter_add_pattern(filter, "*" + extension.ToUpperInvariant());
                }
                gtk_file_chooser_add_filter(dialog, filter);
            }
            if (gtk_dialog_run(dialog) != Accept)
                return [];
            var list = gtk_file_chooser_get_filenames(dialog);
            var paths = new List<string>();
            try
            {
                for (var node = list; node != 0; node = Marshal.ReadIntPtr(node, IntPtr.Size))
                    paths.Add(OwnedString(Marshal.ReadIntPtr(node)));
            }
            finally { g_slist_free(list); }
            if (save && paths.Count != 0)
            {
                var extension = extensions.Single();
                if (!paths[0].EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                {
                    paths[0] += extension;
                    if (File.Exists(paths[0]) && !Ask("Replace file", $"Replace {Path.GetFileName(paths[0])}?"))
                        return [];
                }
            }
            return paths.ToArray();
        }
        finally { gtk_widget_destroy(dialog); }
    }

    internal (bool Images, WordExportOptions? Word)? Conversion(bool word)
    {
        using var dialog = new Dialog(window, L(word ? "ExportWord" : "ExportEpub"), "Continue");
        Pack(dialog.Content, Label(L(word ? "WordConversionHint" : "ConversionHint"), wrap: true));
        var mode = Combo([L("EditableText"), L(word ? "WordOriginalLayout" : "PageImages")]);
        Pack(dialog.Content, mode);
        nint[] settings = [];
        if (word)
        {
            var fields = gtk_box_new(Vertical, 4);
            settings = new[] { "WordImages", "WordTables", "WordPageBreaks", "WordReflow", "WordLists", "WordFooters" }
                .Select(key => Check(L(key), true)).ToArray();
            foreach (var field in settings) Pack(fields, field);
            Bind(mode, "changed", new Signal((_, _) => gtk_widget_set_sensitive(fields, gtk_combo_box_get_active(mode) == 0 ? 1 : 0)));
            Pack(dialog.Content, fields);
        }
        if (dialog.Run() != Accept)
            return null;
        var images = gtk_combo_box_get_active(mode) == 1;
        return (images, word && !images ? new WordExportOptions(Checked(settings[0]), Checked(settings[1]),
            Checked(settings[2]), Checked(settings[3]), Checked(settings[4]), Checked(settings[5])) : null);
    }

    internal (int[] Pages, string Language, int Dpi)? Ocr(int count, string[] languages)
    {
        using var dialog = new Dialog(window, "OCR", "Create searchable PDF");
        var range = Entry(count == 1 ? "1" : $"1-{count}");
        var codes = languages.Length == 2 ? new[] { "eng", "deu", "eng+deu" } : languages;
        var language = Combo(codes.Select(code => L(code == "eng" ? "English" : code == "deu" ? "German" : "BothLanguages")));
        var dpi = Combo(["200 dpi", "300 dpi", "400 dpi"], 1);
        Pack(dialog.Content, Label(L("PageRange")));
        Pack(dialog.Content, range);
        Pack(dialog.Content, Label("Language"));
        Pack(dialog.Content, language);
        Pack(dialog.Content, dpi);
        return dialog.Run() == Accept ? (PageRanges.Parse(String(gtk_entry_get_text(range)), count),
            codes[gtk_combo_box_get_active(language)], new[] { 200, 300, 400 }[gtk_combo_box_get_active(dpi)]) : null;
    }

    internal OverlayObject? Overlay(OverlayObject original, bool geometry)
    {
        using var dialog = new Dialog(window, geometry ? "Properties" : L(original.Kind.ToString()), geometry ? "Apply" : "Add");
        var isText = original.Kind is OverlayKind.Text or OverlayKind.Note;
        nint textBuffer = 0, font = 0, bold = 0, italic = 0, underline = 0;
        if (isText)
        {
            var text = gtk_text_view_new();
            gtk_text_view_set_wrap_mode(text, 3);
            textBuffer = gtk_text_view_get_buffer(text);
            gtk_text_buffer_set_text(textBuffer, original.Text, -1);
            Pack(dialog.Content, Label("Text"));
            Pack(dialog.Content, Scroll(text, 440, 92), true);
            font = gtk_font_button_new();
            gtk_font_chooser_set_font(font, $"{original.SystemFont ?? Graphics.FontFamily(original.Font)} {original.FontSize.ToString(CultureInfo.InvariantCulture)}");
            Pack(dialog.Content, Label("Font family and size"));
            Pack(dialog.Content, font);
            var styles = gtk_box_new(Horizontal, 10);
            bold = Check(L("Bold"), original.Bold);
            italic = Check(L("Italic"), original.Italic);
            underline = Check(L("Underline"), original.Underlined);
            foreach (var field in new[] { bold, italic, underline }) Pack(styles, field);
            Pack(dialog.Content, styles);
        }
        var rgb = new Rgba
        {
            Red = ((original.Color >> 16) & 255) / 255.0, Green = ((original.Color >> 8) & 255) / 255.0,
            Blue = (original.Color & 255) / 255.0, Alpha = (original.Color >> 24) / 255.0
        };
        var color = gtk_color_button_new_with_rgba(ref rgb);
        gtk_color_chooser_set_use_alpha(color, 1);
        Pack(dialog.Content, Label("Color"));
        Pack(dialog.Content, color);
        nint[] coordinates = [];
        if (geometry)
        {
            var row = gtk_box_new(Horizontal, 8);
            coordinates = new[] { original.Bounds.X, original.Bounds.Y, original.Bounds.Width, original.Bounds.Height }
                .Select(value => Entry(value.ToString("0.##", CultureInfo.InvariantCulture), 20)).ToArray();
            for (var i = 0; i < coordinates.Length; i++)
            {
                var column = gtk_box_new(Vertical, 4);
                gtk_entry_set_width_chars(coordinates[i], 6);
                Pack(column, Label(new[] { "X", "Y", "Width", "Height" }[i]));
                Pack(column, coordinates[i]);
                Pack(row, column, true);
            }
            Pack(dialog.Content, row);
        }
        if (dialog.Run() != Accept)
            return null;
        gtk_color_chooser_get_rgba(color, out rgb);
        static uint Byte(double value) => (uint)Math.Round(Math.Clamp(value, 0, 1) * 255);
        var result = original with { Color = Byte(rgb.Alpha) << 24 | Byte(rgb.Red) << 16 | Byte(rgb.Green) << 8 | Byte(rgb.Blue) };
        if (geometry)
        {
            var values = coordinates.Select(entry => double.TryParse(String(gtk_entry_get_text(entry)).Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : double.NaN).ToArray();
            if (values.Any(value => !double.IsFinite(value)) || values[2] <= 0 || values[3] <= 0)
                throw new AppFailure("InvalidOverlay");
            result = result with { Bounds = new(values[0], values[1], values[2], values[3]) };
        }
        if (!isText)
            return result;
        var textValue = ReadTextBuffer(textBuffer);
        if (string.IsNullOrWhiteSpace(textValue) || textValue.Length > 4000)
            throw new AppFailure("InvalidOverlay");
        var (family, size) = Graphics.ReadFont(OwnedString(gtk_font_chooser_get_font(font)));
        if (!DocumentProject.IsFontFamily(family) || size is < 4 or > 144)
            throw new AppFailure("InvalidOverlay");
        return result with { Text = textValue, SystemFont = family, FontSize = size, Bold = Checked(bold),
            Italic = Checked(italic), Underlined = Checked(underline) };
    }

    private static unsafe string ReadTextBuffer(nint buffer)
    {
        // GtkTextIter is an opaque stack value in GTK's ABI. Aligned storage for each iterator.
        var start = stackalloc nint[16];
        var end = stackalloc nint[16];
        gtk_text_buffer_get_bounds(buffer, (nint)start, (nint)end);
        return OwnedString(gtk_text_buffer_get_text(buffer, (nint)start, (nint)end, 1));
    }
}
