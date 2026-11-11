# R1 acceptance record

Updated 2026-11-09. Build host: Debian 13 x64. SDK: .NET 10.0.403. The self-contained Windows x64 WPF build succeeded. Linux checks are in [TEST_RESULTS_LINUX.txt](TEST_RESULTS_LINUX.txt). The [cross-build record](WINDOWS_CROSS_BUILD.md) describes the test package. The Windows-specific rows need evidence from a clean VM.

| Test | Requirement | Observed result and remaining acceptance |
| --- | --- | --- |
| T-001 | F-001 | Linux adapter passed normal, encrypted, incorrect-password and corrupt cases. Check Windows open dialog, drag and drop, command-line path, and password prompt. |
| T-002 | F-002 | Windows pending. Open three PDFs, edit two, switch tabs, close one, and verify tab-specific edits and warning. |
| T-003 | F-003 | Rotation geometry passed on Linux. Exercise 50%, 100%, 200%, fit width, fit page and continuous scroll on Windows. |
| T-004 | F-004 | PDFium bookmark destinations passed on Linux. Click a thumbnail and bookmark on Windows. |
| T-005 | F-005 | Search and selected text on a rotated/cropped page passed on Linux. Verify highlight, hit navigation and clipboard on Windows. |
| T-006 | F-006 | Linux qpdf verified relative rotation and reorder on a mixed-size password PDF. Verify drag, delete and export in the Windows UI. |
| T-007 | F-007 | Linux adapters passed merge and extraction. Test the Windows merge ordering and split directory workflow. |
| T-008 | F-008 | qpdf layer placement on a rotated crop was verified with a generated test layer on Linux. Run the SkiaSharp test with text and PNG, and inspect the Windows overlay editor and output. |
| T-009 | F-009 | Windows/SkiaSharp pending. Add highlight, underline, and note, edit the note, export and inspect the visible layers. |
| T-010 | F-010 | Linux command history passed 30 undo and redo operations and saved-state comparison. Verify Windows shortcuts and edits in Properties. |
| T-011 | F-011 | Linux .ppdf round-trip and re-edit passed. Run the SkiaSharp project/export integration test on Windows. |
| T-012 | F-012 | Windows pending. Export one page at 144 dpi and compare pixel dimensions. Print a selected range in order using a printer or the installed Windows PDF printer. |
| T-013 | F-013 | English-only OCR and bilingual OCR passed on Linux with the supplied models. Search found lighthouse and Leuchtturm in the OCR output. The local Windows ZIP includes Tesseract 5.5.3 and both models. Run the same recognition check in Windows offline. |
| T-014 | F-014 | Windows/SkiaSharp pending. Import the JPG and PNG fixtures, move one earlier, rotate it and verify portrait and landscape output. |
| T-015 | F-015 | Draft ownership and orphan detection passed on Linux. Test Save/Discard/Cancel and terminate the process after a draft interval on Windows. Reopen and inspect the recovered overlay. |
| T-016 | F-016, N-013 | At 1280×720 and 1920×1080, check `© by www.perplex.click` in the status bar and `© by www.perplex.click - Michael Lappenbusch` in About. Check both links, About as the last Help entry, and the MIT License button. The current copyright wording reflects the author's updated requirement. |
| T-017 | F-017 | English fallback and compiled WPF resource lookup passed on Linux for en-US and de-DE. Read all dialogs from a clean Windows profile. |
| T-018 | F-018 | Corrupt input, local cancellation and no partial OCR output were exercised on Linux. Check Windows error dialogs and local log content after corruption and an OCR cancellation. |
| T-019 | N-001, N-002, N-003 | Clean Windows 11 x64 VM pending. Follow the procedure below with .NET Desktop Runtime and OCR utilities absent from the VM. |
| T-020 | N-006 | Failed and canceled staged replacement preserved an existing file on Linux. On Windows, simulate a denied write and compare SHA-256 of the source and pre-existing destination. |
| T-021 | N-005 | Windows pending. Open javascript.pdf, scroll and select text. Confirm no alert, active link or browser request from the input document. |
| T-022 | N-008 | Source dependency inventory and license files are present. Compare the actual Windows ZIP to origin records, notices, advisories, redistributable terms and hashes. |
| T-023 | N-010 | Reference-device measurements pending. Use cold and warm starts, a 50-page 10-MB PDF, and a representative 150-dpi page. Record actual times. |
| T-024 | F-011 | Linux qpdf validation and PDFium rendering passed for the exercised structure outputs. Inspect a SkiaSharp-annotated export in two external viewers. |
| T-025 | N-005, N-007 | Linux .ppdf checks rejected traversal, absolute names, symlinks and high expansion before extracting the source. Repeat in Windows test suite. |
| T-026 | N-003, N-004 | Windows pending. Observe the app and subprocesses during opening, export and OCR with a network monitor. Repeat with networking disconnected. |
| T-027 | Word layout and toolbar | Open `examples/word-layout.pdf` and click **Export to Word**. Verify that **Editable text** is selected by default. Open the DOCX in Word and edit the heading and chart label. Confirm the blue title band, mixed red and green text, embedded picture, and separate diagonal graphic. Explicitly select **Original layout (page images)** and verify page size and orientation with the second PDF page rotated. |

## Windows 11 clean VM

1. Use a supported Windows 11 x64 VM with a standard user and a fresh profile. Record edition, build, date, reviewer, display scaling and screen size. Verify that the VM has no separately installed .NET 10 Desktop Runtime or OCR tools. Take a snapshot.
2. Transfer the candidate ZIP, matching checksum, fixtures, and `verify_clean_vm.ps1` into the VM. Disconnect its network before running the core workflows.
3. Run the script with the VM's built-in Windows PowerShell.

   ```powershell
   powershell -NoProfile -File .\verify_clean_vm.ps1 -Archive .\PerplexPDF-1.0.0-win-x64-portable.zip -Sha256 <archive-sha256>
   ```

4. Confirm the script opened example.pdf. Carry out the manual actions in the table, including PDF export and OCR. Capture the app's own screenshot for the user guide. Test the copyright hyperlink as an explicit action separately from the offline workflows.
5. Review the actual files and licenses in the ZIP. Keep Windows test logs, external viewer evidence, timing measurements, and the VM result together with the checksum. Record the approved results in a JSON file using these keys.

   ```json
   {
     "sha256": "<checksum of the exact candidate ZIP>",
     "reviewer": "<name>",
     "date": "<ISO date>",
     "windowsBuild": "<Windows 11 edition and build>",
     "cleanWindows11": true,
     "offline": true,
     "licensesApproved": true,
     "passed": ["<each verified T-ID>"]
   }
   ```

6. Use the approval file with `create_release.ps1 -ApprovedAcceptance <report.json>` on the builder to promote that exact ZIP. The script checks the archive hash and the presence of the required test IDs.

## Formatting, signatures and setup

1. With example.pdf open, add a text box with DejaVu Serif, 20 pt, Bold, Italic and Underline. Save and reopen a project. Change the size in Properties, apply it, then undo and redo. Export a PDF and check the styled text in a second viewer.
2. Use Sign > Add Signature Image with a transparent PNG. Move the signature and export to PDF. Check the page beneath the transparent regions.
3. From the extracted ZIP on a clean profile, run Setup-PerplexPDF.cmd. Verify the Start menu shortcut and PerplexPDF under Installed apps. In Default apps, select it for .pdf. Check the icon and double-click a PDF in a path with spaces. Re-run setup, then uninstall. Verify the app entries are removed and the test documents and recovery data are still available.
4. Check the toolbar logo at 100% and 150% display scaling. Export example.pdf as Word and EPUB with Editable text and with Page images. Check that the file opens in an independent reader, that page order is correct, and that the text mode supports selecting the Unicode text on the last page.

## Open requirements and deliverables

| Scope | Remaining work |
| --- | --- |
| F-001–F-018 and N-013 | Windows UI and integration acceptance listed above, including the app's screenshot, Windows OCR and printing. The Windows WPF project compiled on Linux. |
| N-001–N-004 | The Windows targeting/runtime packs were restored and the WPF app was compiled. The local acceptance ZIP includes Windows Tesseract and both language models. Build the distribution OCR runtime, run the Windows smoke test and verify the complete offline package on a clean VM. |
| N-005–N-007 | Exercise security, failure recovery and output integrity on the Windows candidate. Core archive and atomic file checks passed on Linux. |
| N-008 | The supplied NuGet packages were verified in offline revocation mode and resolved package locks and test ZIP file hashes were recorded. Complete the Windows redistribution and advisories review and verify the final notice set. |
| N-009 | Inspect keyboard operation, tooltips and visual layout in Windows. |
| F-019 / F-020 (Soll) | Export encryption and metadata editing are pending. Document information shows title, author and keywords. |
| N-010–N-012, N-014 (Soll) | Reference hardware timings, Windows large-file and accessibility checks, final build/runtime verification. |
| Release artifacts | PerplexPDF.exe was compiled and packaged in a Windows test ZIP with a file manifest. The local OCR test ZIP adds the supplied engine and data. Clear the distribution dependency review, create and hash the release ZIP, run the clean VM gates and record the release decision. |

## Environment block

The supplied Linux SDK and eight NuGet packages enabled offline Windows x64 publishing. The supplied qpdf and PDFium archives provided the corresponding Windows tools. The bilingual OCR check used Linux Tesseract and the two supplied traineddata files. The vcpkg tool archive provides build tooling. The Tesseract 5.2.0 installer includes libwebp 0.6.1 in the affected range for CVE-2023-4863. The 5.4.0 installer includes libpng 1.6.43 in the affected range for CVE-2025-65018 and a transitive GPL JBIG-KIT library. The supplied Tesseract 5.5.3 was integrated into a local Windows test ZIP. Its libpng and JBIG-KIT findings are recorded in [the Windows audit](TESSERACT_WINDOWS_AUDIT.md). A distribution-ready Windows OCR bundle and a clean Windows 11 execution environment are still required for release. Windows SkiaSharp integration and the final dependency audit also remain open.
