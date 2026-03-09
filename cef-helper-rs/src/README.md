# CEF Helper Source

## Purpose
Rust source for the CEF helper executable required by the offscreen browser runtime.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `main.rs` | Helper executable entry point. |

## Problem
CEF requires a separate helper process for its subprocess model. This directory supplies that executable in a minimal Rust wrapper.

## Constraints
- The helper must stay compatible with the CEF version used by `cef-gdext`.
- The binary is launched by the host runtime, not directly by the UI.

## Decision
Keep the helper as a thin Rust binary that tracks the same CEF crate version as the embedding crate.

## Alternatives Rejected
- Reuse an opaque prebuilt helper: rejected because version drift would be harder to control.

## Invariants
- The helper stays version-aligned with `cef-gdext`.
- The executable remains buildable by Cargo without extra repo-local generators.

## Revisit Triggers
- CEF changes its helper process requirements.
- Packaging needs platform-specific helper variants beyond the current binary.

## Dependencies
**Internal:** `cef-gdext/`.
**External:** `cef`.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: the helper is a thin runtime requirement rather than a broader architecture decision.
- Revisit trigger: helper startup behavior or packaging changes materially.

## Usage Examples
```bash
cargo build --manifest-path cef-helper-rs/Cargo.toml --locked
```

## API Consumer Contract
- The host runtime launches the built helper executable as part of CEF initialization.
- Consumers should not depend on ad hoc command-line behavior beyond standard CEF subprocess expectations.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: this directory produces a binary executable, not a structured artifact contract.
- Revisit trigger: the helper begins emitting machine-consumed metadata or config.
