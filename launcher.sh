#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION="$SCRIPT_DIR/UAssetViewer.slnx"
GODOT_PROJECT="$SCRIPT_DIR/godot"
SVELTE_UI="$SCRIPT_DIR/svelte-ui"
CEF_HELPER="$SCRIPT_DIR/cef-helper"
CEF_DIR="$SCRIPT_DIR/cef"
ENV_FILE="$SCRIPT_DIR/.launcher.env"

CEF_VERSION="87.1.14+ga29e9a3+chromium-87.0.4280.141"
CEF_TARBALL="cef_binary_${CEF_VERSION}_linux64_minimal.tar.bz2"
CEF_URL="https://cef-builds.spotifycdn.com/${CEF_TARBALL}"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log()   { echo -e "${GREEN}[launcher]${NC} $*"; }
warn()  { echo -e "${YELLOW}[launcher]${NC} $*"; }
error() { echo -e "${RED}[launcher]${NC} $*" >&2; }

# Load saved settings
if [[ -f "$ENV_FILE" ]]; then
    source "$ENV_FILE"
fi

usage() {
    echo "Usage: $0 <command> [args]"
    echo ""
    echo "Commands:"
    echo "  install          Install all dependencies (NuGet packages, npm modules)"
    echo "  build            Compile the project from source"
    echo "  run              Build and launch the application in Godot"
    echo "  set-godot <path> Set the path to the Godot binary or directory"
    echo "  setup-cef        Download and extract CEF native binaries for Linux"
    echo ""
    echo "Examples:"
    echo "  $0 set-godot /path/to/Godot_v4.6-stable_mono_linux_x86_64/"
    echo "  $0 setup-cef"
    echo "  $0 install build run"
}

check_dotnet() {
    if ! command -v dotnet &>/dev/null; then
        error "dotnet SDK not found. Install .NET SDK 8.0+ from https://dotnet.microsoft.com/download"
        exit 1
    fi
    log "Using dotnet: $(dotnet --version)"
}

check_node() {
    if ! command -v node &>/dev/null; then
        warn "Node.js not found - skipping Svelte UI install"
        return 1
    fi
    log "Using node: $(node --version)"
    return 0
}

resolve_godot_binary() {
    local path="$1"

    # Strip trailing slash
    path="${path%/}"

    # If it's a direct binary, use it
    if [[ -f "$path" && -x "$path" ]]; then
        echo "$path"
        return 0
    fi

    # If it's a directory, look for the Godot binary inside
    if [[ -d "$path" ]]; then
        for candidate in "$path"/Godot_* "$path"/godot "$path"/godot4; do
            if [[ -f "$candidate" && -x "$candidate" ]]; then
                echo "$candidate"
                return 0
            fi
        done
        error "No Godot binary found in directory: $path"
        error "Expected an executable like Godot_v4.x-stable_mono_linux_x86_64"
        return 1
    fi

    error "Path does not exist: $path"
    return 1
}

find_godot() {
    # 1. Check saved GODOT_PATH from .launcher.env
    if [[ -n "${GODOT_PATH:-}" ]]; then
        local resolved
        if resolved=$(resolve_godot_binary "$GODOT_PATH"); then
            echo "$resolved"
            return 0
        fi
        warn "Saved GODOT_PATH is invalid: $GODOT_PATH"
    fi

    # 2. Check common Godot binary names in PATH
    local candidates=(
        "godot4"
        "godot"
        "godot-mono"
    )

    for cmd in "${candidates[@]}"; do
        if command -v "$cmd" &>/dev/null; then
            echo "$cmd"
            return 0
        fi
    done

    return 1
}

cmd_set_godot() {
    local path="$1"

    # Resolve to actual binary to validate
    local resolved
    if ! resolved=$(resolve_godot_binary "$path"); then
        exit 1
    fi

    # Save the path (use what the user gave, resolve at runtime)
    # Resolve to absolute path
    local abs_path
    abs_path="$(cd "$(dirname "$resolved")" && pwd)/$(basename "$resolved")"

    echo "GODOT_PATH=\"$abs_path\"" > "$ENV_FILE"
    log "Godot path saved: $abs_path"
    log "Settings written to: $ENV_FILE"
}

cmd_install() {
    log "Installing dependencies..."

    check_dotnet

    # Restore NuGet packages for the full solution
    log "Restoring NuGet packages..."
    dotnet restore "$SOLUTION"
    log "NuGet packages restored."

    # Install Svelte UI dependencies if Node.js is available
    if check_node; then
        if [[ -f "$SVELTE_UI/package.json" ]]; then
            log "Installing Svelte UI dependencies..."
            (cd "$SVELTE_UI" && npm install --legacy-peer-deps)
            log "Svelte UI dependencies installed."
        fi
    fi

    log "All dependencies installed."
}

cmd_build() {
    log "Building project from source..."

    check_dotnet

    # Build the full solution
    log "Building solution: $SOLUTION"
    dotnet build "$SOLUTION" --configuration Release

    # Build Svelte UI if Node.js is available
    if check_node; then
        if [[ -f "$SVELTE_UI/package.json" ]]; then
            log "Building Svelte UI..."
            (cd "$SVELTE_UI" && npm run build)
            log "Svelte UI built."
        fi
    fi

    log "Build complete."
}

cmd_setup_cef() {
    log "Setting up CEF native binaries (CEF $CEF_VERSION)..."

    if [[ -f "$CEF_DIR/Release/libcef.so" ]]; then
        log "CEF binaries already present at: $CEF_DIR/Release/"
        return 0
    fi

    if ! command -v curl &>/dev/null; then
        error "curl is required for downloading CEF binaries"
        exit 1
    fi

    mkdir -p "$CEF_DIR"

    local tarball="$CEF_DIR/$CEF_TARBALL"

    if [[ -f "$tarball" ]]; then
        log "Using cached download: $tarball"
    else
        log "Downloading CEF binaries (~260 MB)..."
        log "URL: $CEF_URL"
        curl -L --progress-bar -o "$tarball" "$CEF_URL"
    fi

    log "Extracting CEF binaries..."
    tar -xjf "$tarball" -C "$CEF_DIR" --strip-components=1

    if [[ -f "$CEF_DIR/Release/libcef.so" ]]; then
        log "CEF binaries extracted to: $CEF_DIR/Release/"
        log "You can delete the tarball to save space:"
        log "  rm \"$tarball\""
    else
        error "Extraction failed - libcef.so not found in $CEF_DIR/Release/"
        exit 1
    fi
}

cmd_run() {
    local godot_bin
    if ! godot_bin=$(find_godot); then
        error "Godot (Mono/.NET) not found."
        error "Set it with: $0 set-godot /path/to/Godot_v4.x-stable_mono_linux_x86_64/"
        exit 1
    fi

    # Set CEF_PATH if native binaries are present
    if [[ -d "$CEF_DIR/Release" && -f "$CEF_DIR/Release/libcef.so" ]]; then
        export CEF_PATH="$CEF_DIR/Release"
        log "CEF_PATH=$CEF_PATH"
    elif [[ -z "${CEF_PATH:-}" ]]; then
        warn "CEF binaries not found. Run '$0 setup-cef' to download them."
        warn "The app will launch but CEF features will be unavailable."
    fi

    # Add CEF Release dir to library path so libcef.so is found
    if [[ -n "${CEF_PATH:-}" ]]; then
        export LD_LIBRARY_PATH="${CEF_PATH}:${LD_LIBRARY_PATH:-}"
    fi

    # Set CEF helper binary path
    local helper_bin="$CEF_HELPER/bin/Release/net8.0/CefHelper"
    if [[ ! -f "$helper_bin" ]]; then
        helper_bin="$CEF_HELPER/bin/Debug/net8.0/CefHelper"
    fi
    if [[ -f "$helper_bin" ]]; then
        export CEF_HELPER_PATH="$helper_bin"
        log "CEF_HELPER_PATH=$CEF_HELPER_PATH"
    else
        warn "CefHelper binary not found. Run '$0 build' first."
    fi

    log "Launching with: $godot_bin"
    exec "$godot_bin" --path "$GODOT_PROJECT"
}

if [[ $# -eq 0 ]]; then
    usage
    exit 1
fi

# Parse commands - set-godot takes the next arg
args=("$@")
i=0
while [[ $i -lt ${#args[@]} ]]; do
    case "${args[$i]}" in
        set-godot)
            i=$((i + 1))
            if [[ $i -ge ${#args[@]} ]]; then
                error "set-godot requires a path argument"
                echo "  Usage: $0 set-godot /path/to/godot"
                exit 1
            fi
            cmd_set_godot "${args[$i]}"
            ;;
        install)   cmd_install ;;
        build)     cmd_build ;;
        run)       cmd_run ;;
        setup-cef) cmd_setup_cef ;;
        -h|--help|help) usage; exit 0 ;;
        *)
            error "Unknown command: ${args[$i]}"
            usage
            exit 1
            ;;
    esac
    i=$((i + 1))
done
