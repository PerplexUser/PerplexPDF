#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 3 ]]; then
    printf 'Usage: %s TESSDATA_DIR TESSDATA_LICENSE OUTPUT_ZIP\n' "$0" >&2
    exit 2
fi

tessdata=$(cd -- "$1" && pwd -P)
license=$(cd -- "$(dirname -- "$2")" && pwd -P)/$(basename -- "$2")
mkdir -p -- "$(dirname -- "$3")"
output=$(cd -- "$(dirname -- "$3")" && pwd -P)/$(basename -- "$3")
root=$(cd -- "$(dirname -- "$0")/.." && pwd -P)
[[ -f "$license" && ! -e "$output" ]] || { printf 'Supply the model license and a new ZIP path.\n' >&2; exit 1; }
sdk=$(python3 -c 'from pathlib import Path; import sys; print(Path(sys.argv[1]).resolve().parent)' "$(command -v dotnet)")
stage=$(mktemp -d "$(dirname -- "$output")/.perplexpdf-macos.XXXXXX")
trap 'rm -rf -- "$stage"' EXIT
package="$stage/PerplexPDF-1.0.0-macOS"
project="$root/src/PerplexPDF.App.Gtk/PerplexPDF.App.Gtk.csproj"
host="$root/packaging/MacAppHost/MacAppHost.csproj"
set --
if [[ -n "${PERPLEXPDF_NUGET_SOURCE:-}" ]]; then set -- --source "$PERPLEXPDF_NUGET_SOURCE"; fi

cd "$root"
dotnet restore "$project" "$@" -p:UseAppHost=false -m:1
dotnet restore "$host" "$@" -m:1
dotnet publish "$project" --no-restore -c Release -o "$package/payload" -m:1 \
    -p:UseAppHost=false -p:DebugType=embedded -p:UseSharedCompilation=false
dotnet publish "$host" --no-restore -c Release -o "$package/setup/host" -m:1 \
    -p:DebugType=embedded -p:UseSharedCompilation=false

mkdir -p -- "$package/payload/tessdata/configs" "$package/payload/licenses" "$package/setup/host/licenses" "$package/examples"
cp -- packaging/macos/{PerplexPDF-Setup.command,START-HIER.txt} "$package/"
cp -- packaging/macos/{Info.plist,dependencies.sh} "$package/setup/"
cp -- packaging/macos/{PerplexPDF.icns,PerplexPDF.png} "$package/payload/"
cp -- "$sdk"/{LICENSE.txt,ThirdPartyNotices.txt} "$package/setup/host/licenses/"
cp -- "$tessdata"/{eng.traineddata,deu.traineddata,pdf.ttf} "$package/payload/tessdata/"
cp -- "$tessdata/configs/pdf" "$package/payload/tessdata/configs/"
cp -- "$license" "$package/payload/licenses/Tessdata-Apache-2.0.txt"
cp -- tests/Fixtures/{example.pdf,word-layout.pdf,epub-structure.pdf,scanned-en-de.pdf,logo.png} "$package/examples/"
cp -- docs/{USER_GUIDE_EN.md,MACOS_BUILD.md,LINUX_BUILD.md,CHANGELOG.md} "$package/"
cat >> "$package/payload/THIRD_PARTY_NOTICES.txt" <<'NOTICE'

macOS uses the Homebrew GTK 3, Cairo, Pango, GdkPixbuf, qpdf and Tesseract packages.
The per-user setup verifies the .NET runtime and PDFium downloads and installs
their accompanying license notices. English and German OCR data are bundled.
NOTICE

python3 - "$package" "$stage/archive.zip" <<'PY'
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib
import sys

package, output = map(Path, sys.argv[1:])
paths = sorted(path for path in package.rglob("*") if path.is_file())
lines = []
for path in paths:
    with path.open("rb") as stream:
        checksum = hashlib.file_digest(stream, "sha256").hexdigest()
    lines.append(f"{checksum}  {path.relative_to(package).as_posix()}\n")
manifest = package / "SHA256SUMS.txt"
manifest.write_text("".join(lines), encoding="utf-8")
with ZipFile(output, "x", ZIP_DEFLATED, compresslevel=6) as archive:
    for path in [*paths, manifest]:
        archive.write(path, path.relative_to(package.parent).as_posix())
PY
mv -- "$stage/archive.zip" "$output"
printf '%s\n' "$output"
