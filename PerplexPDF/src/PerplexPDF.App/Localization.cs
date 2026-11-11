using System.Globalization;
using System.Resources;
using System.Windows.Markup;

[assembly: NeutralResourcesLanguage("en-US")]

namespace PerplexPDF.App;

internal static class Strings
{
    private static readonly ResourceManager Resources = new("PerplexPDF.App.Resources.Strings", typeof(Strings).Assembly);
    internal static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    internal static string Format(string key, params object[] values) => string.Format(CultureInfo.CurrentCulture, Get(key), values);
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class L : MarkupExtension
{
    public L(string key) => Key = key;
    [ConstructorArgument("key")]
    public string Key { get; set; }
    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
