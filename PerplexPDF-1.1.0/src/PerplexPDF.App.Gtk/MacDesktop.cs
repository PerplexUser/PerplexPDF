using System.Diagnostics;
using System.Runtime.InteropServices;
using PerplexPDF.Infrastructure.Pdf;
using static PerplexPDF.Desktop.Gtk;

namespace PerplexPDF.Desktop;

internal static class MacDesktop
{
    private const string GtkLibrary = "libgtk-3.so.0";
    private const string GioLibrary = "libgio-2.0.so.0";
    private const string GLibLibrary = "libglib-2.0.so.0";
    private const int HandlesOpen = 1 << 2;
    private const int NonUnique = 1 << 5;
    private const int MaxOpenFiles = 16;
    private static readonly Dictionary<string, (string Formula, string Dylib)> Libraries = new()
    {
        [GtkLibrary] = ("gtk+3", "libgtk-3.0.dylib"),
        ["libgdk-3.so.0"] = ("gtk+3", "libgdk-3.0.dylib"),
        [GLibLibrary] = ("glib", "libglib-2.0.0.dylib"),
        ["libgobject-2.0.so.0"] = ("glib", "libgobject-2.0.0.dylib"),
        [GioLibrary] = ("glib", "libgio-2.0.0.dylib"),
        ["libcairo.so.2"] = ("cairo", "libcairo.2.dylib"),
        ["libgdk_pixbuf-2.0.so.0"] = ("gdk-pixbuf", "libgdk_pixbuf-2.0.0.dylib"),
        ["libpango-1.0.so.0"] = ("pango", "libpango-1.0.0.dylib"),
        ["libpangocairo-1.0.so.0"] = ("pango", "libpangocairo-1.0.0.dylib")
    };
    private static readonly Dictionary<string, nint> Loaded = new();

    internal static string HomebrewPrefix { get; private set; } = "";

    internal static void Configure()
    {
        var config = Path.Combine(Program.Root, "brew-prefix.txt");
        HomebrewPrefix = File.Exists(config) ? File.ReadAllText(config).Trim() :
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "/opt/homebrew" : "/usr/local";
        if (!Path.IsPathFullyQualified(HomebrewPrefix))
            throw new InvalidDataException("The Homebrew location is invalid. Run PerplexPDF-Setup.command again.");
        PdfiumNative.ResolveDesktopLibrary = ResolveLibrary;
        g_setenv("GDK_BACKEND", "quartz", 1);
        var sharedData = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (string.IsNullOrEmpty(sharedData))
            sharedData = "/usr/local/share:/usr/share";
        g_setenv("XDG_DATA_DIRS", $"{Path.Combine(HomebrewPrefix, "share")}:{sharedData}", 1);
    }

    private static nint ResolveLibrary(string name)
    {
        if (!Libraries.TryGetValue(name, out var library))
            return 0;
        lock (Loaded)
        {
            if (Loaded.TryGetValue(name, out var address))
                return address;
            var path = Path.Combine(HomebrewPrefix, "opt", library.Formula, "lib", library.Dylib);
            try { address = NativeLibrary.Load(path); }
            catch (DllNotFoundException error)
            {
                throw new DllNotFoundException($"Could not load {library.Formula} from {path}. Run PerplexPDF-Setup.command again.", error);
            }
            Loaded.Add(name, address);
            return address;
        }
    }

    internal static int Run(GtkServices services, string[] arguments)
    {
        var app = gtk_application_new("click.perplex.PerplexPDF", HandlesOpen | NonUnique);
        if (app == 0)
            throw new InvalidOperationException("Could not create the PerplexPDF desktop application.");
        var paths = arguments.Where(path => !path.StartsWith("-psn_", StringComparison.Ordinal)).ToArray();
        GtkWindow? window = null;
        Exception? startupError = null;
        try
        {
            Bind(app, "startup", new Signal((_, _) => Observe(() => SetMenu(app, () => window?.RequestClose()))));
            Bind(app, "activate", new Signal((_, _) => Observe(() => Window().Present())));
            Bind(app, "open", new OpenSignal((_, files, count, _, _) => Observe(() =>
            {
                var documents = new List<string>(Math.Min(MaxOpenFiles, count));
                for (var index = 0; index < Math.Min(MaxOpenFiles, count); index++)
                {
                    var path = OwnedString(g_file_get_path(Marshal.ReadIntPtr(files, index * IntPtr.Size)));
                    if (path.Length != 0)
                        documents.Add(path);
                }
                var desktop = Window();
                desktop.Present();
                desktop.OpenFromDesktop(documents);
            })));
            var status = g_application_run(app, 0, 0);
            if (startupError is not null)
                throw new InvalidOperationException("The macOS desktop window could not be opened.", startupError);
            return status;
        }
        finally { g_object_unref(app); }

        GtkWindow Window()
        {
            if (window is not null)
                return window;
            window = new GtkWindow(services, paths, app);
            window.Show();
            return window;
        }

        void Observe(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                startupError = error;
                g_application_quit(app);
            }
        }
    }

    private static void SetMenu(nint app, Action quit)
    {
        var action = g_simple_action_new("quit", 0);
        var menu = g_menu_new();
        var accelerator = Marshal.StringToCoTaskMemUTF8("<Primary>q");
        try
        {
            Bind(action, "activate", new ObjectSignal((_, _, _) => quit()));
            g_action_map_add_action(app, action);
            g_menu_append(menu, "Quit PerplexPDF", "app.quit");
            gtk_application_set_app_menu(app, menu);
            gtk_application_set_accels_for_action(app, "app.quit", [accelerator, 0]);
        }
        finally
        {
            Marshal.FreeCoTaskMem(accelerator);
            g_object_unref(menu);
            g_object_unref(action);
        }
    }

    internal static void ShowStartupError(Exception error)
    {
        try
        {
            var path = Path.Combine(Program.StateRoot, "startup.log");
            Directory.CreateDirectory(Program.StateRoot);
            File.WriteAllText(path, error.ToString());
            var dialog = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false };
            dialog.ArgumentList.Add("-e");
            dialog.ArgumentList.Add("""
                on run argv
                    display alert "PerplexPDF could not start" message (item 1 of argv) as critical
                end run
                """);
            dialog.ArgumentList.Add($"{error.GetBaseException().Message}\n\n{path}");
            Process.Start(dialog)?.Dispose();
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine(failure.Message);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OpenSignal(nint app, nint files, int count, nint hint, nint data);

    [DllImport(GtkLibrary)] private static extern nint gtk_application_new(string id, int flags);
    [DllImport(GLibLibrary)] private static extern int g_setenv(string variable, string value, int overwrite);
    [DllImport(GtkLibrary)] private static extern void gtk_application_set_app_menu(nint app, nint menu);
    [DllImport(GtkLibrary)] private static extern void gtk_application_set_accels_for_action(nint app, string action, nint[] accelerators);
    [DllImport(GioLibrary)] private static extern int g_application_run(nint app, int argc, nint argv);
    [DllImport(GioLibrary)] private static extern void g_application_quit(nint app);
    [DllImport(GioLibrary)] private static extern nint g_file_get_path(nint file);
    [DllImport(GioLibrary)] private static extern nint g_simple_action_new(string name, nint parameterType);
    [DllImport(GioLibrary)] private static extern void g_action_map_add_action(nint map, nint action);
    [DllImport(GioLibrary)] private static extern nint g_menu_new();
    [DllImport(GioLibrary)] private static extern void g_menu_append(nint menu, string label, string action);
}
