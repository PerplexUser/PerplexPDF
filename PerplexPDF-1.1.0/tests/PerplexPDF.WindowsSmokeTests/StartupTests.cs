using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using Xunit;

namespace PerplexPDF.WindowsSmokeTests;

public sealed class StartupTests
{
    [Fact]
    public async Task PublishedExecutableShowsToolbarAndOpensTheExample()
    {
        var root = Environment.GetEnvironmentVariable("PERPLEXPDF_RELEASE")
            ?? throw new InvalidOperationException("Set PERPLEXPDF_RELEASE to the extracted Windows candidate.");
        Assert.Contains("Copyright (c) 2026 Michael Lappenbusch", File.ReadAllText(Path.Combine(root, "LICENSE")));
        var start = new ProcessStartInfo(Path.Combine(root, "PerplexPDF.exe")) { UseShellExecute = false, WorkingDirectory = root };
        start.ArgumentList.Add(Path.Combine(root, "example.pdf"));
        using var app = Process.Start(start) ?? throw new InvalidOperationException("PerplexPDF did not start.");
        try
        {
            var until = DateTime.UtcNow.AddSeconds(30);
            var visible = false;
            while (DateTime.UtcNow < until && !app.HasExited)
            {
                app.Refresh();
                if (app.MainWindowHandle != IntPtr.Zero)
                {
                    var window = AutomationElement.FromHandle(app.MainWindowHandle);
                    var copyright = Find("© by www.perplex.click", window);
                    var page = Find("Page 1 / 4", window);
                    if (copyright is not null && !copyright.Current.IsOffscreen && page is not null &&
                        new[] { "New PDF", "Edit text", "Export to Word", "Export to Epub" }.All(name => IsVisibleButton(name, window)))
                    {
                        Assert.Null(Find("© by www.perplex.click - Michael Lappenbusch", window));
                        visible = true;
                        break;
                    }
                }
                await Task.Delay(200);
            }
            Assert.True(visible, "Expected the page count, toolbar actions and short copyright link in the published UI.");
        }
        finally
        {
            if (!app.HasExited)
            {
                app.CloseMainWindow();
                if (!app.WaitForExit(5000))
                    app.Kill(entireProcessTree: true);
            }
        }
    }

    private static AutomationElement? Find(string name, AutomationElement window) => window.FindFirst(
        TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, name));

    private static bool IsVisibleButton(string name, AutomationElement window) => Find(name, window) is { } button &&
        button.Current.ControlType == ControlType.Button && button.Current.IsEnabled && !button.Current.IsOffscreen;
}
