#!/usr/bin/env bash
set -euo pipefail

PROJECT="src/MusicDownloader/MusicDownloader.csproj"
APP_NAME="MusicDownloader"
DIST_DIR="dist"

VERSION="${VERSION:-$(grep -oE '<Version>[^<]+' "$PROJECT" | sed 's/<Version>//')}"

usage() {
    cat <<USAGE
Usage: $0 [rid...]

Build self-contained single-file binaries and package them as zips under ./dist.

If no RIDs are passed, defaults to all three: win-x64 osx-arm64 osx-x64.

Examples:
    $0                       # build all three
    $0 osx-arm64             # only Apple Silicon
    VERSION=0.2.0 $0 win-x64 # override version used in zip name
USAGE
}

if [[ "${1:-}" == "-h" || "${1:-}" == "--help" ]]; then
    usage
    exit 0
fi

RIDS=("$@")
if [[ ${#RIDS[@]} -eq 0 ]]; then
    RIDS=(win-x64 osx-arm64 osx-x64)
fi

mkdir -p "$DIST_DIR"

publish_one() {
    local rid="$1"
    local out_dir="src/MusicDownloader/bin/Release/net10.0/${rid}/publish"
    local exe_name="$APP_NAME"
    [[ "$rid" == win-* ]] && exe_name="${APP_NAME}.exe"

    echo
    echo "==> Publishing $rid"
    rm -rf "$out_dir"
    dotnet publish "$PROJECT" \
        -c Release \
        -r "$rid" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        --nologo \
        -v minimal

    local zip_name="${APP_NAME}-${VERSION}-${rid}.zip"
    local zip_path="${DIST_DIR}/${zip_name}"
    rm -f "$zip_path"

    if [[ "$rid" == osx-* ]]; then
        chmod +x "${out_dir}/${exe_name}"
    fi

    (cd "$out_dir" && zip -9 -q "${OLDPWD}/${zip_path}" "$exe_name")
    echo "    -> $zip_path ($(du -h "$zip_path" | awk '{print $1}'))"
}

for rid in "${RIDS[@]}"; do
    publish_one "$rid"
done

echo
echo "Done. Artifacts in ./${DIST_DIR}:"
ls -lh "$DIST_DIR" | tail -n +2
