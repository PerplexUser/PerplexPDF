using System.Text;
using PerplexPDF.Application;

namespace PerplexPDF.Infrastructure.Store;

public sealed class LocalWorkspace : IWorkDirectories
{
    private readonly string _temp;
    public string Root { get; }
    public string Recovery { get; }
    public LocalWorkspace(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PerplexPDF");
        _temp = Path.Combine(Root, "Temp");
        Recovery = Path.Combine(Root, "Recovery");
        EnsurePlainDirectory(Root);
        EnsurePlainDirectory(_temp);
        EnsurePlainDirectory(Recovery);
    }

    public IWorkDirectory Create() => new LocalDirectory(_temp);
    public LocalDirectory CreateRecoverySession() => new(Recovery);

    public void CleanStaleTemp()
    {
        foreach (var folder in Directory.EnumerateDirectories(_temp))
        {
            if (Directory.GetLastWriteTimeUtc(folder) < DateTime.UtcNow.AddDays(-1) && IsAbandoned(folder))
                RemoveFolder(folder);
        }
    }

    public IReadOnlyList<string> FindDrafts() => Directory.EnumerateDirectories(Recovery)
        .Where(IsAbandoned)
        .SelectMany(folder => Directory.EnumerateFiles(folder, "*.ppdf"))
        .Where(path => Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out _) && !IsReparse(path))
        .OrderByDescending(File.GetLastWriteTimeUtc).Take(50).ToArray();

    public static void RemoveDraft(string path)
    {
        SafeFiles.TryDelete(path);
        var directory = Path.GetDirectoryName(path)!;
        try
        {
            if (!Directory.EnumerateFiles(directory, "*.ppdf").Any() && IsAbandoned(directory))
                RemoveFolder(directory);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void EnsurePlainDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        else
            Directory.CreateDirectory(path);
        if (IsReparse(path))
            throw new IOException("Unsafe application data directory.");
    }

    private static bool IsReparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsAbandoned(string folder)
    {
        if (!Guid.TryParseExact(Path.GetFileName(folder), "N", out _))
            return false;
        try
        {
            if (IsReparse(folder) || IsReparse(Path.Combine(folder, ".active")))
                return false;
            using var probe = new FileStream(Path.Combine(folder, ".active"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void RemoveFolder(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public sealed class LocalDirectory : IWorkDirectory
    {
        private readonly string _directory;
        private readonly FileStream _ownership;

        internal LocalDirectory(string parent)
        {
            _directory = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            EnsurePlainDirectory(_directory);
            _ownership = new FileStream(File(".active"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        }

        public string File(string name)
        {
            if (name.Length == 0 || name is "." or ".." ||
                name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Invalid temporary file name.", nameof(name));
            return Path.Combine(_directory, name);
        }

        public void Dispose()
        {
            _ownership.Dispose();
            RemoveFolder(_directory);
        }
    }
}

public sealed class LocalDiagnostics(string root)
{
    private readonly object _lock = new();

    public void Record(AppFailure failure)
    {
        lock (_lock)
        {
            try
            {
                var path = Path.Combine(root, "diagnostics.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                    File.Move(path, Path.Combine(root, "diagnostics.previous.log"), overwrite: true);
                File.AppendAllText(path,
                    $"{DateTimeOffset.UtcNow:O} {failure.DiagnosticId:N} {failure.Code} exit={failure.ExitCode?.ToString() ?? "-"}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
