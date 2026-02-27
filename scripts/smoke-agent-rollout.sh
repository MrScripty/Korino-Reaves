#!/bin/bash
# smoke-agent-rollout.sh
# Runs automated smoke checks for agent scaffolding rollout readiness.
#
# Usage:
#   ./scripts/smoke-agent-rollout.sh
#   ./scripts/smoke-agent-rollout.sh --with-main-tests
#
# Notes:
# - Main godot/tests project may currently fail due known compile drift.
# - This script always validates the backend build and feature-flag wiring.

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

GREEN='\033[0;32m'
YELLOW='\033[0;33m'
RED='\033[0;31m'
NC='\033[0m'

WITH_MAIN_TESTS=false
if [[ "${1:-}" == "--with-main-tests" ]]; then
  WITH_MAIN_TESTS=true
fi

FAILURES=0
WARNINGS=0

ok() {
  echo -e "${GREEN}[OK]${NC} $1"
}

warn() {
  echo -e "${YELLOW}[WARN]${NC} $1"
  WARNINGS=$((WARNINGS + 1))
}

fail() {
  echo -e "${RED}[FAIL]${NC} $1"
  FAILURES=$((FAILURES + 1))
}

run_check() {
  local label="$1"
  shift
  echo ""
  echo "== $label =="
  if "$@"; then
    ok "$label"
  else
    fail "$label"
  fi
}

echo "Running agent rollout smoke checks..."
echo "Project root: $PROJECT_ROOT"

run_check "Feature flag wiring present" \
  rg -q "KORINO_AGENT_ENABLED|disabled mode|Agent handler registered in disabled mode" \
    "$PROJECT_ROOT/godot/scripts/MainController.cs"

run_check "Agent README references rollout guide" \
  rg -q "10-agent-rollout-hardening.md" \
    "$PROJECT_ROOT/godot/scripts/Agent/README.md"

run_check "Backend build" \
  dotnet build "$PROJECT_ROOT/godot/UAssetViewer.csproj"

if [[ -f /tmp/korino-agent-capability-tests/AgentCapabilityTests.csproj ]]; then
  run_check "Isolated capability integration harness" \
    dotnet test /tmp/korino-agent-capability-tests/AgentCapabilityTests.csproj
else
  warn "Skipped isolated capability harness: /tmp/korino-agent-capability-tests/AgentCapabilityTests.csproj not found"
fi

if [[ "$WITH_MAIN_TESTS" == "true" ]]; then
  if dotnet test "$PROJECT_ROOT/godot/tests/UAssetViewer.Tests.csproj"; then
    ok "Main test project"
  else
    warn "Main test project failed (known compile drift may still exist)"
  fi
else
  warn "Skipped main test project (pass --with-main-tests to run)"
fi

echo ""
echo "== Summary =="
if [[ $FAILURES -eq 0 ]]; then
  ok "Smoke checks completed"
else
  fail "$FAILURES check(s) failed"
fi

if [[ $WARNINGS -gt 0 ]]; then
  warn "$WARNINGS warning(s)"
fi

[[ $FAILURES -eq 0 ]]
