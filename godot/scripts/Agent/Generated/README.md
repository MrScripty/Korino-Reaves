# Generated Agent Bindings

## Purpose
Generated C# bindings for the local model-management libraries used by the agent runtime.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `pumas_library.cs` | Generated UniFFI bindings for the pumas library surface. |
| `pumas_uniffi.cs` | Generated support types and marshalling helpers. |

## Problem
The agent runtime depends on foreign-library bindings that should not be maintained by hand. This directory keeps those generated artifacts isolated from handwritten agent logic.

## Constraints
- Files are generated and can be overwritten.
- Hand edits will be lost on regeneration.

## Decision
Keep generated bindings in their own directory and treat them as build-managed artifacts.

## Alternatives Rejected
- Mix generated and handwritten files: rejected because regeneration would be unsafe.

## Invariants
- Files in this directory are not manually edited.
- Regeneration must come from the owning binding workflow.

## Revisit Triggers
- The binding generator changes output layout.
- The agent runtime no longer depends on the current foreign library.

## Dependencies
**Internal:** `godot/scripts/Agent/`.
**External:** UniFFI-generated bindings for pumas.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: this is a generated-output boundary, not a standalone architecture decision.
- Revisit trigger: the binding toolchain changes.

## Usage Examples
```csharp
using var library = new PumasLibrary();
```

## API Consumer Contract
- Consumers use the generated types through handwritten wrappers rather than editing these files directly.
- Regeneration can break callers if the upstream foreign API changes.

## Structured Producer Contract
- File contents are generator-owned.
- Regenerate instead of editing when the foreign API changes.
- Compatibility follows the upstream binding generator and foreign library contract.
