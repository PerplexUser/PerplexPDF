using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace PerplexPDF.App;

internal static class Startup
{
    private const uint ErrorIcon = 0x10;

    [STAThread]
    public static int Main()
    {
        try
        {
            return RunApplication();
        }
        catch (Exception error)
        {
            var log = WriteFailureLog(error);
            var message = $"PerplexPDF stopped because of an unexpected error (0x{error.HResult:X8}).";
            if (log is not null)
                message += $"\n\nDiagnostic log\n{log}";
            ShowError(nint.Zero, message, "PerplexPDF error", ErrorIcon);
            return 1;
        }
    }

    // Keep WPF type loading inside the exception handler in Main.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApplication()
    {
        var english = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentCulture = english;
        CultureInfo.DefaultThreadCurrentUICulture = english;
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static string? WriteFailureLog(Exception error)
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local))
                return null;
            var directory = Directory.CreateDirectory(Path.Combine(local, "PerplexPDF"));
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                return null;

            var version = typeof(Startup).Assembly.GetName().Version;
            var report = new StringBuilder().AppendLine($"PerplexPDF {version} | {DateTimeOffset.UtcNow:O}");
            // Exception messages can include paths or document data.
            for (Exception? cause = error; cause is not null; cause = cause.InnerException)
            {
                report.AppendLine($"{cause.GetType().FullName} (0x{cause.HResult:X8})");
                report.AppendLine(new StackTrace(cause, fNeedFileInfo: false).ToString());
            }

            var path = Path.Combine(directory.FullName, "startup.log");
            var staged = Path.Combine(directory.FullName, $"startup-{Guid.NewGuid():N}.tmp");
            try
            {
                using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                    writer.Write(report);
                File.Move(staged, path, overwrite: true);
                return path;
            }
            finally
            {
                File.Delete(staged);
            }
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    // The dialog must also work when WPF cannot initialize.
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ShowError(nint owner, string text, string caption, uint type);
}
