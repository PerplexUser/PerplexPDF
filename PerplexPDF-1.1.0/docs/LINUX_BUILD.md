# PerplexPDF 1.0.0 on Linux

This package targets x86-64 Linux with glibc and GTK 3. The graphical application was opened on Debian 13 with Xfce. It bundles the .NET 10 runtime, Linux PDFium, and English and German OCR data. Install GTK 3, qpdf, Tesseract and the usual .NET system libraries from your distribution. On Debian 13, use the following packages.

    sudo apt install libgtk-3-0t64 librsvg2-common qpdf tesseract-ocr fonts-dejavu-core

Extract the complete archive and open PerplexPDF, or start it from a terminal.

    ./PerplexPDF

Run `./install.sh` for a per-user application-menu entry and support for opening PDF and .ppdf files from the desktop. The system's default PDF application can be selected in the desktop settings. Run `./install.sh --uninstall` to remove the program and menu entry. Recovery data is stored under `$XDG_STATE_HOME/perplexpdf` or `~/.local/state/perplexpdf`.

The Linux workspace uses native GTK file, font, color and print dialogs. Select an annotation and open **Annotate > Properties** to change its appearance or bounds. **Pages > Move page up/down** changes page order. Word opens in editable mode. EPUB includes embedded and inserted pictures, starts a new chapter for a PDF H1 tag and uses bookmark names and typography to infer headings in untagged PDFs. **Page images** keeps the visual page layout as pictures. The included `examples/epub-structure.pdf` has two H1 headings on its first page and a third H1 on the next page.

## Rebuild

Use the pinned .NET SDK in global.json, a Linux x64 PDFium shared library with its build notices, and the English and German Tesseract models and PDF assets. From the source tree, run the Linux packaging script with five paths.

    packaging/create_linux_release.sh /path/libpdfium.so /path/pdfium-notices /path/tessdata /path/tessdata-license /path/PerplexPDF-1.0.0-linux-x64.tar.gz

The Linux GUI links to GTK, Cairo and Pango through their native C interfaces. The PDF, project, OCR, Word and EPUB engines share source with the Windows app. The Linux packaging script uses Microsoft.NETCore.App 10.0.15 from the pinned SDK. The verified Linux PDFium input is 156.0.8076.0 from pypdfium2 5.14.0's Linux x64 build. See the included notices for their licensing.
