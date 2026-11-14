using System.Resources;
using System.Runtime.InteropServices;
using PerplexPDF.Application;
using PerplexPDF.Infrastructure.Ocr;
using PerplexPDF.Infrastructure.Pdf;
using PerplexPDF.Infrastructure.Store;
using static PerplexPDF.Desktop.Gtk;

namespace PerplexPDF.Desktop;

internal static class Program
{
    internal const string Website = "https://www.perplex.click/";
    internal static string Root { get; } = AppContext.BaseDirectory;
    internal static string Platform { get; } = OperatingSystem.IsMacOS()
        ? $"macOS {(RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "Apple Silicon" : "Intel")}"
        : "Linux x64";
    internal static string BrandPath => Path.Combine(Root, OperatingSystem.IsMacOS() ? "PerplexPDF.png" : "PerplexPDF.svg");
    internal static string StateRoot => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "PerplexPDF")
        : LinuxStateRoot();
    private static readonly ResourceManager Strings = new("PerplexPDF.Desktop.Strings", typeof(Program).Assembly);

    internal static string L(string key) => Strings.GetString(key, System.Globalization.CultureInfo.InvariantCulture) ?? key;
    internal static string Format(string key, params object[] args) => string.Format(L(key), args);

    private static int Main(string[] args)
    {
        var mac = OperatingSystem.IsMacOS();
        if (args is ["--version"])
        {
            Console.WriteLine($"PerplexPDF 1.0.0 ({Platform})");
            return 0;
        }
        try
        {
            var arch = RuntimeInformation.ProcessArchitecture;
            if (!(OperatingSystem.IsLinux() && arch == Architecture.X64 || mac && arch is Architecture.X64 or Architecture.Arm64))
                throw new InvalidOperationException("Use x86-64 Linux or macOS on Apple Silicon or Intel.");

            if (mac)
                MacDesktop.Configure();

            var selfTest = args is ["--self-test", _, _];
            if ((mac || !selfTest) && gtk_init_check(0, 0) == 0)
                throw new InvalidOperationException("Open PerplexPDF from a graphical desktop session.");

            if (args is ["--self-test", var fixtures, var output])
                return GtkChecks.RunAsync(Path.GetFullPath(fixtures), Path.GetFullPath(output)).GetAwaiter().GetResult();

            var services = new GtkServices(new LocalWorkspace(StateRoot));
            SynchronizationContext.SetSynchronizationContext(new GtkContext());
            if (mac)
                return MacDesktop.Run(services, args);
            var window = new GtkWindow(services, args);
            window.Show();
            gtk_main();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            if (mac && args is not ["--self-test", _, _])
                MacDesktop.ShowStartupError(error);
            return 1;
        }
    }

    private static string LinuxStateRoot()
    {
        var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (!Path.IsPathFullyQualified(state ?? ""))
            state = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(state!, "perplexpdf");
    }
}

internal sealed class GtkServices
{
    internal LocalWorkspace Local { get; }
    internal PdfWorkspace Workspace { get; }
    internal string Qpdf { get; }
    internal string Tesseract { get; }
    internal string Tessdata { get; }
    internal bool HasQpdf => File.Exists(Qpdf);
    internal bool HasOcr => HasQpdf && File.Exists(Tesseract) && File.Exists(Path.Combine(Tessdata, "pdf.ttf")) &&
        File.Exists(Path.Combine(Tessdata, "configs", "pdf")) && Languages.Length != 0;
    internal string[] Languages => new[] { "eng", "deu" }.Where(lang => File.Exists(Path.Combine(Tessdata, lang + ".traineddata"))).ToArray();

    internal GtkServices(LocalWorkspace local)
    {
        Local = local;
        var bundledPdfium = OperatingSystem.IsMacOS()
            ? Path.GetFullPath(Path.Combine(Program.Root, "..", "Frameworks", "libpdfium.dylib"))
            : Path.Combine(Program.Root, "libpdfium.so");
        var pdfium = PathOverride("PERPLEXPDF_PDFIUM", bundledPdfium);
        Qpdf = Executable("PERPLEXPDF_QPDF", "qpdf");
        Tesseract = Executable("PERPLEXPDF_TESSERACT", "tesseract");
        var data = new[] { Path.Combine(Program.Root, "tessdata"), "/usr/share/tesseract-ocr/5/tessdata",
            "/usr/share/tesseract/tessdata", "/usr/share/tessdata" }.FirstOrDefault(Directory.Exists) ?? "";
        Tessdata = PathOverride("PERPLEXPDF_TESSDATA", data);
        var renderer = new PdfiumRenderer(pdfium);
        var structure = new QpdfStructure(Qpdf, local);
        Workspace = new PdfWorkspace(renderer, structure, new CairoOverlayEngine(),
            new TesseractOcr(Tesseract, Tessdata, renderer, structure, local), new ZipProjectStore(local), local);
    }

    private static string PathOverride(string name, string fallback)
    {
        var candidate = Environment.GetEnvironmentVariable(name);
        return Path.IsPathFullyQualified(candidate ?? "") ? candidate! : fallback;
    }

    private static string Executable(string variable, string name)
    {
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(Path.IsPathFullyQualified).Select(folder => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
        return PathOverride(variable, OperatingSystem.IsMacOS() ? Path.Combine(MacDesktop.HomebrewPrefix, "bin", name)
            : path ?? "/usr/bin/" + name);
    }
}
