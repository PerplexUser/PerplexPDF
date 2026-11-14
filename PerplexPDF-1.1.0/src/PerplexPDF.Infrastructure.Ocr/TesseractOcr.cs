using System.Globalization;
using PerplexPDF.Application;
using PerplexPDF.Domain;

namespace PerplexPDF.Infrastructure.Ocr;

public sealed class TesseractOcr(
    string executable, string tessdata, IPdfRenderEngine renderer, IPdfStructureEngine structure,
    IWorkDirectories directories) : IOcrEngine
{
    public async Task CreateSearchablePdfAsync(string input, string output, IReadOnlyList<int> pages, string language,
        int dpi, IProgress<WorkProgress>? progress, CancellationToken ct)
    {
        if (language is not ("eng" or "deu" or "eng+deu") || dpi is not (200 or 300 or 400))
            throw new AppFailure("InvalidOcrSettings");
        if (language.Split('+').Any(code => !File.Exists(Path.Combine(tessdata, code + ".traineddata"))))
            throw new AppFailure("DependencyMissing");
        using var document = await renderer.OpenAsync(input, null, ct);
        var selected = pages.ToHashSet();
        if (selected.Count == 0 || selected.Any(index => index < 0 || index >= document.Pages.Count))
            throw new AppFailure("InvalidRange");

        using var work = directories.Create();
        var outputPages = new List<SourceSelection>();
        var unchanged = new List<int>();
        var completed = 0;
        progress?.Report(new(completed, selected.Count));
        for (var index = 0; index < document.Pages.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            if (!selected.Contains(index))
            {
                unchanged.Add(index);
                continue;
            }
            FlushUnchanged();
            var png = work.File("scan.png");
            var baseName = work.File($"ocr-{index:D4}");
            var raster = await document.RenderAsync(new PageReference(Guid.NewGuid(), index), dpi, ct);
            await RasterPng.WriteAsync(raster, dpi, png, ct);
            await NativeProcess.RunAsync(executable,
                [png, baseName, "--tessdata-dir", tessdata, "-l", language, "--dpi",
                 dpi.ToString(CultureInfo.InvariantCulture), "pdf"],
                TimeSpan.FromMinutes(4), "OcrFailed", ct);
            File.Delete(png);
            var page = baseName + ".pdf";
            using (var check = await renderer.OpenAsync(page, null, ct))
            {
                if (check.Pages.Count != 1)
                    throw new AppFailure("OcrFailed");
            }
            outputPages.Add(new(page, [0]));
            progress?.Report(new(++completed, selected.Count));
        }
        FlushUnchanged();
        await structure.MergeAsync(outputPages, output, ct);

        void FlushUnchanged()
        {
            if (unchanged.Count == 0)
                return;
            outputPages.Add(new(input, unchanged.ToArray()));
            unchanged.Clear();
        }
    }
}
