#!/bin/bash
set -euo pipefail
umask 077

fail() { printf '\nPerplexPDF-Setup: %s\n' "$*" >&2; exit 1; }
[[ $(/usr/bin/uname -s) == Darwin ]] || fail "Dieses Setup wird auf einem Mac ausgeführt."
here=$(cd -- "$(dirname -- "$0")" && pwd -P)
architecture=$(/usr/bin/uname -m)
if [[ "$architecture" == x86_64 && $(/usr/sbin/sysctl -in sysctl.proc_translated) == 1 ]]; then
    exec /usr/bin/arch -arm64 /bin/bash "$here/PerplexPDF-Setup.command" "$@"
fi
version=$(/usr/bin/sw_vers -productVersion)
[[ ${version%%.*} -ge 14 ]] || fail "PerplexPDF benötigt macOS 14 oder neuer."
[[ $# -eq 0 ]] || fail "Starte PerplexPDF-Setup.command ohne Argumente."

printf 'PerplexPDF 1.0.0 für macOS (%s)\n' "$architecture"
(cd "$here" && /usr/bin/shasum --quiet -a 256 -c SHA256SUMS.txt) ||
    fail "Das Archiv ist unvollständig. Entpacke die ZIP erneut vollständig."
source "$here/setup/dependencies.sh" || fail "Diese Prozessorarchitektur wird nicht unterstützt."

if [[ "$architecture" == arm64 ]]; then
    brew=/opt/homebrew/bin/brew
else
    brew=/usr/local/bin/brew
fi
if [[ ! -x "$brew" ]]; then
    brew=$(command -v brew || true)
fi
[[ -n "$brew" && -x "$brew" ]] || fail "Installiere zuerst Homebrew von https://brew.sh/ und starte dieses Setup erneut."

apps="$HOME/Applications"
target="$apps/PerplexPDF.app"
check_target() {
    if [[ -e "$target" || -L "$target" ]]; then
        [[ ! -L "$target" && -d "$target" ]] || fail "Der Zielpfad ist belegt: $target"
        local existing
        existing=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$target/Contents/Info.plist" 2>/dev/null || true)
        [[ "$existing" == click.perplex.PerplexPDF ]] || fail "Der Zielpfad ist belegt: $target"
    fi
    if /usr/bin/pgrep -u "$(/usr/bin/id -u)" -x PerplexPDF >/dev/null; then
        fail "Beende PerplexPDF vor dem Update."
    fi
}
check_target

for formula in gtk+3 qpdf tesseract; do
    if ! "$brew" list --versions "$formula" >/dev/null 2>&1; then set -- "$@" "$formula"; fi
done
if [[ $# -gt 0 ]]; then
    printf 'Homebrew installiert %s.\n' "$*"
    HOMEBREW_NO_AUTO_UPDATE=1 HOMEBREW_NO_INSTALL_CLEANUP=1 "$brew" install "$@"
fi
brew_prefix=$("$brew" --prefix)
[[ "$brew_prefix" == /* && -x "$brew_prefix/bin/qpdf" && -x "$brew_prefix/bin/tesseract" ]] ||
    fail "Die Homebrew-Installation von qpdf oder Tesseract ist unvollständig."

/bin/mkdir -p "$apps"
stage=$(/usr/bin/mktemp -d "$apps/.perplexpdf-setup.XXXXXX")
lock="$apps/.perplexpdf-install-lock"
locked=0
cleanup() {
    if [[ -e "$stage/previous.app" && ! -e "$target" ]]; then
        if ! /bin/mv -- "$stage/previous.app" "$target"; then
            printf 'Die bisherige App liegt hier: %s\n' "$stage/previous.app" >&2
            if [[ $locked == 1 ]]; then /bin/rmdir "$lock"; fi
            return
        fi
    fi
    /bin/rm -rf -- "$stage"
    if [[ $locked == 1 ]]; then /bin/rmdir "$lock"; fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

download() {
    local url=$1 sha=$2 bits=$3 output=$4 actual
    local cached="${PERPLEXPDF_MACOS_DOWNLOAD_CACHE:-}/${url##*/}"
    if [[ -n "${PERPLEXPDF_MACOS_DOWNLOAD_CACHE:-}" && -f "$cached" ]]; then
        /bin/cp -- "$cached" "$output"
    else
        /usr/bin/curl -q --fail --location --silent --show-error --proto '=https' --proto-redir '=https' \
            --tlsv1.2 --connect-timeout 20 --max-time 300 --retry 2 --output "$output" "$url"
    fi
    actual=$(/usr/bin/shasum -a "$bits" "$output")
    [[ "${actual%% *}" == "$sha" ]] || fail "Prüfsumme stimmt nicht: ${url##*/}"
}

printf 'Lade und prüfe .NET und PDFium für %s.\n' "$rid"
download "$runtime_url" "$runtime_sha" 512 "$stage/runtime.tar.gz"
download "$host_url" "$host_sha" 512 "$stage/apphost.tar.gz"
download "$pdfium_url" "$pdfium_sha" 256 "$stage/pdfium.whl"

candidate="$stage/PerplexPDF.app"
contents="$candidate/Contents"
resources="$contents/Resources"
frameworks="$contents/Frameworks"
runtime="$resources/runtime"
launcher="$contents/MacOS/PerplexPDF"
/bin/mkdir -p "$contents/MacOS" "$frameworks" "$runtime" "$stage/host-pack"
/bin/cp -- "$here/setup/Info.plist" "$contents/Info.plist"
/bin/cp -R -- "$here/payload/." "$resources/"
printf '%s\n' "$brew_prefix" > "$resources/brew-prefix.txt"
/usr/bin/tar -xzf "$stage/runtime.tar.gz" -C "$runtime"
/usr/bin/tar -xzf "$stage/apphost.tar.gz" -C "$stage/host-pack"
template=$(/usr/bin/find "$stage/host-pack" -type f -name apphost -print)
[[ -f "$template" && -x "$runtime/dotnet" ]] || fail "Das .NET-AppHost-Archiv konnte nicht verarbeitet werden."

/usr/bin/unzip -p "$stage/pdfium.whl" pypdfium2_raw/libpdfium.dylib > "$frameworks/libpdfium.dylib"
/usr/bin/unzip -q "$stage/pdfium.whl" 'pypdfium2-5.14.0.dist-info/licenses/*' -d "$resources/licenses/pdfium"
"$runtime/dotnet" "$here/setup/host/MacAppHost.dll" "$template" "$launcher"
/bin/rm -- "$runtime/dotnet"
/bin/chmod 755 "$launcher"

# Keep native code in Frameworks and the .NET runtime's file layout in Resources.
/usr/bin/find "$runtime" -type f -name '*.dylib' -print0 | while IFS= read -r -d '' library; do
    name=${library##*/}
    relative=${library#"$runtime/"}
    parent=../../
    while [[ "$relative" == */* ]]; do
        relative=${relative#*/}
        parent=../$parent
    done
    [[ ! -e "$frameworks/$name" ]] || fail "Doppelte Laufzeitbibliothek: $name"
    /bin/mv -- "$library" "$frameworks/$name"
    /bin/ln -s "${parent}Frameworks/$name" "$library"
done
for library in "$frameworks/"*.dylib; do
    if ! /usr/bin/codesign --verify --strict "$library" >/dev/null 2>&1; then
        /usr/bin/codesign --force --sign - --timestamp=none "$library"
    fi
done
/usr/bin/plutil -lint "$contents/Info.plist"
/usr/bin/codesign --force --sign - --timestamp=none "$candidate"
/usr/bin/codesign --verify --strict "$candidate"

printf 'Prüfe PDF-Textbearbeitung, Word, EPUB und OCR auf diesem Mac.\n'
if ! /usr/bin/env -u PERPLEXPDF_PDFIUM -u PERPLEXPDF_TESSDATA -u PERPLEXPDF_QPDF -u PERPLEXPDF_TESSERACT \
        "$launcher" --self-test "$here/examples" "$stage/checks"; then
    fail "Die Funktionsprüfung ist fehlgeschlagen. Die Ausgabe oben enthält den Fehler."
fi

/bin/mkdir "$lock" || fail "Eine andere Einrichtung belegt den Anwendungsordner: $lock"
locked=1
check_target
if [[ -e "$target" ]]; then /bin/mv -- "$target" "$stage/previous.app"; fi
/bin/mv -- "$candidate" "$target"
printf '\nPerplexPDF wurde installiert. Öffne im Finder:\n%s\n' "$target"
/usr/bin/open "$target"
