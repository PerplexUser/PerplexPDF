using System.Security.Cryptography;
using PerplexPDF.Domain;

namespace PerplexPDF.Application;

public static class SafeFiles
{
    public static async Task<string> CopyPdfAsync(string source, string target, CancellationToken ct)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        CheckLength(input.Length, DocumentProject.MaxSourceBytes);
        await CheckPdfHeaderAsync(input, ct);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
        var buffer = new byte[65536];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            total += read;
            CheckLength(total, DocumentProject.MaxSourceBytes);
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static async Task CheckPdfHeaderAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[Math.Min(1024, (int)Math.Min(stream.Length, 1024))];
        await stream.ReadExactlyAsync(header, ct);
        if (header.AsSpan().IndexOf("%PDF-"u8) < 0)
            throw new AppFailure("InvalidPdf");
        stream.Position = 0;
    }

    public static void CheckLength(long size, long maximum)
    {
        if (size < 1 || size > maximum)
            throw new AppFailure("SizeLimit");
    }

    public static async Task CommitFileAsync(string stagedFile, string target, CancellationToken ct)
    {
        target = Path.GetFullPath(target);
        var directory = Path.GetDirectoryName(target) ?? throw new IOException();
        var temporary = Path.Combine(directory, $".perplexpdf-{Guid.NewGuid():N}.tmp");
        var ownsTemporary = false;
        try
        {
            await using (var input = new FileStream(stagedFile, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                ownsTemporary = true;
                await input.CopyToAsync(output, ct);
                await output.FlushAsync(ct);
                output.Flush(flushToDisk: true);
            }
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (ownsTemporary)
                TryDelete(temporary);
        }
    }

    public static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public static bool SamePath(string first, string second)
    {
        first = Path.GetFullPath(first);
        second = Path.GetFullPath(second);
        if (OperatingSystem.IsMacOS())
            return string.Equals(first.Normalize(), second.Normalize(), StringComparison.OrdinalIgnoreCase);
        return string.Equals(first, second, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
