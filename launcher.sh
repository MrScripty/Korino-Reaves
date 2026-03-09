#!/usr/bin/env bash
set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOLUTION="$PROJECT_ROOT/UAssetViewer.slnx"
GODOT_PROJECT="$PROJECT_ROOT/godot"
GODOT_CSPROJ="$GODOT_PROJECT/UAssetViewer.csproj"
DOTNET_TEST_PROJECT="$GODOT_PROJECT/tests/UAssetViewer.Tests.csproj"
SVELTE_UI="$PROJECT_ROOT/svelte-ui"
CEF_GDEXT="$PROJECT_ROOT/cef-gdext"
CEF_HELPER_RS="$PROJECT_ROOT/cef-helper-rs"
CEF_BIN="$GODOT_PROJECT/bin"
ENV_FILE="$PROJECT_ROOT/.launcher.env"
SVELTE_PID_FILE="$PROJECT_ROOT/.svelte-dev.pid"

LAUNCHER_STATE_ROOT="${KORINO_REAVES_LAUNCHER_STATE_ROOT:-$PROJECT_ROOT/.launcher-state}"
ISOLATE_STATE="${KORINO_REAVES_LAUNCHER_ISOLATE_STATE:-1}"
SMOKE_WINDOW_SECONDS="${KORINO_REAVES_SMOKE_WINDOW_SECONDS:-10}"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

ACTION=""
ACTION_VALUE=""
RUN_ARGS=()
TEMP_STATE_DIR=""

log() {
    echo -e "${GREEN}[launcher]${NC} $*"
}

warn() {
    echo -e "${YELLOW}[launcher]${NC} $*"
}

error() {
    echo -e "${RED}[launcher]${NC} $*" >&2
}

usage() {
    cat <<EOF
Korino-Reaves launcher

Usage:
  ./launcher.sh --help
  ./launcher.sh --install
  ./launcher.sh --build
  ./launcher.sh --build-release
  ./launcher.sh --run [-- <godot args>]
  ./launcher.sh --run-release [-- <godot args>]
  ./launcher.sh --test
  ./launcher.sh --release-smoke

Additional actions:
  ./launcher.sh --dev-server
  ./launcher.sh --stop-dev-server
  ./launcher.sh --set-godot <path>

Flags:
  --install          Check/install repo dependencies
  --build            Build development artifacts
  --build-release    Build release artifacts
  --run              Run the app in development mode
  --run-release      Run the app against release artifacts
  --test             Run the canonical local verification suite
  --release-smoke    Launch the release path briefly and fail on unhealthy startup
  --dev-server       Start only the Svelte dev server
  --stop-dev-server  Stop the Svelte dev server
  --set-godot PATH   Save the Godot binary to .launcher.env
  --help             Show this help and exit 0

Managed state:
  By default, launcher actions use isolated state under:
    $LAUNCHER_STATE_ROOT
  Set KORINO_REAVES_LAUNCHER_ISOLATE_STATE=0 to use host state intentionally.

Examples:
  ./launcher.sh --install
  ./launcher.sh --build
  ./launcher.sh --build-release
  ./launcher.sh --run
  ./launcher.sh --run -- --verbose
  ./launcher.sh --run-release
  ./launcher.sh --test
  ./launcher.sh --release-smoke

Exit codes:
  0  success
  1  action failed
  2  invalid launcher usage
EOF
}

cleanup() {
    if [[ -n "$TEMP_STATE_DIR" && -d "$TEMP_STATE_DIR" ]]; then
        rm -rf "$TEMP_STATE_DIR"
    fi
}

trap cleanup EXIT

if [[ -f "$ENV_FILE" ]]; then
    # shellcheck disable=SC1090
    source "$ENV_FILE"
fi

set_action() {
    local action_name="$1"
    local action_value="${2:-}"

    if [[ -n "$ACTION" ]]; then
        error "Exactly one action flag must be selected"
        usage
        exit 2
    fi

    ACTION="$action_name"
    ACTION_VALUE="$action_value"
}

parse_args() {
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --help)
                set_action "help"
                shift
                ;;
            --install)
                set_action "install"
                shift
                ;;
            --build)
                set_action "build"
                shift
                ;;
            --build-release)
                set_action "build_release"
                shift
                ;;
            --run)
                set_action "run"
                shift
                ;;
            --run-release)
                set_action "run_release"
                shift
                ;;
            --test)
                set_action "test"
                shift
                ;;
            --release-smoke)
                set_action "release_smoke"
                shift
                ;;
            --dev-server)
                set_action "dev_server"
                shift
                ;;
            --stop-dev-server)
                set_action "stop_dev_server"
                shift
                ;;
            --set-godot)
                if [[ $# -lt 2 ]]; then
                    error "--set-godot requires a path argument"
                    usage
                    exit 2
                fi
                set_action "set_godot" "$2"
                shift 2
                ;;
            --)
                if [[ "$ACTION" != "run" && "$ACTION" != "run_release" ]]; then
                    error "Argument forwarding is only supported for --run and --run-release"
                    usage
                    exit 2
                fi
                shift
                RUN_ARGS=("$@")
                return
                ;;
            --*)
                error "Unknown flag: $1"
                usage
                exit 2
                ;;
            *)
                error "Positional arguments are not allowed: $1"
                usage
                exit 2
                ;;
        esac
    done

    if [[ -z "$ACTION" ]]; then
        error "No action selected"
        usage
        exit 2
    fi
}

check_command() {
    command -v "$1" >/dev/null 2>&1
}

check_dotnet_sdk() {
    check_command dotnet
}

check_node_runtime() {
    check_command node && check_command npm
}

check_rust_toolchain() {
    check_command cargo
}

check_nuget_packages() {
    local app_assets=""
    app_assets="$GODOT_PROJECT/.godot/mono/temp/obj/project.assets.json"

    if [[ ! -f "$app_assets" ]]; then
        app_assets="$GODOT_PROJECT/obj/project.assets.json"
    fi

    [[ -f "$app_assets" ]] && [[ -f "$GODOT_PROJECT/tests/obj/project.assets.json" ]]
}

check_npm_modules() {
    [[ -d "$SVELTE_UI/node_modules" ]]
}

install_dotnet_sdk() {
    error "[error] dotnet-sdk install is not automated. Install .NET SDK 8.0+ and rerun --install."
    return 1
}

install_node_runtime() {
    error "[error] node-runtime install is not automated. Install Node.js 20+ and rerun --install."
    return 1
}

install_rust_toolchain() {
    error "[error] rust-toolchain install is not automated. Install Rust via rustup and rerun --install."
    return 1
}

install_nuget_packages() {
    dotnet restore "$GODOT_CSPROJ"
    dotnet restore "$DOTNET_TEST_PROJECT"
}

install_npm_modules() {
    (cd "$SVELTE_UI" && npm install --legacy-peer-deps)
}

run_install_step() {
    local name="$1"
    local check_fn="$2"
    local install_fn="$3"

    if "$check_fn"; then
        log "[ok] $name already satisfied"
        return
    fi

    log "[install] $name missing; installing"
    "$install_fn"

    if ! "$check_fn"; then
        error "[error] $name install failed"
        exit 1
    fi

    log "[done] $name installed"
}

require_dependency() {
    local name="$1"
    local check_fn="$2"
    local guidance="$3"

    if ! "$check_fn"; then
        error "$name is missing. $guidance"
        exit 1
    fi
}

setup_managed_state_env() {
    local mode="$1"
    local temporary="${2:-0}"
    local state_dir=""

    if [[ "$ISOLATE_STATE" != "1" ]]; then
        log "State mode: host"
        return
    fi

    if [[ "$temporary" == "1" ]]; then
        mkdir -p "$LAUNCHER_STATE_ROOT"
        state_dir="$(mktemp -d "$LAUNCHER_STATE_ROOT/${mode}.XXXXXX")"
        TEMP_STATE_DIR="$state_dir"
    else
        state_dir="$LAUNCHER_STATE_ROOT/$mode"
        mkdir -p "$state_dir"
    fi

    mkdir -p "$state_dir/xdg-config" "$state_dir/xdg-data" "$state_dir/xdg-state" "$state_dir/dotnet"

    export XDG_CONFIG_HOME="$state_dir/xdg-config"
    export XDG_DATA_HOME="$state_dir/xdg-data"
    export XDG_STATE_HOME="$state_dir/xdg-state"
    export DOTNET_CLI_HOME="$state_dir/dotnet"
    export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

    log "State mode: isolated ($state_dir)"
}

resolve_godot_binary() {
    local path="$1"
    path="${path%/}"

    if [[ -f "$path" && -x "$path" ]]; then
        printf '%s\n' "$path"
        return 0
    fi

    if [[ -d "$path" ]]; then
        local candidate=""
        for candidate in "$path"/Godot_* "$path"/godot "$path"/godot4; do
            if [[ -f "$candidate" && -x "$candidate" ]]; then
                printf '%s\n' "$candidate"
                return 0
            fi
        done
        error "No executable Godot binary found in directory: $path"
        return 1
    fi

    error "Path does not exist: $path"
    return 1
}

find_godot() {
    if [[ -n "${GODOT_PATH:-}" ]]; then
        local resolved=""
        if resolved="$(resolve_godot_binary "$GODOT_PATH")"; then
            printf '%s\n' "$resolved"
            return 0
        fi
        warn "Saved GODOT_PATH is invalid: $GODOT_PATH"
    fi

    local candidate=""
    for candidate in godot4 godot godot-mono; do
        if check_command "$candidate"; then
            printf '%s\n' "$candidate"
            return 0
        fi
    done

    return 1
}

ensure_godot() {
    if ! find_godot >/dev/null; then
        error "Godot not found. Save it with ./launcher.sh --set-godot <path>."
        exit 1
    fi
}

write_godot_path() {
    local resolved=""
    resolved="$(resolve_godot_binary "$ACTION_VALUE")"
    printf 'GODOT_PATH="%s"\n' "$resolved" >"$ENV_FILE"
    log "Saved GODOT_PATH to $ENV_FILE"
}

platform_artifact_names() {
    case "$(uname -s)" in
        Linux*)
            printf '%s;%s\n' "libcef_gdext.so" "cef-helper"
            ;;
        Darwin*)
            printf '%s;%s\n' "libcef_gdext.dylib" "cef-helper"
            ;;
        MINGW*|MSYS*|CYGWIN*)
            printf '%s;%s\n' "cef_gdext.dll" "cef-helper.exe"
            ;;
        *)
            error "Unsupported platform for launcher artifact mapping: $(uname -s)"
            exit 1
            ;;
    esac
}

copy_cef_artifacts() {
    local profile="$1"
    local artifact_names=""
    local gdext_lib=""
    local helper_bin=""
    artifact_names="$(platform_artifact_names)"
    gdext_lib="${artifact_names%%;*}"
    helper_bin="${artifact_names##*;}"

    mkdir -p "$CEF_BIN"
    cp "$CEF_GDEXT/target/$profile/$gdext_lib" "$CEF_BIN/"
    cp "$CEF_HELPER_RS/target/$profile/$helper_bin" "$CEF_BIN/"

    local runtime_dir=""
    runtime_dir="$(find "$CEF_GDEXT/target/$profile/build" -path "*/cef-dll-sys-*/out/*" -type d | head -n 1 || true)"
    if [[ -n "$runtime_dir" ]]; then
        cp -u "$runtime_dir"/libcef.so "$CEF_BIN/" 2>/dev/null || true
        cp -u "$runtime_dir"/libEGL.so "$CEF_BIN/" 2>/dev/null || true
        cp -u "$runtime_dir"/libGLESv2.so "$CEF_BIN/" 2>/dev/null || true
        cp -u "$runtime_dir"/libvk_swiftshader.so "$CEF_BIN/" 2>/dev/null || true
        cp -u "$runtime_dir"/libvulkan.so.1 "$CEF_BIN/" 2>/dev/null || true
        cp -u "$runtime_dir"/chrome-sandbox "$CEF_BIN/" 2>/dev/null || true
        cp -u "$runtime_dir"/vk_swiftshader_icd.json "$CEF_BIN/" 2>/dev/null || true
        cp -u "$runtime_dir"/icudtl.dat "$CEF_BIN/" 2>/dev/null || true
        cp -u "$runtime_dir"/v8_context_snapshot.bin "$CEF_BIN/" 2>/dev/null || true
        cp -u "$runtime_dir"/*.pak "$CEF_BIN/" 2>/dev/null || true
        cp -rn "$runtime_dir"/locales "$CEF_BIN/" 2>/dev/null || true
    else
        warn "CEF runtime directory not found under $CEF_GDEXT/target/$profile/build"
    fi
}

build_frontend_bundle() {
    (cd "$SVELTE_UI" && npm run build)
    rm -rf "$GODOT_PROJECT/ui"
    cp -r "$SVELTE_UI/dist" "$GODOT_PROJECT/ui"
}

build_dev_artifacts() {
    require_dependency "dotnet-sdk" check_dotnet_sdk "Run ./launcher.sh --install after installing .NET SDK 8.0+."
    require_dependency "rust-toolchain" check_rust_toolchain "Run ./launcher.sh --install after installing Rust."
    require_dependency "node-runtime" check_node_runtime "Run ./launcher.sh --install after installing Node.js."
    require_dependency "nuget-packages" check_nuget_packages "Run ./launcher.sh --install first."
    require_dependency "npm-modules" check_npm_modules "Run ./launcher.sh --install first."

    log "Building development Rust artifacts"
    (cd "$CEF_GDEXT" && cargo build)
    (cd "$CEF_HELPER_RS" && cargo build)
    copy_cef_artifacts "debug"

    log "Building development .NET artifacts"
    dotnet build "$SOLUTION" --configuration Debug --no-restore

    log "Building frontend bundle"
    build_frontend_bundle
}

build_release_artifacts() {
    require_dependency "dotnet-sdk" check_dotnet_sdk "Run ./launcher.sh --install after installing .NET SDK 8.0+."
    require_dependency "rust-toolchain" check_rust_toolchain "Run ./launcher.sh --install after installing Rust."
    require_dependency "node-runtime" check_node_runtime "Run ./launcher.sh --install after installing Node.js."
    require_dependency "nuget-packages" check_nuget_packages "Run ./launcher.sh --install first."
    require_dependency "npm-modules" check_npm_modules "Run ./launcher.sh --install first."

    log "Building release Rust artifacts"
    (cd "$CEF_GDEXT" && cargo build --release)
    (cd "$CEF_HELPER_RS" && cargo build --release)
    copy_cef_artifacts "release"

    log "Building release .NET artifacts"
    dotnet build "$SOLUTION" --configuration Release --no-restore

    log "Building frontend bundle"
    build_frontend_bundle
}

start_svelte_dev() {
    if [[ -f "$SVELTE_PID_FILE" ]]; then
        local existing_pid=""
        existing_pid="$(cat "$SVELTE_PID_FILE")"
        if kill -0 "$existing_pid" 2>/dev/null; then
            log "Svelte dev server already running (PID: $existing_pid)"
            return 0
        fi
        rm -f "$SVELTE_PID_FILE"
    fi

    require_dependency "node-runtime" check_node_runtime "Run ./launcher.sh --install after installing Node.js."
    require_dependency "npm-modules" check_npm_modules "Run ./launcher.sh --install first."

    log "Starting Svelte dev server"
    (cd "$SVELTE_UI" && npm run dev >/dev/null 2>&1) &
    local pid=$!
    echo "$pid" >"$SVELTE_PID_FILE"

    if check_command curl; then
        local attempts=0
        until curl -s http://127.0.0.1:5173 >/dev/null 2>&1; do
            attempts=$((attempts + 1))
            if [[ $attempts -ge 60 ]]; then
                error "Svelte dev server did not become ready"
                kill "$pid" 2>/dev/null || true
                rm -f "$SVELTE_PID_FILE"
                exit 1
            fi
            sleep 0.5
        done
    else
        sleep 2
    fi

    log "Svelte dev server running at http://127.0.0.1:5173 (PID: $pid)"
}

stop_svelte_dev() {
    if [[ ! -f "$SVELTE_PID_FILE" ]]; then
        log "Svelte dev server is not running"
        return 0
    fi

    local pid=""
    pid="$(cat "$SVELTE_PID_FILE")"
    if kill -0 "$pid" 2>/dev/null; then
        kill "$pid" 2>/dev/null || true
        pkill -P "$pid" 2>/dev/null || true
    fi

    rm -f "$SVELTE_PID_FILE"
    log "Svelte dev server stopped"
}

ensure_release_artifacts() {
    local release_dll=""
    release_dll="$(find "$GODOT_PROJECT/.godot/mono/temp/bin/Release" -name "UAssetViewer.dll" -print -quit 2>/dev/null || true)"
    if [[ -z "$release_dll" || ! -f "$GODOT_PROJECT/ui/index.html" ]]; then
        error "Release artifacts are missing. Run ./launcher.sh --build-release first."
        exit 1
    fi
}

run_godot_project() {
    local mode="$1"
    shift
    local godot_bin=""
    godot_bin="$(find_godot)"

    export CEF_PATH="$CEF_BIN"

    if [[ "$mode" == "dev" ]]; then
        start_svelte_dev
        trap stop_svelte_dev EXIT
    fi

    log "Launching Godot with mode: $mode"
    "$godot_bin" --path "$GODOT_PROJECT" "$@"
}

cmd_install() {
    setup_managed_state_env "install" 0
    run_install_step "dotnet-sdk" check_dotnet_sdk install_dotnet_sdk
    run_install_step "node-runtime" check_node_runtime install_node_runtime
    run_install_step "rust-toolchain" check_rust_toolchain install_rust_toolchain
    run_install_step "nuget-packages" check_nuget_packages install_nuget_packages
    run_install_step "npm-modules" check_npm_modules install_npm_modules
}

cmd_build() {
    setup_managed_state_env "build" 0
    build_dev_artifacts
}

cmd_build_release() {
    setup_managed_state_env "build-release" 0
    build_release_artifacts
}

cmd_run() {
    setup_managed_state_env "run" 0
    ensure_godot
    require_dependency "nuget-packages" check_nuget_packages "Run ./launcher.sh --install first."
    require_dependency "npm-modules" check_npm_modules "Run ./launcher.sh --install first."
    run_godot_project "dev" "${RUN_ARGS[@]}"
}

cmd_run_release() {
    setup_managed_state_env "run-release" 0
    ensure_godot
    ensure_release_artifacts
    run_godot_project "release" "${RUN_ARGS[@]}"
}

cmd_test() {
    setup_managed_state_env "test" 1
    require_dependency "dotnet-sdk" check_dotnet_sdk "Run ./launcher.sh --install after installing .NET SDK 8.0+."
    require_dependency "node-runtime" check_node_runtime "Run ./launcher.sh --install after installing Node.js."
    require_dependency "nuget-packages" check_nuget_packages "Run ./launcher.sh --install first."
    require_dependency "npm-modules" check_npm_modules "Run ./launcher.sh --install first."

    log "Running frontend static checks"
    (cd "$SVELTE_UI" && npm run check)
    (cd "$SVELTE_UI" && npx tsc --noEmit)

    log "Running .NET tests"
    dotnet test "$DOTNET_TEST_PROJECT" --no-restore
}

cmd_release_smoke() {
    setup_managed_state_env "release-smoke" 1
    ensure_godot
    ensure_release_artifacts

    log "Starting release smoke window (${SMOKE_WINDOW_SECONDS}s)"
    run_godot_project "release" &
    local smoke_pid=$!
    sleep "$SMOKE_WINDOW_SECONDS"

    if kill -0 "$smoke_pid" 2>/dev/null; then
        log "Release process survived smoke window; terminating cleanly"
        kill "$smoke_pid" 2>/dev/null || true
        wait "$smoke_pid" 2>/dev/null || true
        return 0
    fi

    wait "$smoke_pid"
    local exit_code=$?
    error "Release process exited during smoke window with code $exit_code"
    exit 1
}

cmd_dev_server() {
    setup_managed_state_env "dev-server" 0
    start_svelte_dev
}

cmd_stop_dev_server() {
    stop_svelte_dev
}

parse_args "$@"

case "$ACTION" in
    help)
        usage
        ;;
    install)
        cmd_install
        ;;
    build)
        cmd_build
        ;;
    build_release)
        cmd_build_release
        ;;
    run)
        cmd_run
        ;;
    run_release)
        cmd_run_release
        ;;
    test)
        cmd_test
        ;;
    release_smoke)
        cmd_release_smoke
        ;;
    dev_server)
        cmd_dev_server
        ;;
    stop_dev_server)
        cmd_stop_dev_server
        ;;
    set_godot)
        write_godot_path
        ;;
    *)
        error "Unhandled action: $ACTION"
        exit 2
        ;;
esac
