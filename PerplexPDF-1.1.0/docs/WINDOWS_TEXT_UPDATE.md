# PerplexPDF Windows x64 text and Word test update

The source update builds on `3f01c5bc5c6e4e74e2eb28f5d9bc2fef08e36aec`, the last Windows conversion build. PDFCraft informed the direct text editing workflow, page duplication, and bookmark search. The editable Word mode follows the layout heuristics of the supplied JavaScript converter. The Windows application uses its existing PDFium, qpdf, and WPF stack.

## Build provenance

- Built the Windows WPF assemblies with .NET SDK 10.0.403 on Debian x64 and the supplied Microsoft.WindowsDesktop.App.Ref 10.0.14 targeting pack. The package's Microsoft and NuGet signatures passed offline verification.
- In a temporary build tree, the PDF project compiled against SkiaSharp.dll from the preceding self-contained Windows x64 conversion ZIP. This is the SkiaSharp 3.119.2 dependency already locked by the source. The source ZIP retains the normal package references and lock files.
- Staged the recompiled PerplexPDF assemblies with the preceding Windows x64 apphost, dependency manifest, .NET 10.0.14 runtime, and matching native tools. The input ZIP passed its complete SHA-256 manifest. The resulting ZIP has a fresh payload manifest for setup and offline verification.
- The standard full offline build procedure remains in [WINDOWS_CROSS_BUILD.md](WINDOWS_CROSS_BUILD.md).

## Observed checks

- WPF cross-build succeeded. The main assembly contains the compiled XAML, embedded English fallback strings, and icon.
- Fifteen Linux scenarios passed, including consecutive PDF text replacements, search after export, page duplication, structured DOCX, Word and EPUB page-image modes, and bilingual OCR. Linux tests used Linux builds of the native engines and the supplied OCR language models.
- The example Word document was rendered and visually checked in LibreOffice. It contains an editable table, Word bullets, an embedded picture, contents entries with dotted tabs, and a page field.
- Windows startup, installed font matching, the color picker, PDF association, and Windows OCR still need a run on a Windows machine. Start with `example.pdf` and `examples/word-layout.pdf`.

Direct editing works with supported visible text objects in unsigned and unencrypted PDFs. The original stays open, and the changed content goes into a working copy. Image scans can be made searchable using OCR. Use the Word page-image mode when visual fidelity to the source takes priority over editing the Word content.
