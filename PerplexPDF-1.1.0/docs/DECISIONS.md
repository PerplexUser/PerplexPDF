# Technical decisions

| Date | Decision | Consequence |
| --- | --- | --- |
| 2026-11-08 | WPF and .NET 10, with x64 Windows as the release target | The checked-in Linux harness exercises the same non-WPF adapters. Windows 11 is the acceptance platform. |
| 2026-11-08 | Self-contained multi-file publish | The Windows app, runtime, native libraries, command-line tools, and language files stay together in the portable ZIP. |
| 2026-11-08 | PDFium uses its no-V8/no-XFA build and a read-only mapped local copy | The viewer renders passive page content and reads local bookmark destinations. Export preserves source PDF features where qpdf supports them. Exported files can retain original PDF actions for other viewers. |
| 2026-11-08 | PDFium adapter serializes native calls and bounds its raster cache | Native handles are closed on tab disposal. Individual native calls finish before cancellation takes effect. |
| 2026-11-08 | Page-local coordinates use the imported crop and visual rotation | A later relative rotation transforms pages, overlays, selection, and hit geometry together. Each overlay records its creation orientation. |
| 2026-11-08 | qpdf takes passwords over standard input | Passwords stay out of process arguments, logs, and disk credential files. The app retains the session password in memory. |
| 2026-11-08 | .ppdf schema 1.0 stores an exact source snapshot and stable page IDs | The manifest uses zero-based source indices. The UI uses one-based ranges. Known entries are streamed under fixed size and ratio limits with SHA-256 checks for the source and images. Future schemas require an explicit migration. |
| 2026-11-08 | Export uses validation and a same-directory atomic rename | A failed conversion leaves previous output intact. Split promotes a whole newly staged directory. |
| 2026-11-08 | OCR rasterizes the selected pages | It preserves the content of unselected pages. The rewritten PDF may lose document-wide outlines, tags, metadata, and signature validity. |
| 2026-11-08 | Use redistributable PDFium, qpdf, SkiaSharp and Tesseract builds | Pin build versions and origin checks. Record hashes of the actual packaged files. Complete final redistributability checks on the assembled candidate. |

## Release boundaries

R2 backlog: native PDF text/object editing, AcroForm support, independently validated redaction, export encryption, and richer annotation interoperability.

R3 backlog: cryptographic signing and verification, comparison, PDF/A and accessibility validation, repeatable batch workflows.

R4 backlog: optional online features with a separate data-flow and consent design.
