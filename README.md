# Korino-Reaves

## Purpose
Korino-Reaves is a Godot-based Unreal asset inspection and modding workbench. It combines a Svelte frontend, C# runtime/editor logic, Rust-backed CEF embedding, and Unreal asset parsing libraries so developers can inspect assets, diff versions, extract content, and automate repetitive workflows locally.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `launcher.sh` | Canonical install, build, run, test, and release-smoke entry point. |
| `godot/` | Godot project, C# runtime, IPC handlers, rendering, and tests. |
| `svelte-ui/` | Svelte UI and typed bridge contract consumer. |
| `cef-gdext/` | Rust GDExtension that embeds CEF offscreen and bridges IPC/framebuffer updates. |
| `cef-helper-rs/` | Helper executable required by the CEF embedding model. |
| `native/` | Native bridge code and runtime assets that support rendering/color workflows. |
| `scripts/` | Repo maintenance helpers, including README validation. |

## Problem
Unreal modding workflows often force users to jump between separate asset viewers, diff tools, extractors, and undocumented manual steps. This repository exists to keep those operations in one locally runnable toolchain with explicit contracts between the UI, runtime, and native layers.

## Constraints
- Unreal asset parsing depends on upstream libraries and version-specific behavior.
- The UI must communicate across a process/runtime boundary through stable IPC messages.
- CEF, Godot, .NET, Rust, and Node must coexist without corrupting operator state.
- Linux and Windows are required supported platforms.

## Decision
Use Godot as the host runtime, Svelte for the UI, Rust for CEF/native integration, and `launcher.sh` as the canonical developer/operator workflow surface. Keep shared contracts explicit rather than letting the frontend and backend drift independently.

## Alternatives Rejected
- Keep the upstream `UAssetGUI` workflow as the primary entry point: rejected because it does not represent the current architecture.
- Split the UI and runtime into separate repos: rejected because contract changes are frequent and need atomic verification.

## Invariants
- `launcher.sh` remains the canonical local workflow surface.
- IPC contracts stay synchronized between `godot/scripts/Models` and `svelte-ui/src/lib/bridge`.
- First-party verification must pass through frontend checks, Rust builds, and .NET build/test gates.

## Revisit Triggers
- A second runtime host replaces Godot.
- IPC contracts require versioning beyond the current single-repo model.
- Release packaging becomes a first-class supported workflow again.

## Dependencies
**Internal:** `godot/`, `svelte-ui/`, `cef-gdext/`, `cef-helper-rs/`, `native/`, `scripts/`.
**External:** Godot 4, .NET 8+, Node.js 20+, Rust stable, CEF, UAssetAPI, CUE4Parse, Semantic Kernel.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: the current repo documents architecture in module READMEs but does not yet maintain ADRs for these choices.
- Revisit trigger: a breaking runtime, IPC, or packaging decision lands.

## Usage Examples
```bash
./launcher.sh --install
./launcher.sh --build
./launcher.sh --run
./launcher.sh --test
```

## API Consumer Contract
- Operators and CI invoke repo workflows through `launcher.sh`.
- Frontend/backend callers exchange messages through the typed IPC contract only.
- Verification failures are blocking for commit, push, and CI flows.

## Structured Producer Contract
- `godot/scripts/Models` and `svelte-ui/src/lib/bridge/types.ts` define the stable IPC payload shapes.
- `godot/scripts/Agent/Generated` is generated output and must be regenerated rather than hand-edited.
- `Cargo.lock` and `package-lock.json` are checked-in dependency contracts and must stay in sync with tool-managed dependency changes.
