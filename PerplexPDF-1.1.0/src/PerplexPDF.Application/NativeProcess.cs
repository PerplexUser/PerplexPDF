using System.Diagnostics;
using System.Text;

namespace PerplexPDF.Application;

public static class NativeProcess
{
    public static async Task RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout,
        string failureCode, CancellationToken ct, string? input = null)
    {
        ct.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new AppFailure("DependencyMissing");

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(false)
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                throw new AppFailure(failureCode);
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            throw new AppFailure("DependencyMissing", error);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeout);
        // Pipe reads can outlive cancellation. WaitAsync lets us terminate the process before
        // waiting for its pipes to close, including on Windows anonymous pipes.
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        try
        {
            if (input is not null)
                await process.StandardInput.WriteLineAsync(input.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await Task.WhenAll(process.WaitForExitAsync(), stdout, stderr).WaitAsync(deadline.Token);
        }
        catch (Exception error) when (error is OperationCanceledException or IOException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await process.WaitForExitAsync(cleanup.Token); }
            catch (OperationCanceledException) { }
            ct.ThrowIfCancellationRequested();
            throw new AppFailure(error is OperationCanceledException ? "ToolTimeout" : failureCode, error);
        }
        if (process.ExitCode != 0)
            throw new AppFailure(failureCode, exitCode: process.ExitCode);
    }
}
