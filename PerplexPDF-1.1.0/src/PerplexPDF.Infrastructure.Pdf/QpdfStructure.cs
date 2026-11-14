using System.Globalization;
using System.Text;
using PerplexPDF.Application;
using PerplexPDF.Domain;

namespace PerplexPDF.Infrastructure.Pdf;

public sealed class QpdfStructure(string executable, IWorkDirectories directories) : IPdfStructureEngine
{
    public async Task RebuildPagesAsync(string source, string? password, IReadOnlyList<PageReference> order,
        string output, CancellationToken ct)
    {
        if (order.Count == 0)
            throw new AppFailure("InvalidRange");
        using var work = directories.Create();
        var args = PasswordArguments(password);
        args.AddRange([source, "--decrypt", "--pages", ".", Range(order.Select(page => page.SourcePage)), "--"]);
        foreach (var group in order.Select((page, index) => (page.Rotation, Index: index))
                     .Where(item => item.Rotation != 0).GroupBy(item => item.Rotation))
        {
            args.Add($"--rotate=+{Number(group.Key)}:{Range(group.Select(item => item.Index))}");
        }
        args.Add(output);
        await RunAsync(args, work, ct, password: password);
    }

    public async Task MergeAsync(IReadOnlyList<SourceSelection> sources, string output, CancellationToken ct)
    {
        if (sources.Count == 0 || sources.Any(source => source.Pages.Length == 0))
            throw new AppFailure("InvalidRange");
        using var work = directories.Create();
        var args = new List<string> { "--empty", "--pages" };
        for (var index = 0; index < sources.Count; index++)
        {
            var input = sources[index];
            var path = input.Path;
            if (input.Password is not null)
            {
                using var unlocked = directories.Create();
                var decrypt = PasswordArguments(input.Password);
                path = work.File($"unlocked-{index}.pdf");
                decrypt.AddRange([input.Path, "--decrypt", path]);
                await RunAsync(decrypt, unlocked, ct, password: input.Password);
            }
            args.Add(path);
            args.Add(Range(input.Pages));
        }
        args.AddRange(["--", output]);
        await RunAsync(args, work, ct);
    }

    public async Task ApplyOverlayAsync(string source, string overlay, string output, CancellationToken ct)
    {
        using var work = directories.Create();
        await RunAsync([source, "--overlay", overlay, "--", output], work, ct);
    }

    public async Task ValidateAsync(string source, CancellationToken ct)
    {
        using var work = directories.Create();
        await RunAsync(["--check", source], work, ct, "InvalidExport");
    }

    private static List<string> PasswordArguments(string? password)
    {
        if (password is null)
            return [];
        if (password.Contains('\r') || password.Contains('\n') || password.Contains('\0'))
            throw new AppFailure("PdfPassword");
        return ["--password-file=-"];
    }

    private async Task RunAsync(IReadOnlyList<string> args, IWorkDirectory work, CancellationToken ct, string code = "PdfToolFailed", string? password = null)
    {
        if (args.Any(arg => arg.Contains('\r') || arg.Contains('\n') || arg.StartsWith('@')))
            throw new AppFailure("UnsafePath");

        // Normal operations use wide Windows argv directly. Large page lists use qpdf's response file.
        if (args.Sum(arg => arg.Length + 3) < 24_000)
        {
            await NativeProcess.RunAsync(executable, args, TimeSpan.FromMinutes(4), code, ct, password);
            return;
        }
        var file = work.File("qpdf.args");
        await File.WriteAllLinesAsync(file, args, new UTF8Encoding(false), ct);
        await NativeProcess.RunAsync(executable, ["@" + file], TimeSpan.FromMinutes(4), code, ct, password);
    }

    private static string Range(IEnumerable<int> indices) => string.Join(",", indices.Select(index => Number(index + 1)));
    private static string Number(int number) => number.ToString(CultureInfo.InvariantCulture);
}
