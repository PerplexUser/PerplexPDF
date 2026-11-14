# Dependency inventory

| Component | Version or locked origin | License and purpose |
| --- | --- | --- |
| .NET SDK / .NET and Windows Desktop runtime | SDK 10.0.403, runtime 10.0.14 | MIT and Microsoft runtime third-party notices. WPF UI and self-contained .NET runtime. |
| PDFium Windows x64 | bblanchon release 159.0.8138.0, tag chromium/8138, standard package | BSD-3-Clause with bundled third-party terms. Render, text search and selection, bookmarks and metadata. Packaging project is MIT. |
| qpdf Windows x64 | v12.4.2, qpdf-12.4.2-msvc64.zip | Apache-2.0 and bundled dependency notices. Page assembly, overlays and validation. Archive SHA-256: `db87077e683630c1217e0e8f9a20a9749d952ab676e881c3689187763a5de25d`. |
| SkiaSharp / SkiaSharp.NativeAssets.Win32 | 3.119.2 | Managed MIT package and Skia native notices. Layer PDF generation, image import and PNG composition. |
| Tesseract CLI | 5.5.3, vcpkg 2026.10.16 core port on x64-windows-static | Apache-2.0, Leptonica BSD-2-Clause, transitive notices collected from the resolved port tree. PDF text layer. The release recipe requires libpng 1.6.59 and checks for JBIG dependencies. |
| tessdata_fast and PDF renderer assets | 4.1.0, eng and deu models. PDF config and GlyphLessFont from the exact Tesseract build version. | Apache-2.0. Offline recognition and invisible text. |
| DejaVu Sans, Serif and Sans Mono | DejaVu 2.37 | Bitstream Vera terms with DejaVu additions. The source ZIP includes the actual font files and their notice. |
| Visual C++ app-local runtime | Builder's x64 Microsoft.VC143.CRT redist folder | Microsoft redistributable terms. Version and hashes recorded from selected DLLs. Confirm entitlement and REDIST list during packaging. |
| xUnit / Microsoft.NET.Test.Sdk | xUnit 2.9.3, VS test adapter 3.1.5, test SDK 17.14.1 | Build-time tests. Not part of the user package. |

Sources: [.NET release metadata](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.14/release.json), [PDFium release](https://github.com/bblanchon/pdfium-binaries/releases/tag/chromium%2F8138), [qpdf release](https://github.com/qpdf/qpdf/releases/tag/v12.4.2), [SkiaSharp package](https://www.nuget.org/packages/SkiaSharp/3.119.2), [vcpkg release](https://github.com/microsoft/vcpkg/releases/tag/2026.10.16), [Tesseract compilation instructions](https://tesseract-ocr.github.io/tessdoc/Compiling.html), [tessdata_fast release](https://github.com/tesseract-ocr/tessdata_fast/releases/tag/4.1.0), [Microsoft CRT redistribution](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files).

## Lock and review

`Directory.Packages.props` pins direct package versions. The Windows restore generates lockfiles with resolved transitive versions and NuGet content hashes. The application and its non-WPF dependencies were restored against the supplied local NuGet feed. The resolved Windows app lockfiles are included. The Windows xUnit test restore and release-time audit remain open.

`packaging/native-lock.json` fixes upstream release identifiers. The builder checks qpdf against its fixed SHA-256, PDFium against the digest of the pinned immutable upstream release, and model/config files against Git blob hashes at the verified release commit. The resulting provenance and per-file SHA-256 list are part of the release candidate. The cross-built Windows test package uses the supplied PDFium archive with SHA-256 `8d48973e3eb472dda0f5f2435e595ac043f9f2219dd71883392f549445afa8a5` and the qpdf archive listed above. Its qpdf directory retains the original app-local CRT DLLs.

T-022 requires checking the actual Windows bundle against upstream notices, component versions and security advisories, and the Visual Studio redist entitlement. The final review must cover statically bundled codecs and crypto libraries as well as the visible executables. The build stops on missing notices from a redistributable package and on high/critical NuGet audit findings. The runtime and package inventory contains PDFium, qpdf, SkiaSharp, Tesseract and the components listed above. The project takes on no PDFsharp, iText or MuPDF package dependency.

The Linux checks used locally installed PDFium 156.0.8076.0, qpdf 12.2.0, Tesseract 5.5.0 and the supplied English and German models. Those native binaries are not in the source handoff or the Windows package.

## Local Windows OCR acceptance input

The supplied Tesseract 5.5.3 Windows installer and English/German models
can be assembled into the owner's local test package. See
[the Windows audit](docs/TESSERACT_WINDOWS_AUDIT.md) and the exact hashes in
`packaging/tesseract-local-test.lock.json`. The local installer path and
the Windows release builder remain separate.
