using Microsoft.NET.HostModel.AppHost;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: MacAppHost APPHOST_TEMPLATE BUNDLE_EXECUTABLE");
    return 2;
}

HostWriter.CreateAppHost(args[0], args[1], "../Resources/PerplexPDF.dll",
    enableMacOSCodeSign: OperatingSystem.IsMacOS(),
    dotNetSearchOptions: new HostWriter.DotNetSearchOptions
    {
        Location = HostWriter.DotNetSearchOptions.SearchLocation.AppRelative,
        AppRelativeDotNet = "../Resources/runtime"
    });
return 0;
