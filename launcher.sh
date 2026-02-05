#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION="$SCRIPT_DIR/UAssetViewer.slnx"
GODOT_PROJECT="$SCRIPT_DIR/godot"
SVELTE_UI="$SCRIPT_DIR/svelte-ui"
CEF_GDEXT="$SCRIPT_DIR/cef-gdext"
CEF_HELPER_RS="$SCRIPT_DIR/cef-helper-rs"
CEF_BIN="$GODOT_PROJECT/bin"
ENV_FILE="$SCRIPT_DIR/.launcher.env"

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

SVELTE_PID_FILE="$SCRIPT_DIR/.svelte-dev.pid"

usage() {
    echo "Usage: $0 <command> [args]"
    echo ""
    echo "Commands:"
    echo "  install          Install all dependencies (NuGet packages, npm modules)"
    echo "  build            Compile the project from source"
    echo "  run              Launch the application (starts Svelte dev server automatically)"
    echo "  dev              Start only the Svelte dev server"
    echo "  stop             Stop the Svelte dev server"
    echo "  set-godot <path> Set the path to the Godot binary or directory"
    echo ""
    echo "Examples:"
    echo "  $0 set-godot /path/to/Godot_v4.6-stable_mono_linux_x86_64/"
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

check_rust() {
    if ! command -v cargo &>/dev/null; then
        error "Rust/cargo not found. Install from https://rustup.rs"
        exit 1
    fi
    log "Using cargo: $(cargo --version)"
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
    check_rust

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
    check_rust

    # Build Rust GDExtension and helper binary
    log "Building Rust CEF GDExtension..."
    (cd "$CEF_GDEXT" && cargo build --release)
    log "CEF GDExtension built."

    log "Building Rust CEF helper binary..."
    (cd "$CEF_HELPER_RS" && cargo build --release)
    log "CEF helper built."

    # Copy built artifacts to godot/bin/
    mkdir -p "$CEF_BIN"
    cp "$CEF_GDEXT/target/release/libcef_gdext.so" "$CEF_BIN/"
    cp "$CEF_HELPER_RS/target/release/cef-helper" "$CEF_BIN/"
    log "Copied GDExtension and helper to godot/bin/"

    # Copy CEF 143 runtime files from crate build output
    local cef_out
    cef_out="$(find "$CEF_GDEXT/target/release/build" -path "*/cef-dll-sys-*/out/cef_linux_x86_64" -type d | head -1)"
    if [[ -n "$cef_out" && -f "$cef_out/libcef.so" ]]; then
        log "Copying CEF 143 runtime from: $cef_out"
        cp -u "$cef_out"/libcef.so "$CEF_BIN/"
        cp -u "$cef_out"/libEGL.so "$CEF_BIN/" 2>/dev/null || true
        cp -u "$cef_out"/libGLESv2.so "$CEF_BIN/" 2>/dev/null || true
        cp -u "$cef_out"/libvk_swiftshader.so "$CEF_BIN/" 2>/dev/null || true
        cp -u "$cef_out"/libvulkan.so.1 "$CEF_BIN/" 2>/dev/null || true
        cp -u "$cef_out"/chrome-sandbox "$CEF_BIN/" 2>/dev/null || true
        cp -u "$cef_out"/vk_swiftshader_icd.json "$CEF_BIN/" 2>/dev/null || true
        cp -u "$cef_out"/icudtl.dat "$CEF_BIN/"
        cp -u "$cef_out"/v8_context_snapshot.bin "$CEF_BIN/"
        cp -u "$cef_out"/*.pak "$CEF_BIN/"
        cp -rn "$cef_out"/locales "$CEF_BIN/" 2>/dev/null || true
        log "CEF runtime files copied to godot/bin/"
    else
        warn "CEF runtime files not found in build output — run 'cargo build --release' in cef-gdext first"
    fi

    # Build the .NET solution
    log "Building solution: $SOLUTION"
    dotnet build "$SOLUTION" --configuration Release

    # Build Svelte UI if Node.js is available
    if check_node; then
        if [[ -f "$SVELTE_UI/package.json" ]]; then
            # Install dependencies if node_modules is missing
            if [[ ! -d "$SVELTE_UI/node_modules" ]]; then
                log "Installing Svelte UI dependencies..."
                (cd "$SVELTE_UI" && npm install --legacy-peer-deps)
            fi

            log "Building Svelte UI..."
            (cd "$SVELTE_UI" && npm run build)

            # Copy build output to godot/ui/ so res://ui resolves correctly
            # (adapter-static outputs to svelte-ui/dist/)
            rm -rf "$GODOT_PROJECT/ui"
            cp -r "$SVELTE_UI/dist" "$GODOT_PROJECT/ui"
            log "Svelte UI built and copied to godot/ui/"
        fi
    fi

    log "Build complete."
}

is_svelte_running() {
    if [[ -f "$SVELTE_PID_FILE" ]]; then
        local pid
        pid=$(cat "$SVELTE_PID_FILE")
        if kill -0 "$pid" 2>/dev/null; then
            return 0
        fi
        # Stale PID file
        rm -f "$SVELTE_PID_FILE"
    fi
    return 1
}

start_svelte_dev() {
    if is_svelte_running; then
        log "Svelte dev server already running (PID: $(cat "$SVELTE_PID_FILE"))"
        return 0
    fi

    if ! check_node; then
        error "Node.js required for Svelte dev server"
        return 1
    fi

    if [[ ! -d "$SVELTE_UI/node_modules" ]]; then
        log "Installing Svelte dependencies..."
        (cd "$SVELTE_UI" && npm install --legacy-peer-deps)
    fi

    log "Starting Svelte dev server..."
    (cd "$SVELTE_UI" && npm run dev > /dev/null 2>&1) &
    local pid=$!
    echo "$pid" > "$SVELTE_PID_FILE"

    # Wait for server to be ready
    local max_wait=30
    local waited=0
    while ! curl -s http://localhost:5173 > /dev/null 2>&1; do
        sleep 0.5
        waited=$((waited + 1))
        if [[ $waited -ge $max_wait ]]; then
            error "Svelte dev server failed to start"
            kill "$pid" 2>/dev/null || true
            rm -f "$SVELTE_PID_FILE"
            return 1
        fi
    done

    log "Svelte dev server started (PID: $pid) at http://localhost:5173"
}

stop_svelte_dev() {
    if [[ -f "$SVELTE_PID_FILE" ]]; then
        local pid
        pid=$(cat "$SVELTE_PID_FILE")
        if kill -0 "$pid" 2>/dev/null; then
            log "Stopping Svelte dev server (PID: $pid)..."
            kill "$pid" 2>/dev/null || true
            # Also kill any child processes (npm spawns node)
            pkill -P "$pid" 2>/dev/null || true
        fi
        rm -f "$SVELTE_PID_FILE"
        log "Svelte dev server stopped."
    else
        log "Svelte dev server is not running."
    fi
}

cmd_dev() {
    start_svelte_dev
    log "Dev server running. Press Ctrl+C to stop."
    # Wait for the dev server process
    if [[ -f "$SVELTE_PID_FILE" ]]; then
        wait "$(cat "$SVELTE_PID_FILE")" 2>/dev/null || true
    fi
}

cmd_stop() {
    stop_svelte_dev
}

cmd_run() {
    local godot_bin
    if ! godot_bin=$(find_godot); then
        error "Godot (Mono/.NET) not found."
        error "Set it with: $0 set-godot /path/to/Godot_v4.x-stable_mono_linux_x86_64/"
        exit 1
    fi

    # CEF runtime must be in godot/bin/ (copied during build).
    # libcef_gdext.so and cef-helper both use RUNPATH=$ORIGIN to find
    # libcef.so in the same directory, so LD_LIBRARY_PATH is not needed.
    if [[ ! -f "$CEF_BIN/libcef.so" ]]; then
        warn "CEF runtime not found in godot/bin/. Run '$0 build' first."
        warn "The app will launch but CEF features will be unavailable."
    fi

    # Start Svelte dev server if not already running
    start_svelte_dev || warn "Continuing without dev server (will use file:// fallback if available)"

    # CEF_PATH tells CEF where to find resource files (icudtl.dat, .pak, locales)
    export CEF_PATH="$CEF_BIN"
    log "CEF_PATH=$CEF_PATH"

    log "Launching with: $godot_bin"

    # Run Godot and stop dev server when it exits
    "$godot_bin" --path "$GODOT_PROJECT"
    local exit_code=$?

    log "Godot exited with code $exit_code"
    stop_svelte_dev

    exit $exit_code
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
        dev)       cmd_dev ;;
        stop)      cmd_stop ;;
        -h|--help|help) usage; exit 0 ;;
        *)
            error "Unknown command: ${args[$i]}"
            usage
            exit 1
            ;;
    esac
    i=$((i + 1))
done
