# Original acceptance fixtures

The PDFs and images were generated for PerplexPDF. Regenerate them with generate.py and local Pillow, ReportLab, and pypdf.

- example.pdf has bookmarks, source text, mixed page sizes, and a pre-rotated crop.
- protected.pdf has the same pages with AES-256 protection. The test user password is perplex-test.
- scanned-en-de.pdf contains English and German as images.
- javascript.pdf contains an alert OpenAction and an external test link.
- corrupt.pdf is intentionally invalid.
- 200-pages.pdf repeats the small fixture to exercise many pages.
- logo.png and portrait.jpg exercise transparency, image import, and orientation.
