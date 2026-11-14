# PerplexPDF 1.1.0 Windows x64

The setup EXE installs the self-contained application for the current user. It creates a Start menu shortcut, registers PDF and project file associations, and adds an uninstall entry in Windows Settings. Choose the default PDF app in Windows Settings. The application, .NET 10.0.15, PDFium, qpdf, and the English and German OCR components are included in the installer.

To build on Windows, follow the instructions in [the repository README](../README.md). The Windows release script builds the installer with NSIS 3.11 or later when available. On Linux, `dotnet publish src/PerplexPDF.App -c Release -r win-x64 --self-contained true` builds the Windows application. Stage the verified Windows native dependencies, licenses, and payload manifests using the layout in `packaging/create_release.ps1`, then pass `PAYLOAD_DIR`, `SETUP_EXE`, and `PRODUCT_VERSION` to `makensis` with `-D` and `packaging/PerplexPDF-Setup.nsi`.

The supplied setup was built on Linux with .NET SDK 10.0.404 and NSIS 3.11. Its native PDF and OCR tools are from the Windows 1.0.0 package. All 16 Linux checks passed, including creating, rendering, saving, and reopening a blank portrait A4 PDF. A Windows desktop run and Windows startup measurements remain pending.

## PerplexPDF 1.0.0 Word update

The Windows package contains the newly compiled WPF and PerplexPDF assemblies, the existing x64 apphost and icon, and the runtime and native tools from the preceding local Windows package. The build used .NET SDK 10.0.403 and Microsoft.WindowsDesktop.App.Ref 10.0.14 on Linux, with the existing SkiaSharp assembly as the compile reference. The source retains the normal NuGet references and lock files.

The WPF build passed with zero warnings and errors. Fifteen Linux scenarios passed, including direct PDF text changes, editable Word headings with a blue background and white text, mixed text colors, a separate vector crop and editable chart label, scanned pages, and bilingual OCR. The original-layout test also verified unchanged raster content and mixed page orientation. The sample Word files were opened and rendered in LibreOffice.

The package is supplied for the author's Windows verification. The revised executable and setup still need a Windows run. The included OCR runtime is the one described in [the Windows OCR dependency audit](TESSERACT_WINDOWS_AUDIT.md). The separate distribution workflow requires native dependency review and a clean Windows acceptance record.
