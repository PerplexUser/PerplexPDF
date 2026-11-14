#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 5 ]]; then
    printf 'Usage: %s LIBPDFIUM_SO PDFIUM_LICENSE_DIR TESSDATA_DIR TESSDATA_LICENSE OUTPUT_TAR_GZ\n' "$0" >&2
    exit 2
fi
pdfium=$(realpath -- "$1")
pdfium_notices=$(realpath -- "$2")
tessdata=$(realpath -- "$3")
tessdata_license=$(realpath -- "$4")
output=$(realpath -m -- "$5")
root=$(cd -- "$(dirname -- "$0")/.." && pwd -P)
sdk=$(dirname -- "$(realpath -- "$(command -v dotnet)")")
runtime=10.0.15
[[ $(uname -s) == Linux && $(uname -m) == x86_64 && ! -e "$output" ]] ||
    { printf 'Build on x86-64 Linux and choose a new archive path.\n' >&2; exit 1; }
[[ -f "$pdfium" && -d "$pdfium_notices" && -f "$tessdata_license" &&
    -f "$sdk/shared/Microsoft.NETCore.App/$runtime/libcoreclr.so" &&
    -f "$sdk/host/fxr/$runtime/libhostfxr.so" ]] ||
    { printf 'The pinned Linux runtime or native input is missing.\n' >&2; exit 1; }

mkdir -p -- "$(dirname -- "$output")"
stage=$(mktemp -d "$(dirname -- "$output")/.perplexpdf-linux.XXXXXX")
trap 'rm -rf -- "$stage"' EXIT
package="$stage/PerplexPDF-1.0.0-linux-x64"
project="$root/src/PerplexPDF.App.Gtk/PerplexPDF.App.Gtk.csproj"
restore=()
if [[ -n "${PERPLEXPDF_NUGET_SOURCE:-}" ]]; then restore+=(--source "$PERPLEXPDF_NUGET_SOURCE"); fi
cd "$root"
dotnet restore "$project" "${restore[@]}" -p:PerplexPDFLinuxPackage=true -m:1
dotnet publish "$project" --no-restore -c Release -o "$package" -m:1 \
    -p:PerplexPDFLinuxPackage=true -p:DebugType=embedded -p:UseSharedCompilation=false

mkdir -p -- "$package/runtime/host/fxr" "$package/runtime/shared/Microsoft.NETCore.App" "$package/licenses/pdfium" \
    "$package/licenses/dotnet" "$package/tessdata/configs" "$package/examples"
cp -- "$sdk/dotnet" "$package/runtime/"
cp -a -- "$sdk/host/fxr/$runtime" "$package/runtime/host/fxr/"
cp -a -- "$sdk/shared/Microsoft.NETCore.App/$runtime" "$package/runtime/shared/Microsoft.NETCore.App/"
cp -- "$sdk/LICENSE.txt" "$sdk/ThirdPartyNotices.txt" "$package/licenses/dotnet/"
cp -- "$pdfium" "$package/libpdfium.so"
cp -a -- "$pdfium_notices/." "$package/licenses/pdfium/"
cp -- "$tessdata_license" "$package/licenses/Tessdata-Apache-2.0.txt"
cp -- "$tessdata/"{eng.traineddata,deu.traineddata,pdf.ttf} "$package/tessdata/"
cp -- "$tessdata/configs/pdf" "$package/tessdata/configs/"
cp -- "$root/packaging/install_linux.sh" "$package/install.sh"
cp -- "$root/docs/"{USER_GUIDE_EN.md,LINUX_BUILD.md,MACOS_BUILD.md} "$package/"
cp -- "$root/tests/Fixtures/"{example.pdf,word-layout.pdf,epub-structure.pdf,scanned-en-de.pdf,logo.png} "$package/examples/"
cat >> "$package/THIRD_PARTY_NOTICES.txt" <<'NOTICE'

Linux uses the distribution's GTK 3, Cairo, Pango, GdkPixbuf, qpdf and Tesseract.
The bundled PDFium and .NET runtime notices and Tesseract data license are in
licenses/. The Linux package uses system fonts through Pango and fontconfig.
NOTICE
(cd "$package" && find . -type f ! -name SHA256SUMS.txt -print0 | sort -z | xargs -0 sha256sum > SHA256SUMS.txt)
env -u DOTNET_ROOT -u DOTNET_ROOT_X64 "$package/PerplexPDF" --version
tar -C "$stage" -czf "$stage/archive.tar.gz" PerplexPDF-1.0.0-linux-x64
mv -- "$stage/archive.tar.gz" "$output"
printf '%s\n' "$output"
