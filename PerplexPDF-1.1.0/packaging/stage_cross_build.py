"""Package an offline Windows x64 build for testing with the supplied PDF tools."""

import argparse
from hashlib import file_digest
from pathlib import Path, PurePosixPath
import shutil
import struct
import tarfile
from tempfile import TemporaryDirectory
from zipfile import ZIP_DEFLATED, ZipFile

from local_ocr import copy_local_ocr

REPO = Path(__file__).resolve().parents[1]
PDFIUM = "8d48973e3eb472dda0f5f2435e595ac043f9f2219dd71883392f549445afa8a5"
QPDF = "db87077e683630c1217e0e8f9a20a9749d952ab676e881c3689187763a5de25d"
RUNTIME = "10.0.14"
SKIA = "3.119.2"
QPDF_BIN = "qpdf-12.4.2-msvc64/bin/"


def digest(path):
    with path.open("rb") as stream:
        return sha256_stream(stream)


def sha256_stream(stream):
    return file_digest(stream, "sha256").hexdigest()


def require_hash(path, expected):
    if digest(path) != expected:
        raise ValueError(f"Unexpected SHA-256 for {path.name}")


def copy_stream(archive, name, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    with archive.open(name) as source, target.open("xb") as output:
        shutil.copyfileobj(source, output)


def require_x64(path):
    with path.open("rb") as stream:
        header = stream.read(64)
        if len(header) != 64 or header[:2] != b"MZ":
            raise ValueError(f"Missing PE header: {path.name}")
        stream.seek(struct.unpack_from("<I", header, 60)[0])
        if stream.read(6) != b"PE\0\0\x64\x86":
            raise ValueError(f"Expected x64 Windows binary: {path.name}")


def copy_pdfium(archive_path, staged):
    require_hash(archive_path, PDFIUM)
    with tarfile.open(archive_path, "r:gz") as archive:
        for name, output in (
            ("bin/pdfium.dll", staged / "tools/pdfium/pdfium.dll"),
            ("LICENSE", staged / "licenses/pdfium/LICENSE"),
            ("VERSION", staged / "licenses/pdfium/VERSION"),
            ("args.gn", staged / "licenses/pdfium/args.gn"),
        ):
            member = archive.getmember(name)
            if not member.isfile():
                raise ValueError(f"Invalid PDFium archive member: {name}")
            output.parent.mkdir(parents=True, exist_ok=True)
            with archive.extractfile(member) as source, output.open("xb") as destination:
                shutil.copyfileobj(source, destination)
    require_x64(staged / "tools/pdfium/pdfium.dll")


def copy_qpdf(archive_path, staged):
    require_hash(archive_path, QPDF)
    binary_directory = staged / "tools/qpdf"
    licenses = staged / "licenses/qpdf"
    with ZipFile(archive_path) as archive:
        for name in archive.namelist():
            item = PurePosixPath(name)
            if name.startswith(QPDF_BIN) and (item.name == "qpdf.exe" or item.suffix.lower() == ".dll"):
                copy_stream(archive, name, binary_directory / item.name)
            if "/_sources/" in name and item.name in ("license.rst.txt", "acknowledgement.rst.txt"):
                copy_stream(archive, name, licenses / item.name)
    for name in ("qpdf.exe", "qpdf30.dll", "msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll"):
        require_x64(binary_directory / name)
    # The remaining files are license texts and origin notes, not additional binaries.
    shutil.copytree(REPO / "licenses/qpdf-test", licenses, dirs_exist_ok=True)


def copy_package_notices(nuget_cache, staged):
    for package, version in (
        ("microsoft.netcore.app.runtime.win-x64", RUNTIME),
        ("microsoft.windowsdesktop.app.runtime.win-x64", RUNTIME),
        ("skiasharp", SKIA),
        ("skiasharp.nativeassets.win32", SKIA),
    ):
        source = nuget_cache / package / version
        files = [p for p in source.iterdir() if p.is_file() and
                 any(word in p.name.lower() for word in ("license", "notice", "copyright"))]
        if not files:
            raise ValueError(f"Missing notices from {package} {version}")
        destination = staged / "licenses" / f"{package}-{version}"
        destination.mkdir(parents=True)
        for path in files:
            shutil.copyfile(path, destination / path.name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish", type=Path, required=True)
    parser.add_argument("--inputs", type=Path, required=True)
    parser.add_argument("--nuget-cache", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--local-ocr", action="store_true",
                        help="Include the supplied Tesseract payload for the owner's local Windows acceptance.")
    args = parser.parse_args()
    if args.local_ocr and not args.output.name.endswith("-ocr-local-testbuild.zip"):
        parser.error("Use an -ocr-local-testbuild.zip filename for the local OCR package.")
    require_x64(args.publish / "PerplexPDF.exe")
    require_x64(args.publish / "libSkiaSharp.dll")
    for name in ("PerplexPDF.dll", "PerplexPDF.deps.json", "PerplexPDF.runtimeconfig.json",
                 "hostfxr.dll", "hostpolicy.dll", "coreclr.dll", "PresentationFramework.dll", "PresentationCore.dll"):
        if not (args.publish / name).is_file():
            raise ValueError(f"Missing startup file: {name}")
    args.output.parent.mkdir(parents=True, exist_ok=True)

    with TemporaryDirectory(dir=args.output.parent) as work:
        staged = Path(work) / "payload"
        shutil.copytree(args.publish, staged, ignore=shutil.ignore_patterns("*.pdb"))
        copy_pdfium(args.inputs / "pdfium-win-x64.tgz", staged)
        copy_qpdf(args.inputs / "qpdf-12.4.2-msvc64.zip", staged)
        copy_package_notices(args.nuget_cache, staged)
        if args.local_ocr:
            copy_local_ocr(args.inputs, staged)
            for path in (staged / "tools/tesseract").iterdir():
                if path.suffix.lower() in (".exe", ".dll"):
                    require_x64(path)
            shutil.copyfile(REPO / "tests/Fixtures/scanned-en-de.pdf", staged / "ocr-scan-en-de.pdf")
            shutil.copyfile(REPO / "docs/TESSERACT_WINDOWS_AUDIT.md", staged / "TESSERACT_WINDOWS_AUDIT.md")
            shutil.copyfile(REPO / "packaging/verify_clean_vm.ps1", staged / "verify_clean_vm.ps1")
        shutil.copyfile(REPO / "licenses/DejaVu.txt", staged / "licenses/DejaVu.txt")
        shutil.copyfile(REPO / "tests/Fixtures/example.pdf", staged / "example.pdf")
        for name in ("USER_GUIDE_EN.md", "RELEASE_CHECKLIST.md", "TEST_RESULTS_LINUX.txt", "WINDOWS_CROSS_BUILD.md"):
            shutil.copyfile(REPO / "docs" / name, staged / name)
        readme = "README_OCR_LOCAL_TEST.txt" if args.local_ocr else "README_TEST_BUILD.txt"
        shutil.copyfile(REPO / "docs" / readme, staged / "README.txt")
        shutil.copyfile(REPO / "DEPENDENCIES.md", staged / "DEPENDENCIES.md")
        for name in ("Setup-PerplexPDF.cmd", "Setup-PerplexPDF.ps1"):
            shutil.copyfile(REPO / "packaging" / name, staged / name)

        if args.local_ocr:
            native = sorted(p for p in staged.rglob("*")
                            if p.is_file() and p.suffix.lower() in (".exe", ".dll", ".ttf", ".traineddata"))
            (staged / "NATIVE-SHA256SUMS.txt").write_text(
                "".join(f"{digest(path)}  {path.relative_to(staged).as_posix()}\n" for path in native),
                encoding="ascii",
            )
        paths = sorted(p for p in staged.rglob("*") if p.is_file())
        manifest = staged / "SHA256SUMS.txt"
        manifest.write_text("".join(f"{digest(path)}  {path.relative_to(staged).as_posix()}\n" for path in paths),
                            encoding="ascii")
        archive_path = Path(work) / args.output.name
        with ZipFile(archive_path, "w", ZIP_DEFLATED, compresslevel=7) as archive:
            for path in [*paths, manifest]:
                archive.write(path, path.relative_to(staged).as_posix())
        with ZipFile(archive_path) as archive:
            for row in archive.read("SHA256SUMS.txt").decode("ascii").splitlines():
                expected, name = row.split("  ", 1)
                with archive.open(name) as member:
                    if sha256_stream(member) != expected:
                        raise ValueError(f"Corrupt ZIP member: {name}")
        archive_path.replace(args.output)
    print(f"{digest(args.output)}  {args.output.name}")


if __name__ == "__main__":
    main()
