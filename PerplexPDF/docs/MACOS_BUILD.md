# PerplexPDF 1.0.0 on macOS

The installer targets Apple Silicon and Intel x86-64 on macOS 14 or newer with a working Homebrew installation. GTK uses the Quartz backend. The archive contains the compiled .NET application, German and English OCR data, the app icon and the installer. The installer downloads the architecture-specific .NET 10.0.14 runtime, Microsoft apphost template and PDFium 156.0.8076.0 from the pinned sources in the dependency lock. It verifies the hashes before extracting the downloads.

Extract the complete ZIP and run PerplexPDF-Setup.command. If necessary, open Terminal in the extracted folder and run the following command.

    /bin/bash ./PerplexPDF-Setup.command

Setup uses Homebrew for GTK 3, qpdf and Tesseract. It prepares a native .app in a temporary directory, locally signs it and runs the bundled PDF, Word, EPUB and bilingual OCR checks. It installs the checked bundle in ~/Applications/PerplexPDF.app and opens it. To uninstall, move that app to the Trash. Documents and drafts are stored separately.

Finder can open PDF and PerplexPDF project documents in the app. To set a PDF default, use Get Info on a PDF, choose PerplexPDF under Open with and select Change All. The app uses Command for primary shortcuts. Annotation properties and page ordering use the same controls as the Linux GTK app. See the English user guide for the document workflow.

After setup, the document tools work locally. A working Homebrew installation is required when installing or updating GTK, qpdf and Tesseract. Homebrew bottle availability varies with the macOS version and processor. The local signature is ad hoc. Distribution with an Apple Developer ID and notarization requires the signing account and a Mac release build.

## Rebuild

Install the .NET SDK pinned in global.json and Python 3.11 or later. The archive can be rebuilt on Linux or macOS using the English and German Tesseract models, PDF assets and their license.

    packaging/create_macos_release.sh /path/to/tessdata /path/to/TESSDATA-APACHE-2.0.txt /path/to/PerplexPDF-1.0.0-macOS.zip

The GTK project shares the desktop code with Linux. The installer patches the downloaded native apphost using Microsoft.NET.HostModel from the pinned SDK and binds it to the precompiled managed app. Neither an SDK nor a C compiler is needed for that step on the destination Mac. Native packages are selected by processor architecture. Setup accepts previously downloaded copies of the three locked archives from the directory named by PERPLEXPDF_MACOS_DOWNLOAD_CACHE and checks the same hashes.

The managed build and shared document tests can be checked on Linux. On a Mac, also verify Finder launches the app with a PDF and a project, Command-Q prompts for changed projects, and PDF printing works with the installed print system. The setup performs its native checks before installing the app.

Upstream references

- [Microsoft .NET release metadata](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json)
- [PDFium wheels and checksums](https://pypi.org/project/pypdfium2/5.14.0/#files)
- [Homebrew GTK 3 formula](https://formulae.brew.sh/formula/gtk%2B3)
- [Apple documentation on opening downloaded apps](https://support.apple.com/en-us/102445)
