PerplexPDF 1.0.0 | Windows x64 test build

Extract the whole ZIP into a user folder on Windows 11 x64. Run
Setup-PerplexPDF.cmd to install for your user or start PerplexPDF.exe from
that folder for portable use. To associate .pdf, choose PerplexPDF in
Windows Settings > Apps > Default apps. Open example.pdf to try the app.
The .NET 10 desktop runtime, PDFium, qpdf, SkiaSharp and the fonts are included.

If PerplexPDF shows a startup error, check the local diagnostic log at
%LOCALAPPDATA%\PerplexPDF\startup.log.

OCR is unavailable in this Windows test build. Review of the two
supplied Windows OCR installers found older codec dependencies. The
release requires an audited Windows Tesseract bundle. The Linux test
run passed with both English and German OCR models.

This build was compiled on Linux. Run the Windows start and functional tests
before distribution. Record the results in RELEASE_CHECKLIST.md.
SHA256SUMS.txt lists hashes of all included payload files.
