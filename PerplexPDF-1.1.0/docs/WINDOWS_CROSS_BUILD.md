# Windows x64 cross-build · 2026-11-09

The WPF application was restored from the eight supplied NuGet packages and
published self-contained for win-x64 using .NET SDK 10.0.403 on Debian 13 x64.
Windows PE headers and the English fallback resources were verified. The ZIP was verified
by hashing every archived payload file.

The startup fix embeds the English strings in PerplexPDF.dll alongside the
compiled WPF resources. In the previous package, the application-wide
resource fallback pointed to the en-US satellite, which contained only the
strings. PerplexPDF now shows a dialog on an unhandled application error and
writes a local diagnostic log when the application data directory is writable.

## Observed results and open checks

- Restore and self-contained WPF publish succeeded.
- All eight package signatures passed NuGet verification in offline revocation
  mode. Online revocation and the release-time advisory audit are pending.
- Twelve Linux checks passed with the supplied English and German language
  models, including search for both recognized terms in the OCR output.
- The qpdf archive SHA-256 matched the upstream distribution. PDFium's supplied
  archive identifies version 159.0.8138.0 with V8 and XFA disabled.
- The Windows test package contains the .NET runtime, PDFium, qpdf and SkiaSharp.
  It uses the app-local CRT files shipped in the original qpdf archive for qpdf.
- Windows GUI start, Windows SkiaSharp integration, printing, OCR execution,
  final notices review and the clean Windows 11 VM gate remain open.
  The local OCR test package adds the supplied Tesseract 5.5.3 runtime and both
  language models. See [the Windows dependency audit](TESSERACT_WINDOWS_AUDIT.md).
- The uploaded Windows Tesseract 5.2.0 setup was unpacked without execution.
  The Tesseract DLL imports Leptonica, which imports the included libwebp
  DLL. Its WebPGetDecoderVersion function returns 0.6.1. This library version
  is in the affected range for [CVE-2023-4863](https://nvd.nist.gov/vuln/detail/CVE-2023-4863).
  The test package requires a separate audited Windows OCR build.
- The Tesseract 5.4.0 x64 setup was also inspected without execution.
  It includes libwebp 1.4.0 and libpng 1.6.43. The latter is in the affected
  range for [CVE-2025-65018](https://www.cve.org/CVERecord?id=CVE-2025-65018).
  Its import chain also includes JBIG-KIT 2.1, identified as GPL in the binary.
  The Windows test build uses the PDF and rendering components while the OCR
  bundle undergoes separate security and redistribution review.
- The uploaded msvcrt.dll is x86. The test package uses the x64 app-local
  Visual C++ files already shipped by qpdf and the Windows 11 system DLLs.

## Reproduce the test package

Use .NET SDK 10.0.403 and Python 3.11 or later. Place the supplied NuGet packages and the two PDF tool
archives in an offline directory. Restore only the app project against this
directory, then publish and assemble the same test package. Run from the repo.

```bash
dotnet restore src/PerplexPDF.App -r win-x64 --locked-mode \
  --source "$offline_inputs" -p:SelfContained=true -p:NuGetAudit=false
dotnet publish src/PerplexPDF.App -c Release -r win-x64 --self-contained true \
  --no-restore -p:PublishTrimmed=false -p:PublishSingleFile=false \
  -p:DebugType=embedded -o artifacts/cross-publish
python3 packaging/stage_cross_build.py --publish artifacts/cross-publish \
  --inputs "$offline_inputs" --nuget-cache "$nuget_packages" \
  --output artifacts/PerplexPDF-1.0.0-win-x64-testbuild.zip
```

Set `offline_inputs` to the directory with the original packages and
`nuget_packages` to the actual NuGet global packages directory
(`dotnet nuget locals global-packages --list`). Run the offline checks
separately from publish because the projects share build directories.

For the owner's local OCR acceptance package, add the supplied
`tesseract-ocr-w64-setup-5.5.3.20260724.exe`, `eng.traineddata` and
`deu.traineddata` to the input directory. Pass `--local-ocr` to the same
assembler and use the `PerplexPDF-1.0.0-win-x64-ocr-local-testbuild.zip`
filename. The assembler verifies every selected installer payload and model.

The full release still uses `packaging/create_release.ps1` on a Windows builder,
with the acceptance evidence listed in RELEASE_CHECKLIST.md.
