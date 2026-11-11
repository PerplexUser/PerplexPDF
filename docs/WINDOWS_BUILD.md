# PerplexPDF 1.0.0 Word update for Windows

The Windows package contains the newly compiled WPF and PerplexPDF assemblies, the existing x64 apphost and icon, and the runtime and native tools from the preceding local Windows package. The build used .NET SDK 10.0.403 and Microsoft.WindowsDesktop.App.Ref 10.0.14 on Linux, with the existing SkiaSharp assembly as the compile reference. The source retains the normal NuGet references and lock files.

The WPF build passed with zero warnings and errors. Fifteen Linux scenarios passed, including direct PDF text changes, editable Word headings with a blue background and white text, mixed text colors, a separate vector crop and editable chart label, scanned pages, and bilingual OCR. The original-layout test also verified unchanged raster content and mixed page orientation. The sample Word files were opened and rendered in LibreOffice.

The package is supplied for the author's Windows verification. The revised executable and setup still need a Windows run. The included OCR runtime is the one described in [the Windows OCR dependency audit](TESSERACT_WINDOWS_AUDIT.md). The separate distribution workflow requires native dependency review and a clean Windows acceptance record.
