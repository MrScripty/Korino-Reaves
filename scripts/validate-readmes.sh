#!/bin/bash
# validate-readmes.sh
# Validates that all significant directories have README.md files.
# Used by lefthook pre-commit hooks.
#
# Usage: ./scripts/validate-readmes.sh
#
# Exit codes:
#   0 - All directories have README files
#   1 - Missing README files detected

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
NC='\033[0m' # No Color

# Project root (relative to script location)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Directories that should be excluded from README checks
EXCLUDED_DIRS=(
    "node_modules"
    ".git"
    ".godot"
    ".svelte-kit"
    "build"
    "dist"
    "bin"
    "obj"
    ".vs"
    ".vscode"
    ".idea"
    "coverage"
    "TestResults"
    "__pycache__"
    ".agent-complete"
    ".claude"
    ".github"
    "UAssetAPI"
    "UAssetGUI"
    "plans"
    "static"
)

# Directories that MUST have README files
REQUIRED_README_DIRS=(
    "godot/scripts/Models"
    "godot/scripts/Cef"
    "godot/scripts/Bridge"
    "godot/scripts/Assets"
    "godot/scripts/Rendering"
    "godot/scripts/Agent"
    "godot/scripts/Diff"
    "svelte-ui/src/lib/bridge"
    "svelte-ui/src/lib/components"
    "svelte-ui/src/lib/view-models"
)

ERRORS=0
WARNINGS=0

# Build exclusion pattern for find
build_exclude_pattern() {
    local pattern=""
    for dir in "${EXCLUDED_DIRS[@]}"; do
        pattern="$pattern -not -path '*/$dir/*' -not -path '*/$dir'"
    done
    echo "$pattern"
}

# Check specific required directories
check_required_dirs() {
    echo "Checking required README directories..."

    for dir in "${REQUIRED_README_DIRS[@]}"; do
        if [ -d "$PROJECT_ROOT/$dir" ]; then
            if [ ! -f "$PROJECT_ROOT/$dir/README.md" ]; then
                echo -e "${RED}[ERROR]${NC} Missing required README: $dir/README.md"
                ((ERRORS++))
            else
                echo -e "${GREEN}[OK]${NC} README exists: $dir/README.md"
            fi
        fi
    done
}

# Check if a directory should be excluded
is_excluded() {
    local dir="$1"
    for excluded in "${EXCLUDED_DIRS[@]}"; do
        if [[ "$dir" == *"/$excluded"* ]] || [[ "$dir" == *"/$excluded" ]] || [[ "$dir" == "$excluded"* ]]; then
            return 0
        fi
    done
    return 1
}

# Main function
main() {
    echo "Validating README files..."
    echo "Project root: $PROJECT_ROOT"
    echo ""

    # Check required directories
    check_required_dirs

    echo ""
    echo "=== Validation Summary ==="

    if [ $ERRORS -eq 0 ]; then
        echo -e "${GREEN}All required READMEs present!${NC}"
        exit 0
    else
        echo -e "${RED}$ERRORS missing README(s)${NC}"
        echo ""
        echo "Please add README.md files to the directories listed above."
        echo "README template:"
        echo ""
        echo "  # Directory Name"
        echo "  "
        echo "  ## Purpose"
        echo "  Brief description of what this directory contains."
        echo "  "
        echo "  ## Contents"
        echo "  - \`File1.cs\` - Description"
        echo "  - \`File2.cs\` - Description"
        echo ""
        exit 1
    fi
}

main "$@"
