# Release notes

## 1.1.0 Windows edition · 2026-11-14

- Added a single Windows x64 installer with Start menu access, PDF and project file registration, and uninstall through Windows Settings.
- Added **New PDF** to the File menu and toolbar. **Ctrl+N** opens a blank portrait A4 page ready to edit and save.
- Open PDFs passed by Windows Explorer before startup recovery and cleanup. Read page sizes without parsing every page's content stream during open.

## 1.0.0 macOS edition · 2026-11-09

- Added a per-user macOS installer for Apple Silicon and Intel with a Finder app, icon, PDF and project file associations, and Command shortcuts.
- Shared the GTK interface with Linux and retained the editable Word export, EPUB chapter and image export, and bilingual local OCR.

## 1.0.0 Linux and EPUB update · 2026-11-09

- Added a Linux x86-64 GTK desktop build and a per-user installer with PDF and project file associations.
- EPUB exports PDF graphics and inserted pictures as image files alongside selectable text.
- Each PDF H1 tag starts a chapter and a contents entry. Lower heading levels stay within the chapter, and a chapter can span several PDF pages.

## 1.0.0 Word update · 2026-11-09

- Restored editable Word content as the default for **Export to Word**.
- Preserved mixed text colors and extracted standalone vector graphics as separate pictures, with editable labels in Word paragraphs.
- Reported pages without a text layer after export so users can identify scans for OCR.

## 1.0.0 Windows handoff · 2026-11-09

- Added toolbar actions for editing existing text and exporting to Word or EPUB.
- Shortened the status-bar credit and placed About at the bottom of Help.
- Added the project's MIT license to the source, Windows package, and About dialog.
- Made the default Word mode preserve whole pages, backgrounds, graphics, and page dimensions. The editable mode preserves text color and simple solid backgrounds.

## Windows x64 text and Word test update · 2026-11-09

- Edit visible PDF text objects in a working copy and export the changed PDF content.
- Choose installed Windows fonts and a native color picker for added text and notes.
- Rebuild editable Word paragraphs, tables, lists, images, contents entries, and footer page numbers.
- Duplicate pages with their overlays and filter the bookmarks tree.

## 1.0.0 source handoff · 2026-11-08

- English WPF workspace with tabs, keyboard shortcuts, thumbnails, bookmarks, search, text selection, and the product copyright link.
- Page rotation, reorder, deletion, extraction, split, and merge through qpdf.
- Text, image, rectangle, ellipse, highlight, underline, and note overlays. Position and appearance can be edited and undone.
- Versioned .ppdf projects, safe PDF and PNG export, and local recovery drafts.
- Local Tesseract OCR with selectable pages, languages, resolution, progress, and cancellation.
- JPG/PNG to PDF, Windows print workflow, original fixtures, and acceptance suites.

The package-independent test run is recorded in TEST_RESULTS_LINUX.txt. Windows acceptance, final redistribution review, and release packaging remain pending.

## 1.0.0 Windows cross-build · 2026-11-09

- Built the self-contained WPF application for Windows x64 from the supplied offline package feed.
- Packaged the Windows PDF tools and runtime for a Windows start test.
- Verified bilingual OCR on Linux with the supplied models and recorded dependency hashes and package locks.
- Displayed the Windows OCR tool as unavailable when the executable or required data files are missing.

## 1.0.0 local Windows OCR test · 2026-11-09

- Assembled the compiled Windows app with the supplied Tesseract 5.5.3 x64 CLI and both language models.
- Included the bilingual scan fixture and Windows verification script.
- Recorded the exact installer payload hashes, CVE-2026-46675 exposure review, and GPL dependency finding.
