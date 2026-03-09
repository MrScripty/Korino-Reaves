#!/usr/bin/env bash
set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

SOURCE_ROOTS=(
    "godot/scripts"
    "godot/tests"
    "svelte-ui/src"
    "cef-gdext/src"
    "cef-helper-rs/src"
    "native/color-bridge/src"
)

EXCLUDED_NAMES=(
    ".git"
    ".godot"
    ".launcher-state"
    ".svelte-kit"
    "bin"
    "build"
    "coverage"
    "dist"
    "node_modules"
    "obj"
    "target"
)

ERRORS=0

is_excluded_name() {
    local name="$1"
    for excluded in "${EXCLUDED_NAMES[@]}"; do
        if [[ "$name" == "$excluded" ]]; then
            return 0
        fi
    done
    return 1
}

check_source_root() {
    local root="$1"

    while IFS= read -r dir; do
        if [[ ! -f "$dir/README.md" ]]; then
            printf '%b[ERROR]%b Missing README: %s/README.md\n' "$RED" "$NC" "${dir#$PROJECT_ROOT/}"
            ERRORS=$((ERRORS + 1))
        fi
    done < <(
        find "$PROJECT_ROOT/$root" \
            \( \
                -name .git -o \
                -name .godot -o \
                -name .launcher-state -o \
                -name .svelte-kit -o \
                -name bin -o \
                -name build -o \
                -name coverage -o \
                -name dist -o \
                -name node_modules -o \
                -name obj -o \
                -name target \
            \) -prune -o -type d -print | sort
    )
}

main() {
    echo "Validating source directory READMEs..."
    echo "Project root: $PROJECT_ROOT"
    echo

    for root in "${SOURCE_ROOTS[@]}"; do
        check_source_root "$root"
    done

    echo
    if [[ $ERRORS -eq 0 ]]; then
        printf '%bAll source READMEs present.%b\n' "$GREEN" "$NC"
        exit 0
    fi

    printf '%b%d README(s) missing.%b\n' "$RED" "$ERRORS" "$NC"
    exit 1
}

main "$@"
