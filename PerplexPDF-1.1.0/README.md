# PerplexPDF

PerplexPDF is an English-language desktop PDF editor for Windows and Linux x86-64 and for macOS on Apple Silicon and Intel. The C#/.NET 10 application opens local documents and projects, searches and displays pages, edits text and annotations, organizes pages, and exports PDF, PNG, DOCX and EPUB. OCR uses local English and German Tesseract models. The desktop interfaces use WPF on Windows and GTK 3 on Linux and macOS.

On Windows 11 x64, run **PerplexPDF-1.1.0-Setup-win-x64.exe** to install PerplexPDF for your user and open it from the Start menu. Use **File > New PDF**, the **New PDF** toolbar button, or **Ctrl+N** to create a blank portrait A4 page. See the [Windows build record](docs/WINDOWS_BUILD.md) and the [acceptance checklist](docs/RELEASE_CHECKLIST.md).

The Linux x86-64 package starts directly from its extracted directory and includes a per-user installer with PDF and project file associations. See the [Linux guide](docs/LINUX_BUILD.md) for installation and build instructions.

The macOS installer prepares a Finder application for the current Mac, checks its native document functions and installs it for the current user. See the [macOS guide](docs/MACOS_BUILD.md) for prerequisites and build instructions.

## Build on Windows x64

1. Install the build tools on the **development** machine. Use .NET SDK **10.0.404 x64**, PowerShell 7.4 or later, Git, and Visual Studio 2022 or newer Build Tools with the MSVC x64 toolchain, Windows SDK, and x64 Visual C++ redistributable files. For the installer, install NSIS 3.11 or later. Allow HTTPS access to NuGet.org and GitHub for the first build.
2. In an interactive Windows desktop session, open PowerShell in this repository and run:

   ```powershell
   ./packaging/create_release.ps1
   ```

3. The script publishes the self-contained x64 app into `artifacts/build-…/publish`, verifies and stages native dependencies and notices, runs the unit and integration suites, and smoke-tests the extracted candidate. It writes the portable ZIP and `SHA256SUMS.txt` to `artifacts/candidate` after those tests pass. If NSIS is installed, it also builds the setup EXE. Pass `-MakeNsis` with the full path to `makensis.exe` if it is installed in a custom location.
4. Test the ZIP from a clean, supported Windows 11 x64 VM with network disconnected and without installing .NET or OCR tools. Use the VM procedure in [RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md). Promote the exact verified ZIP with `./packaging/create_release.ps1 -ApprovedAcceptance <report.json>`.

Supply `-CrtDirectory` if Visual Studio's x64 app-local CRT redistributable folder is in a custom location. Keep the resolved NuGet lockfiles from the first Windows restore for subsequent locked builds. The build records dependency origins and hashes in the candidate.

## Run the package-independent checks

```text
dotnet run --project tests/PerplexPDF.OfflineChecks -c Release
```

Pass five absolute arguments after `--` to exercise the actual native adapters: PDFium library, qpdf executable, Tesseract executable, tessdata directory, and fixture directory. The check runner reports whether the German model was available. The Windows script runs the separate xUnit suites with the bundled engines.

## Project layout

| Path | Purpose |
| --- | --- |
| `src/PerplexPDF.App` | WPF shell, English resources, tab and editing view models |
| `src/PerplexPDF.App.Gtk` | GTK 3 shell, macOS integration and Cairo/Pango overlays |
| `src/PerplexPDF.Domain` | Page references, coordinates, overlays, command history |
| `src/PerplexPDF.Application` | Engine contracts, safe commits, PDF workflows |
| `src/PerplexPDF.Infrastructure.*` | PDFium, qpdf, SkiaSharp, Tesseract, project storage |
| `tests/Fixtures/example.pdf` | Original sample PDF with bookmarks and mixed page sizes |
| `docs/baseline` | Supplied R1 requirements, specification, and start order |
| `packaging` | Platform build and install scripts, native dependency locks, and Windows VM verification |

See the [English user guide](docs/USER_GUIDE_EN.md), [release notes](docs/CHANGELOG.md), and [dependency inventory](DEPENDENCIES.md).

PerplexPDF's own code is available under the [MIT License](LICENSE). Bundled components retain the terms listed in [Third-Party Notices](THIRD_PARTY_NOTICES.txt) and `licenses/`.

[© by www.perplex.click - Michael Lappenbusch](https://www.perplex.click/)
