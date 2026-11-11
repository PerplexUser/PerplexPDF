# Windows OCR dependency audit · 2026-11-09

## Local acceptance package

The project's supplied Tesseract installer is the x64 5.5.3 asset published on
24 July 2026. The checked installer SHA-256 is
`bee9e3434bd94fd65387d9be28cd467a41f61b1275383b55b0f59a1331270ae4`.
The local acceptance package uses the original Tesseract CLI and its 33
app-local DLL dependencies, together with the supplied English and German
models. Windows supplies its own x64 system DLLs. The archive tool unpacked
the installer without running it.

The source archives and installer have identical Tesseract LICENSE, AUTHORS,
PDF configuration and PDF font files. The included per-file lock records
the exact CLI, DLL, configuration, font and model hashes. The Windows GUI,
native CLI and OCR path still require a Windows execution test. The prior
bilingual test used Linux Tesseract 5.5.0.

Sources: [Tesseract release](https://github.com/tesseract-ocr/tesseract/releases/tag/5.5.3), [installer checksum record](https://community.chocolatey.org/packages/tesseract/5.5.3.20260724), [installer build recipe](https://github.com/tesseract-ocr/tesseract/blob/5.5.3/nsis/build.sh).

## Findings for the release

| Component | Observed version | Disposition |
| --- | --- | --- |
| Tesseract CLI | 5.5.3, x64, Apache-2.0 source | Separate executable using local files and command-line arguments. |
| libpng | 1.6.58 | In the affected range for CVE-2026-46675. The PDF adapter supplies its own RGB PNG with only IHDR, pHYs, IDAT and IEND, so the ancillary chunks named by this advisory are absent from the OCR input. Pin at least 1.6.59 for the release build and complete the dependency review. |
| libwebp | 1.6.0 | The old CVE-2023-4863 version range ends before 1.3.2. |
| libtiff → JBIG-KIT | TIFF 4.7.2, JBIG-KIT 2.1 | The official installer links this GPL-licensed dependency transitively. Exact corresponding source and license obligations must be resolved before redistribution of this bundle. |
| libarchive, libcurl, XZ | 3.8.8, 8.21.0, 5.8.3 | Review exact upstream component advisories and notices during release acceptance. |

References: [libpng maintainer advisory](https://github.com/pnggroup/libpng/security/advisories/GHSA-qvg3-h654-xq3j), [WebP advisory](https://nvd.nist.gov/vuln/detail/CVE-2023-4863), [JBIG-KIT licensing](https://www.cl.cam.ac.uk/~mgk25/jbigkit/), [GNU GPL FAQ on separate executables and corresponding source](https://www.gnu.org/licenses/gpl-faq.html), [MSYS2 notices](https://www.msys2.org/license/).

The local acceptance assembly has a separate filename and workflow. Public
packaging uses the Windows static Tesseract build, resolves its exact
dependency notices and requires the clean Windows acceptance record. It pins
vcpkg 2026.10.16, Tesseract 5.5.3 and libpng 1.6.59, and checks the installed
port list for JBIG dependencies. The Windows recipe still needs execution
and acceptance. T-013,
T-019, T-022 and T-026 remain open on Windows.
