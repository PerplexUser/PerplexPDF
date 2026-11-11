using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace PerplexPDF.App;

public partial class CopyrightLink : UserControl
{
    public CopyrightLink() => InitializeComponent();

    internal CopyrightLink(string caption) : this()
    {
        SiteCaption.Text = caption;
        AutomationProperties.SetName(SiteLink, caption);
    }

    private void OpenCopyright(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo("https://www.perplex.click/") { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(Window.GetWindow(this), Strings.Get("BrowserError"), "PerplexPDF",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
