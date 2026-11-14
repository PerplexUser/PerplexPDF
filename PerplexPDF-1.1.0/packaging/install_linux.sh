#!/usr/bin/env bash
set -euo pipefail

here=$(cd -- "$(dirname -- "$0")" && pwd -P)
data_home=$(realpath -m -- "${XDG_DATA_HOME:-$HOME/.local/share}")
target="$data_home/perplexpdf"
desktop="$data_home/applications/perplexpdf.desktop"
icon="$data_home/icons/hicolor/scalable/apps/perplexpdf.svg"
mime="$data_home/mime/packages/perplexpdf.xml"
marker=".perplexpdf-managed-install"

refresh() {
    if command -v update-desktop-database >/dev/null && [[ -d "$data_home/applications" ]]; then
        update-desktop-database "$data_home/applications"
    fi
    if command -v update-mime-database >/dev/null && [[ -d "$data_home/mime" ]]; then
        update-mime-database "$data_home/mime"
    fi
}

if [[ -L "$target" || ( -e "$target" && ! -f "$target/$marker" ) ]]; then
    printf 'The install directory is already in use: %s\n' "$target" >&2
    exit 1
fi

if [[ ${1:-} == --uninstall ]]; then
    if [[ -f "$target/$marker" ]]; then
        rm -rf -- "$target"
        rm -f -- "$desktop" "$icon" "$mime"
        refresh
        printf 'PerplexPDF removed. Document and recovery files are retained.\n'
    fi
    exit 0
fi

[[ $# -eq 0 ]] || { printf 'Usage: %s [--uninstall]\n' "$0" >&2; exit 2; }
[[ -x "$here/PerplexPDF" && -f "$here/SHA256SUMS.txt" ]] ||
    { printf 'Extract the complete PerplexPDF archive before installing.\n' >&2; exit 1; }
(cd "$here" && sha256sum --quiet -c SHA256SUMS.txt)
mkdir -p -- "$data_home" "$(dirname -- "$desktop")" "$(dirname -- "$icon")" "$(dirname -- "$mime")"

if [[ "$here" != "$target" ]]; then
    stage=$(mktemp -d "$data_home/.perplexpdf-stage.XXXXXX")
    backup=
    cleanup() {
        if [[ -n "$backup" && -d "$backup/previous" && ! -e "$target" ]]; then
            mv -- "$backup/previous" "$target"
        fi
        if [[ -d "$stage" ]]; then rm -rf -- "$stage"; fi
        if [[ -n "$backup" && -d "$backup" ]]; then rm -rf -- "$backup"; fi
    }
    trap cleanup EXIT
    cp -a -- "$here/." "$stage/"
    printf 'PerplexPDF user install\n' > "$stage/$marker"
    if [[ -d "$target" ]]; then
        backup=$(mktemp -d "$data_home/.perplexpdf-backup.XXXXXX")
        mv -- "$target" "$backup/previous"
    fi
    mv -- "$stage" "$target"
fi

executable="$target/PerplexPDF"
case "$executable" in
    *$'\n'*|*$'\r'*) printf 'The install path cannot contain line breaks.\n' >&2; exit 1 ;;
esac
# Desktop Entry string escaping, followed by quoting for Exec.
executable=${executable//\\/\\\\\\\\}
executable=${executable//\"/\\\\\"}
executable=${executable//\$/\\\\\$}
executable=${executable//\`/\\\\\`}
executable=${executable//%/%%}
cat > "$desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=PerplexPDF
Comment=Edit and convert PDF documents locally
Exec="$executable" %F
Icon=perplexpdf
Terminal=false
Categories=Office;Viewer;
MimeType=application/pdf;application/x-perplexpdf-project;
StartupWMClass=PerplexPDF
DESKTOP
cp -- "$target/PerplexPDF.svg" "$icon"
cat > "$mime" <<'MIME'
<?xml version="1.0" encoding="UTF-8"?>
<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
  <mime-type type="application/x-perplexpdf-project">
    <comment>PerplexPDF project</comment>
    <sub-class-of type="application/zip"/>
    <glob pattern="*.ppdf"/>
  </mime-type>
</mime-info>
MIME
refresh
printf 'PerplexPDF is available in the application menu. Install location: %s\n' "$target"
