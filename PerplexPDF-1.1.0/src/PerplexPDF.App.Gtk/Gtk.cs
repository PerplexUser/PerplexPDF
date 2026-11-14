using System.Runtime.InteropServices;

namespace PerplexPDF.Desktop;

// GTK 3 and GLib's C ABI. The macOS resolver maps these names to Homebrew libraries.
internal static class Gtk
{
    private const string Library = "libgtk-3.so.0";
    private const string Gdk = "libgdk-3.so.0";
    private const string GObject = "libgobject-2.0.so.0";
    private const string GLib = "libglib-2.0.so.0";
    internal const int Horizontal = 0, Vertical = 1;
    internal const int Accept = -3, Cancel = -6, Close = -7, Yes = -8, No = -9;
    internal const int Automatic = 1, Never = 2;
    internal const uint Shift = 1, Control = 4;
    internal const int PointerMotion = 1 << 2, ButtonPress = 1 << 8, ButtonRelease = 1 << 9;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rgba { internal double Red, Green, Blue, Alpha; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Target { internal nint Name; internal uint Flags, Info; }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void Signal(nint instance, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ObjectSignal(nint instance, nint argument, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SwitchSignal(nint instance, nint child, uint index, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int EventSignal(nint instance, nint argument, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void DropSignal(nint widget, nint context, int x, int y, nint selection, uint info, uint time, nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ClosureNotify(nint data, nint closure);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SourceFunc(nint data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void DestroyNotify(nint data);

    private static readonly ClosureNotify FreeSignal = (data, _) => GCHandle.FromIntPtr(data).Free();
    private static readonly DestroyNotify FreeSource = data => GCHandle.FromIntPtr(data).Free();
    private static readonly SourceFunc RunSource = data =>
    {
        try { return ((Func<bool>)GCHandle.FromIntPtr(data).Target!)() ? 1 : 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 0; }
    };

    internal static void Bind(nint widget, string name, Delegate callback)
    {
        var handle = GCHandle.Alloc(callback);
        if (g_signal_connect_data(widget, name, Marshal.GetFunctionPointerForDelegate(callback),
                GCHandle.ToIntPtr(handle), FreeSignal, 0) == 0)
        {
            handle.Free();
            throw new InvalidOperationException($"Could not bind GTK signal {name}.");
        }
    }

    internal static uint Post(Action action) => Source(() => { action(); return false; }, 0);
    internal static uint Source(Func<bool> action, uint milliseconds)
    {
        var handle = GCHandle.ToIntPtr(GCHandle.Alloc(action));
        var id = milliseconds == 0 ? g_idle_add_full(0, RunSource, handle, FreeSource) :
            g_timeout_add_full(0, milliseconds, RunSource, handle, FreeSource);
        if (id == 0)
            GCHandle.FromIntPtr(handle).Free();
        return id;
    }

    internal static string String(nint text) => Marshal.PtrToStringUTF8(text) ?? "";
    internal static string OwnedString(nint text)
    {
        try { return String(text); }
        finally { g_free(text); }
    }

    [DllImport(GObject)] private static extern nuint g_signal_connect_data(nint widget, string name, nint callback,
        nint data, ClosureNotify free, int flags);
    [DllImport(GObject)] internal static extern void g_object_unref(nint value);
    [DllImport(GLib)] internal static extern void g_free(nint value);
    [DllImport(GLib)] internal static extern void g_slist_free(nint list);
    [DllImport(GLib)] internal static extern void g_strfreev(nint array);
    [DllImport(GLib)] internal static extern void g_error_free(nint error);
    [DllImport(GLib)] private static extern uint g_idle_add_full(int priority, SourceFunc callback, nint data, DestroyNotify free);
    [DllImport(GLib)] private static extern uint g_timeout_add_full(int priority, uint interval, SourceFunc callback, nint data, DestroyNotify free);
    [DllImport(GLib)] internal static extern int g_source_remove(uint id);

    [DllImport(Library)] internal static extern int gtk_init_check(nint argc, nint argv);
    [DllImport(Library)] internal static extern void gtk_main();
    [DllImport(Library)] internal static extern void gtk_main_quit();
    [DllImport(Library)] internal static extern nint gtk_window_new(int type);
    [DllImport(Library)] internal static extern void gtk_window_set_application(nint window, nint application);
    [DllImport(Library)] internal static extern void gtk_window_present(nint window);
    [DllImport(Library)] internal static extern void gtk_accelerator_parse(string text, out uint key, out uint modifiers);
    [DllImport(Library)] internal static extern void gtk_window_set_title(nint window, string title);
    [DllImport(Library)] internal static extern void gtk_window_set_default_size(nint window, int width, int height);
    [DllImport(Library)] internal static extern void gtk_window_set_transient_for(nint window, nint parent);
    [DllImport(Library)] internal static extern void gtk_window_set_modal(nint window, int modal);
    [DllImport(Library)] internal static extern void gtk_window_set_position(nint window, int position);
    [DllImport(Library)] internal static extern void gtk_window_set_wmclass(nint window, string name, string @class);
    [DllImport(Library)] internal static extern int gtk_window_set_icon_from_file(nint window, string filename, out nint error);
    [DllImport(Library)] internal static extern void gtk_window_close(nint window);
    [DllImport(Library)] internal static extern nint gtk_box_new(int orientation, int spacing);
    [DllImport(Library)] internal static extern void gtk_box_pack_start(nint box, nint child, int expand, int fill, uint padding);
    [DllImport(Library)] internal static extern void gtk_box_pack_end(nint box, nint child, int expand, int fill, uint padding);
    [DllImport(Library)] internal static extern void gtk_container_add(nint container, nint child);
    [DllImport(Library)] internal static extern void gtk_container_set_border_width(nint container, uint width);
    [DllImport(Library)] internal static extern void gtk_widget_show_all(nint widget);
    [DllImport(Library)] internal static extern void gtk_widget_show(nint widget);
    [DllImport(Library)] internal static extern void gtk_widget_hide(nint widget);
    [DllImport(Library)] internal static extern void gtk_widget_destroy(nint widget);
    [DllImport(Library)] internal static extern void gtk_widget_set_sensitive(nint widget, int enabled);
    [DllImport(Library)] internal static extern void gtk_widget_set_size_request(nint widget, int width, int height);
    [DllImport(Library)] internal static extern void gtk_widget_set_hexpand(nint widget, int expand);
    [DllImport(Library)] internal static extern void gtk_widget_set_vexpand(nint widget, int expand);
    [DllImport(Library)] internal static extern void gtk_widget_set_halign(nint widget, int align);
    [DllImport(Library)] internal static extern void gtk_widget_set_valign(nint widget, int align);
    [DllImport(Library)] internal static extern void gtk_widget_set_tooltip_text(nint widget, string text);
    [DllImport(Library)] internal static extern void gtk_widget_add_events(nint widget, int mask);
    [DllImport(Library)] internal static extern void gtk_widget_queue_draw(nint widget);
    [DllImport(Library)] internal static extern void gtk_widget_grab_focus(nint widget);
    [DllImport(Library)] internal static extern int gtk_widget_get_allocated_width(nint widget);
    [DllImport(Library)] internal static extern int gtk_widget_get_allocated_height(nint widget);
    [DllImport(Library)] internal static extern nint gtk_widget_get_style_context(nint widget);
    [DllImport(Library)] internal static extern void gtk_style_context_add_class(nint context, string name);
    [DllImport(Library)] internal static extern nint gtk_button_new_with_label(string text);
    [DllImport(Library)] internal static extern nint gtk_label_new(string? text);
    [DllImport(Library)] internal static extern void gtk_label_set_text(nint label, string text);
    [DllImport(Library)] internal static extern void gtk_label_set_xalign(nint label, float alignment);
    [DllImport(Library)] internal static extern void gtk_label_set_ellipsize(nint label, int mode);
    [DllImport(Library)] internal static extern void gtk_label_set_line_wrap(nint label, int wrap);
    [DllImport(Library)] internal static extern void gtk_label_set_selectable(nint label, int selectable);
    [DllImport(Library)] internal static extern nint gtk_link_button_new_with_label(string uri, string label);
    [DllImport(Library)] internal static extern nint gtk_image_new_from_file(string path);
    [DllImport(Library)] internal static extern nint gtk_image_new();
    [DllImport(Library)] internal static extern nint gtk_image_new_from_pixbuf(nint pixbuf);
    [DllImport(Library)] internal static extern nint gtk_flow_box_new();
    [DllImport(Library)] internal static extern void gtk_flow_box_set_selection_mode(nint box, int mode);
    [DllImport(Library)] internal static extern void gtk_flow_box_set_row_spacing(nint box, uint spacing);
    [DllImport(Library)] internal static extern void gtk_flow_box_set_column_spacing(nint box, uint spacing);
    [DllImport(Library)] internal static extern void gtk_flow_box_set_max_children_per_line(nint box, uint count);
    [DllImport(Library)] internal static extern void gtk_flow_box_set_min_children_per_line(nint box, uint count);
    [DllImport(Library)] internal static extern void gtk_flow_box_insert(nint box, nint child, int position);
    [DllImport(Library)] internal static extern void gtk_image_set_from_surface(nint image, nint surface);
    [DllImport(Library)] internal static extern void gtk_image_set_pixel_size(nint image, int size);
    [DllImport(Library)] internal static extern nint gtk_menu_bar_new();
    [DllImport(Library)] internal static extern nint gtk_menu_new();
    [DllImport(Library)] internal static extern nint gtk_menu_item_new_with_mnemonic(string label);
    [DllImport(Library)] internal static extern nint gtk_separator_menu_item_new();
    [DllImport(Library)] internal static extern void gtk_menu_item_set_submenu(nint item, nint submenu);
    [DllImport(Library)] internal static extern void gtk_menu_shell_append(nint menu, nint child);
    [DllImport(Library)] internal static extern nint gtk_notebook_new();
    [DllImport(Library)] internal static extern void gtk_notebook_set_scrollable(nint notebook, int scrollable);
    [DllImport(Library)] internal static extern int gtk_notebook_append_page(nint notebook, nint child, nint label);
    [DllImport(Library)] internal static extern void gtk_notebook_remove_page(nint notebook, int index);
    [DllImport(Library)] internal static extern int gtk_notebook_get_current_page(nint notebook);
    [DllImport(Library)] internal static extern void gtk_notebook_set_current_page(nint notebook, int index);
    [DllImport(Library)] internal static extern nint gtk_paned_new(int orientation);
    [DllImport(Library)] internal static extern void gtk_paned_pack1(nint pane, nint child, int resize, int shrink);
    [DllImport(Library)] internal static extern void gtk_paned_pack2(nint pane, nint child, int resize, int shrink);
    [DllImport(Library)] internal static extern void gtk_paned_set_position(nint pane, int position);
    [DllImport(Library)] internal static extern nint gtk_scrolled_window_new(nint horizontal, nint vertical);
    [DllImport(Library)] internal static extern void gtk_scrolled_window_set_policy(nint scroll, int horizontal, int vertical);
    [DllImport(Library)] internal static extern nint gtk_scrolled_window_get_vadjustment(nint scroll);
    [DllImport(Library)] internal static extern nint gtk_scrolled_window_get_hadjustment(nint scroll);
    [DllImport(Library)] internal static extern double gtk_adjustment_get_value(nint adjustment);
    [DllImport(Library)] internal static extern double gtk_adjustment_get_page_size(nint adjustment);
    [DllImport(Library)] internal static extern void gtk_adjustment_set_value(nint adjustment, double value);
    [DllImport(Library)] internal static extern nint gtk_drawing_area_new();
    [DllImport(Library)] internal static extern nint gtk_list_box_new();
    [DllImport(Library)] internal static extern void gtk_list_box_insert(nint list, nint row, int position);
    [DllImport(Library)] internal static extern void gtk_list_box_select_row(nint list, nint row);
    [DllImport(Library)] internal static extern nint gtk_list_box_get_row_at_index(nint list, int index);
    [DllImport(Library)] internal static extern int gtk_list_box_row_get_index(nint row);
    [DllImport(Library)] internal static extern nint gtk_entry_new();
    [DllImport(Library)] internal static extern void gtk_entry_set_text(nint entry, string text);
    [DllImport(Library)] internal static extern nint gtk_entry_get_text(nint entry);
    [DllImport(Library)] internal static extern void gtk_entry_set_placeholder_text(nint entry, string text);
    [DllImport(Library)] internal static extern void gtk_entry_set_visibility(nint entry, int visible);
    [DllImport(Library)] internal static extern void gtk_entry_set_max_length(nint entry, int length);
    [DllImport(Library)] internal static extern void gtk_entry_set_width_chars(nint entry, int characters);
    [DllImport(Library)] internal static extern void gtk_entry_set_activates_default(nint entry, int activates);
    [DllImport(Library)] internal static extern nint gtk_text_view_new();
    [DllImport(Library)] internal static extern nint gtk_text_view_get_buffer(nint view);
    [DllImport(Library)] internal static extern void gtk_text_view_set_wrap_mode(nint view, int mode);
    [DllImport(Library)] internal static extern void gtk_text_buffer_set_text(nint buffer, string text, int length);
    [DllImport(Library)] internal static extern void gtk_text_buffer_get_bounds(nint buffer, nint start, nint end);
    [DllImport(Library)] internal static extern nint gtk_text_buffer_get_text(nint buffer, nint start, nint end, int hidden);
    [DllImport(Library)] internal static extern nint gtk_dialog_new();
    [DllImport(Library)] internal static extern nint gtk_dialog_get_content_area(nint dialog);
    [DllImport(Library)] internal static extern nint gtk_dialog_add_button(nint dialog, string label, int response);
    [DllImport(Library)] internal static extern void gtk_dialog_set_default_response(nint dialog, int response);
    [DllImport(Library)] internal static extern int gtk_dialog_run(nint dialog);
    [DllImport(Library)] internal static extern nint gtk_check_button_new_with_label(string label);
    [DllImport(Library)] internal static extern int gtk_toggle_button_get_active(nint button);
    [DllImport(Library)] internal static extern void gtk_toggle_button_set_active(nint button, int active);
    [DllImport(Library)] internal static extern nint gtk_combo_box_text_new();
    [DllImport(Library)] internal static extern void gtk_combo_box_text_append_text(nint combo, string text);
    [DllImport(Library)] internal static extern void gtk_combo_box_set_active(nint combo, int index);
    [DllImport(Library)] internal static extern int gtk_combo_box_get_active(nint combo);
    [DllImport(Library)] internal static extern nint gtk_font_button_new();
    [DllImport(Library)] internal static extern void gtk_font_chooser_set_font(nint chooser, string font);
    [DllImport(Library)] internal static extern nint gtk_font_chooser_get_font(nint chooser);
    [DllImport(Library)] internal static extern nint gtk_color_button_new_with_rgba(ref Rgba color);
    [DllImport(Library)] internal static extern void gtk_color_chooser_set_use_alpha(nint chooser, int useAlpha);
    [DllImport(Library)] internal static extern void gtk_color_chooser_get_rgba(nint chooser, out Rgba color);
    [DllImport(Library)] internal static extern nint gtk_file_chooser_dialog_new(string title, nint parent, int action, nint end);
    [DllImport(Library)] internal static extern void gtk_file_chooser_set_select_multiple(nint chooser, int multiple);
    [DllImport(Library)] internal static extern void gtk_file_chooser_set_current_name(nint chooser, string name);
    [DllImport(Library)] internal static extern void gtk_file_chooser_set_do_overwrite_confirmation(nint chooser, int confirm);
    [DllImport(Library)] internal static extern nint gtk_file_chooser_get_filenames(nint chooser);
    [DllImport(Library)] internal static extern nint gtk_file_filter_new();
    [DllImport(Library)] internal static extern void gtk_file_filter_set_name(nint filter, string name);
    [DllImport(Library)] internal static extern void gtk_file_filter_add_pattern(nint filter, string pattern);
    [DllImport(Library)] internal static extern void gtk_file_chooser_add_filter(nint chooser, nint filter);
    [DllImport(Library)] internal static extern nint gtk_clipboard_get(nint selection);
    [DllImport(Library)] internal static extern void gtk_clipboard_set_text(nint clipboard, string text, int length);
    [DllImport(Library)] internal static extern void gtk_drag_dest_set(nint widget, int flags, ref Target targets, int count, int actions);
    [DllImport(Library)] internal static extern nint gtk_selection_data_get_uris(nint selection);
    [DllImport(Library)] internal static extern void gtk_drag_finish(nint context, int success, int delete, uint time);
    [DllImport(Library)] internal static extern nint gtk_print_operation_new();
    [DllImport(Library)] internal static extern void gtk_print_operation_cancel(nint operation);
    [DllImport(Library)] internal static extern void gtk_print_operation_set_n_pages(nint operation, int count);
    [DllImport(Library)] internal static extern int gtk_print_operation_run(nint operation, int action, nint parent, out nint error);
    [DllImport(Library)] internal static extern nint gtk_print_context_get_cairo_context(nint context);
    [DllImport(Library)] internal static extern double gtk_print_context_get_width(nint context);
    [DllImport(Library)] internal static extern double gtk_print_context_get_height(nint context);
    [DllImport(Gdk)] internal static extern nint gdk_atom_intern(string name, int existing);
    [DllImport(Gdk)] internal static extern int gdk_event_get_coords(nint @event, out double x, out double y);
    [DllImport(Gdk)] internal static extern int gdk_event_get_button(nint @event, out uint button);
    [DllImport(Gdk)] internal static extern int gdk_event_get_keyval(nint @event, out uint key);
    [DllImport(Gdk)] internal static extern int gdk_event_get_state(nint @event, out uint state);
}

internal sealed class GtkContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback callback, object? state) => Gtk.Post(() => callback(state));
}
