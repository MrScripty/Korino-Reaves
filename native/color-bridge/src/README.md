# Color Bridge Source

## Purpose
Native source for the color-management bridge used by the rendering pipeline.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `color_bridge.cpp` | Native bridge implementation for color-space integration. |

## Problem
The rendering stack needs a native bridge for color operations that are not handled directly in the managed/runtime layers.

## Constraints
- Native code must stay compatible with the runtime that loads it.
- The bridge is small but crosses an ABI boundary.

## Decision
Keep the color bridge as a dedicated native source directory rather than mixing it into the managed rendering code.

## Alternatives Rejected
- Reimplement the bridge entirely in managed code: rejected because the current dependency surface is native.

## Invariants
- ABI expectations between the native bridge and the managed caller remain aligned.
- This directory stays limited to the color bridge responsibility.

## Revisit Triggers
- Color management moves fully into another native or managed dependency.
- Additional native bridge files justify a fuller module split.

## Dependencies
**Internal:** `godot/scripts/Rendering`.
**External:** Native toolchain and color-management libraries used by the bridge build.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: the native bridge is currently a small implementation detail.
- Revisit trigger: the bridge API grows or changes incompatibly.

## Usage Examples
```bash
g++ -shared -o color_bridge.so native/color-bridge/src/color_bridge.cpp
```

## API Consumer Contract
- Managed/runtime callers load the compiled bridge, not this source file directly.
- ABI changes require synchronized updates on both sides of the bridge.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: this directory produces native code, not persisted machine-readable artifacts.
- Revisit trigger: checked-in manifests or generated headers are added.
