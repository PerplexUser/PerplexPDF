PerplexPDF 1.0.0 | Local Windows x64 test with OCR and setup

1. Extract the entire ZIP to a user folder on Windows 11 x64.
2. Run Setup-PerplexPDF.cmd to install for your Windows user, or start
   PerplexPDF.exe in the extracted folder for portable use.
3. To make PerplexPDF the default for PDFs, choose it for .pdf in Windows
   Settings > Apps > Default apps. The associated files use the PerplexPDF icon.
4. Open example.pdf. Use Edit > Edit PDF Text to replace a visible text
   object, then Save PDF As. Try Add Text with an installed Windows font
   and the color picker, and Sign > Add Signature Image.
5. To test OCR, open ocr-scan-en-de.pdf, choose Tools > OCR and select
   English + German at 300 dpi. Save the copy and find "lighthouse" and "Leuchtturm".

Uninstall through Settings > Apps > Installed apps. Setup installs to
%LOCALAPPDATA%\Programs\PerplexPDF and keeps documents and recovery data separate.
Use File > Export as Word (.docx) or Export as EPUB to convert a PDF.
Choose editable text or page images. Open examples/word-layout.pdf and
compare with examples/word-layout.docx for the structured Word result.
See USER_GUIDE_EN.md for details.

The .NET desktop runtime, PDF tools, Tesseract 5.5.3 and both language models
are included. SHA256SUMS.txt covers every payload file. verify_clean_vm.ps1
provides the packaged-tool and startup checks on the Windows test machine.

If PerplexPDF shows a startup error, the dialog points to the local diagnostic
log at %LOCALAPPDATA%\PerplexPDF\startup.log when the log can be written.

This test package uses freshly compiled PerplexPDF assemblies with the prior
x64 runtime and tools. See WINDOWS_TEXT_UPDATE.md for build and test details.
This is the project owner's local acceptance package. Review the Windows
dependency and redistribution findings in TESSERACT_WINDOWS_AUDIT.md before
sharing a release. A clean Windows startup and OCR run remain to be recorded.
