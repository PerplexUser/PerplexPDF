using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Xps;
using PerplexPDF.Application;
using PerplexPDF.Domain;

namespace PerplexPDF.App;

public partial class MainWindow
{
    private async Task PrintAsync()
    {
        if (Active is not { } tab)
            return;
        var project = tab.Project;
        var dialog = new PrintDialog { UserPageRangeEnabled = true, MinPage = 1, MaxPage = (uint)project.Pages.Length };
        if (dialog.ShowDialog() != true)
            return;
        var from = dialog.PageRangeSelection == PageRangeSelection.UserPages ? dialog.PageRange.PageFrom - 1 : 0;
        var to = dialog.PageRangeSelection == PageRangeSelection.UserPages ? dialog.PageRange.PageTo - 1 : project.Pages.Length - 1;
        if (from < 0 || to < from || to >= project.Pages.Length)
            throw new AppFailure("InvalidRange");
        var queue = dialog.PrintQueue ?? throw new AppFailure("PrintFailed");
        var ticket = dialog.PrintTicket ?? throw new AppFailure("PrintFailed");
        var capabilities = queue.GetPrintCapabilities(ticket);
        var mediaWidth = capabilities.OrientedPageMediaWidth ?? dialog.PrintableAreaWidth;
        var mediaHeight = capabilities.OrientedPageMediaHeight ?? dialog.PrintableAreaHeight;
        var printable = capabilities.PageImageableArea;
        var originX = printable?.OriginWidth ?? 0;
        var originY = printable?.OriginHeight ?? 0;
        var width = printable?.ExtentWidth ?? dialog.PrintableAreaWidth;
        var height = printable?.ExtentHeight ?? dialog.PrintableAreaHeight;
        if (width <= 0 || height <= 0 || mediaWidth <= 0 || mediaHeight <= 0)
            throw new AppFailure("PrintFailed");

        if (await RunAsync("PreparingPrint", async ct =>
        {
            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = new Size(mediaWidth, mediaHeight);
            var pages = project.Pages.Skip(from).Take(to - from + 1).ToArray();
            var totalArea = pages.Sum(page =>
            {
                var geometry = tab.Document.Pdf.Pages[page.SourcePage];
                return geometry.Width * geometry.Height;
            });
            var dpi = Math.Min(165, 72 * Math.Sqrt(55_000_000 / totalArea));
            if (dpi < 6)
                throw new AppFailure("RenderLimit");
            foreach (var page in pages)
            {
                ct.ThrowIfCancellationRequested();
                var points = new PageTransform(tab.Document.Pdf.Pages[page.SourcePage], page.Rotation).Display;
                var pageDpi = Math.Min(dpi, 72 * 12_000 / Math.Max(points.Width, points.Height));
                var png = await Task.Run(() => Workspace.RenderPngAsync(tab.Document, project, page, pageDpi, ct), ct);
                var scale = Math.Min(width / points.Width, height / points.Height);
                var image = new Image { Source = PageSurface.Decode(png), Width = points.Width * scale, Height = points.Height * scale };
                var output = new FixedPage { Width = mediaWidth, Height = mediaHeight };
                FixedPage.SetLeft(image, originX + (width - image.Width) / 2);
                FixedPage.SetTop(image, originY + (height - image.Height) / 2);
                output.Children.Add(image);
                var content = new PageContent();
                ((IAddChild)content).AddChild(output);
                document.Pages.Add(content);
            }
            ct.ThrowIfCancellationRequested();
            var writer = PrintQueue.CreateXpsDocumentWriter(queue);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            writer.WritingCompleted += (_, eventArgs) =>
            {
                if (eventArgs.Error is not null)
                    completion.TrySetException(new AppFailure("PrintFailed", eventArgs.Error));
                else if (eventArgs.Cancelled)
                    completion.TrySetCanceled();
                else
                    completion.TrySetResult();
            };
            writer.WritingCancelled += (_, _) => completion.TrySetCanceled();
            using var cancel = ct.Register(() => Dispatcher.InvokeAsync(() =>
            {
                try { writer.CancelAsync(); }
                catch (InvalidOperationException) { }
            }));
            writer.WriteAsync(document.DocumentPaginator, ticket);
            await completion.Task;
        }))
            _view.Status = Strings.Get("PrintQueued");
    }
}
