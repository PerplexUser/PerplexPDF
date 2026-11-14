# PerplexPDF user guide

## Install or run portably

On macOS, run **PerplexPDF-Setup.command** from the extracted package. Open **PerplexPDF.app** under `~/Applications`. See the [macOS guide](MACOS_BUILD.md) for setup and Finder file associations. On macOS the primary shortcuts use Command.

On Linux, extract the archive and run `./PerplexPDF` or `./install.sh` for a per-user installation. See the [Linux guide](LINUX_BUILD.md) for prerequisites and desktop controls.

On Windows, run **PerplexPDF-1.1.0-Setup-win-x64.exe** to install under
`%LOCALAPPDATA%\Programs\PerplexPDF`, then open PerplexPDF from the Start menu. To use the portable ZIP, extract the whole archive and run **PerplexPDF.exe** from the extracted folder.
Setup registers PDF and PerplexPDF project files and adds a Start menu shortcut.
Choose PerplexPDF for `.pdf` under **Windows Settings > Apps > Default apps**.
Windows uses the PerplexPDF icon for the associated files. To uninstall, open
**Settings > Apps > Installed apps** and select **Uninstall** for PerplexPDF.

## Create a blank PDF on Windows

Click **New PDF** in the toolbar or choose **File > New PDF**. PerplexPDF opens a blank portrait A4 page (210 × 297 mm). Use the annotation tools to add text or images, then choose **Save PDF** to write the document.

## Open and navigate

Choose **File > Open PDF…**, drag a PDF into the window, or pass its path to PerplexPDF.exe. For a saved project, use **Open Project…**. Each file opens in a separate tab. A protected PDF asks for its password. The left panel has **Thumbnails** and **Bookmarks**. Select a thumbnail or type a page number and press Enter. Choose **Single Page** or **Continuous** under **View**. **Fit Page** and **Fit Width** scale the current view.

Use **Find** to highlight matches and move between them. To copy text, use **Select** and drag over a region, then press Ctrl+C or choose **Edit > Copy Selected Text**. Image-only scans can be made searchable with OCR.

Type part of a title in **Filter bookmarks** to find a bookmark and show its parent entries.

## Organize and annotate

Drag thumbnails to reorder pages or use the arrows above them. Use **Pages** to rotate, duplicate, delete, extract, merge, or split. **Duplicate Page** copies the current page and its overlays. Enter page ranges such as `3, 1-2` in the desired output order. For split, enter separate groups as `1-3; 4, 6`. The app writes each group as its own PDF in a new folder. Merge opens a new tab and allows you to order input PDFs first.

Choose **Add Text**, **Add Image**, or an **Annotate** tool, then drag a box on the page. For text, choose a bundled or installed Windows font, enter a size from 4 to 144 pt, select bold, italic, or underline, and open the color picker with **Choose…**. Click an overlay in **Select** mode to move it or edit its bounds and style in **Properties**, then click **Apply**. The **Comments** tab lists notes. Undo and redo cover page and overlay edits.

## Edit existing PDF text

Click **Edit text** in the toolbar or choose **Edit > Edit PDF Text…**. Click existing text and confirm the replacement. The first edit opens a working copy in a new tab. Click another text object to continue editing or press Esc to leave the tool. If the tab has page or overlay edits in its undo history, the next text edit opens another copy to retain that history. Use **Save PDF As…** to write the result.

This tool edits individual visible text objects in unsigned, unencrypted PDFs. It checks that the replacement is readable with the existing font and fits the page. A paragraph may be split across multiple text objects. OCR can prepare scanned text for search and editable Word export.

## Add a signature

Choose **Sign > Add Signature Image…**, drag a box, and place a PNG or JPG as a visible signature. Save a project to keep the image editable, or export to PDF to place it on the page.

## Save, export, and print

- **Save Project** stores the original PDF and editable objects in a `.ppdf` file. **Open Project** restores those objects. Save the project before closing if you want to continue editing.
- **Save PDF As…** writes a conventional PDF with overlays applied as page content. The underlying source text stays present when covered by an overlay.
- **Export Current Page as PNG…** creates an image using 144 pixels per inch of page size. **Print…** fits selected pages to the printer's printable area and respects page order.
- **Tools > Create PDF from Images…** converts JPG and PNG files to portrait or landscape A4 pages. Set order, image rotation, and orientation in the import dialog.

The app prompts before rewriting a signed PDF and labels detected signatures as unverified. The standard PDF exported from a password-opened source opens without a password after confirmation. A project preserves its encrypted source bytes and stores its overlay text and images in readable form.

## Convert a PDF to Word or EPUB

1. Open a PDF or a PerplexPDF project and click **Export to Word** or **Export to Epub** in the toolbar. Both actions are also available under **File**.
2. Word defaults to **Editable text**. The export reconstructs editable paragraphs, headings, tables, lists, and contents entries. It preserves text colors and simple solid background bands, and places pictures and vector artwork as separate images. The status bar reports pages without a text layer. Create a searchable copy with **Tools > OCR** for editable scans and export that copy.
3. EPUB defaults to **Editable text**. Embedded and inserted pictures stay with the text. Each tagged H1 becomes a chapter in the contents, even when several headings share a PDF page. Lower heading levels stay within their chapter. For untagged PDFs, headings are inferred from bookmarks and text formatting.
4. The page images options place complete rendered pages as lossless PNGs. Choose the output file. Both formats follow the current page order.

Try both Word modes with `examples/word-layout.pdf`. Try the EPUB chapter and picture layout with `examples/epub-structure.pdf`.

## Run offline OCR

1. Open a scanned PDF and choose **Tools > OCR…**.
2. Select the pages, English, German, or both, and a resolution. Start with 300 dpi for typical printed material.
3. Choose a filename for the searchable copy. The progress bar can cancel the operation. Selected pages are converted to images with recognized text. Other pages retain their original page content.
4. Open the copy and use **Find** to check the result. Recognition depends on the image and is intended for printed text.

## Recover edits and find diagnostic IDs

PerplexPDF saves changed project snapshots approximately every 15 seconds while the app is idle. After an unexpected exit, it offers abandoned drafts on startup. Open the draft and save it as a project. The unsaved prompt on tab or window close lets you save, discard, or cancel.

Failure dialogs contain a code and reference ID. Local logs are under `%LOCALAPPDATA%\PerplexPDF` in `diagnostics.log`. Logs contain reference IDs, operation codes, and process exit codes.

## Shortcuts

| Key | Action |
| --- | --- |
| Ctrl+N | Create a blank A4 PDF on Windows |
| Ctrl+O | Open a PDF |
| Ctrl+Shift+O | Open a project |
| Ctrl+S | Save a project |
| Ctrl+Shift+S | Export as PDF |
| Ctrl+F | Find text |
| Ctrl+C | Copy selected text |
| Ctrl+P | Print |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| Ctrl++ / Ctrl+- | Zoom |
| Esc | Cancel the operation or leave the active tool |

The status bar links to the product website. **Help > About PerplexPDF** shows the author credit and links to the MIT license and third-party notices.

The screenshot is an outstanding Windows acceptance deliverable.
