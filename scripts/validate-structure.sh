#!/bin/bash
# validate-structure.sh
# Validates the project directory structure matches ARCHITECTURE.md requirements.
#
# Usage: ./scripts/validate-structure.sh
#
# Exit codes:
#   0 - All validations passed
#   1 - Structure validation failed

# Note: Not using set -e because arithmetic operators can return non-zero
# set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[0;33m'
NC='\033[0m' # No Color

# Project root (relative to script location)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

echo "Validating project structure..."
echo "Project root: $PROJECT_ROOT"
echo ""

ERRORS=0
WARNINGS=0

# Function to check if directory exists
check_dir() {
    local dir="$1"
    local required="$2"

    if [ -d "$PROJECT_ROOT/$dir" ]; then
        echo -e "${GREEN}[OK]${NC} Directory exists: $dir"
        return 0
    else
        if [ "$required" = "required" ]; then
            echo -e "${RED}[ERROR]${NC} Missing required directory: $dir"
            ((ERRORS++))
            return 1
        else
            echo -e "${YELLOW}[WARN]${NC} Optional directory missing: $dir"
            ((WARNINGS++))
            return 0
        fi
    fi
}

# Function to check if file exists
check_file() {
    local file="$1"
    local required="$2"

    if [ -f "$PROJECT_ROOT/$file" ]; then
        echo -e "${GREEN}[OK]${NC} File exists: $file"
        return 0
    else
        if [ "$required" = "required" ]; then
            echo -e "${RED}[ERROR]${NC} Missing required file: $file"
            ((ERRORS++))
            return 1
        else
            echo -e "${YELLOW}[WARN]${NC} Optional file missing: $file"
            ((WARNINGS++))
            return 0
        fi
    fi
}

# Function to check if directory has README
check_readme() {
    local dir="$1"

    if [ -d "$PROJECT_ROOT/$dir" ]; then
        if [ -f "$PROJECT_ROOT/$dir/README.md" ]; then
            echo -e "${GREEN}[OK]${NC} README exists: $dir/README.md"
            return 0
        else
            echo -e "${YELLOW}[WARN]${NC} Missing README: $dir/README.md"
            ((WARNINGS++))
            return 0
        fi
    fi
}

echo "=== Checking Core Structure ==="

# Check plans directory
check_dir "plans" "required"
check_file "plans/ARCHITECTURE.md" "required"
check_file "plans/AGENT-ORCHESTRATION.md" "required"
check_file "plans/00-shared-contracts.md" "required"

# Check shared contracts (critical - must exist before other agents)
echo ""
echo "=== Checking Shared Contracts ==="

check_dir "svelte-ui/src/lib/bridge" "required"
check_file "svelte-ui/src/lib/bridge/types.ts" "required"

check_dir "godot/scripts/Models" "required"
check_file "godot/scripts/Models/IpcMessage.cs" "required"
check_file "godot/scripts/Models/TreeNode.cs" "required"
check_file "godot/scripts/Models/PropertyValue.cs" "required"
check_file "godot/scripts/Models/SelectionState.cs" "required"
check_file "godot/scripts/Models/DiffResult.cs" "required"
check_file "godot/scripts/Models/AgentMessage.cs" "required"
check_file "godot/scripts/Models/ErrorResponse.cs" "required"
check_file "godot/scripts/Models/AssetInfo.cs" "required"
check_file "godot/scripts/Models/ViewportState.cs" "required"

# Check expected directory structure (from ARCHITECTURE.md)
echo ""
echo "=== Checking Expected Directories ==="

# Godot directories
check_dir "godot" "optional"
check_dir "godot/scripts" "optional"
check_dir "godot/scripts/Cef" "optional"
check_dir "godot/scripts/Bridge" "optional"
check_dir "godot/scripts/Assets" "optional"
check_dir "godot/scripts/Rendering" "optional"
check_dir "godot/scripts/Input" "optional"
check_dir "godot/scenes" "optional"

# Svelte directories
check_dir "svelte-ui" "optional"
check_dir "svelte-ui/src" "optional"
check_dir "svelte-ui/src/lib" "optional"
check_dir "svelte-ui/src/lib/components" "optional"
check_dir "svelte-ui/src/lib/view-models" "optional"

# CEF helper
check_dir "cef-helper" "optional"

# Check README files in key directories
echo ""
echo "=== Checking README Files ==="

check_readme "godot/scripts/Models"
check_readme "svelte-ui/src/lib/bridge"
check_readme "godot/scripts/Cef"
check_readme "godot/scripts/Bridge"
check_readme "godot/scripts/Assets"

# Summary
echo ""
echo "=== Validation Summary ==="
if [ $ERRORS -eq 0 ]; then
    echo -e "${GREEN}All required validations passed!${NC}"
else
    echo -e "${RED}$ERRORS error(s) found${NC}"
fi

if [ $WARNINGS -gt 0 ]; then
    echo -e "${YELLOW}$WARNINGS warning(s) found${NC}"
fi

echo ""

if [ $ERRORS -gt 0 ]; then
    echo -e "${RED}Validation FAILED${NC}"
    exit 1
else
    echo -e "${GREEN}Validation PASSED${NC}"
    exit 0
fi
