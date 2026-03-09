# Asset Tests

## Purpose
Tests for asset loading, mappings, property access, and tree-building helpers.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `AssetLoaderTests.cs` | Asset load/save behavior. |
| `MappingsManagerTests.cs` | Mappings loading and selection behavior. |
| `PropertyServiceTests.cs` | Property read/write path handling. |
| `TreeBuilderTests.cs` | Tree-shape generation from asset data. |

## Problem
Asset-facing helpers sit at the center of inspection and editing workflows and need stable behavior under versioned Unreal content.

## Constraints
- Tests must not depend on full manual UI sessions.

## Decision
Keep asset helper coverage in a dedicated directory aligned to the handwritten asset services.

## Alternatives Rejected
- Test asset helpers only via IPC flows: rejected because lower-level failures would be harder to isolate.

## Invariants
- Asset helper tests remain focused on parsing/service behavior.
- Tree and property coverage stays close to asset helper contracts.

## Revisit Triggers
- Asset tests require larger shared fixtures or golden files.

## Dependencies
**Internal:** `godot/scripts/Assets`.
**External:** .NET test runner.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: this is a direct subsystem test partition.
- Revisit trigger: asset tests adopt heavier fixture-management rules.

## Usage Examples
```bash
dotnet test godot/tests/UAssetViewer.Tests.csproj --filter Assets
```

## API Consumer Contract
- The test runner is the intended consumer.
- Tests should remain deterministic and focused on asset subsystem behavior.

## Structured Producer Contract
- None identified as of 2026-03-09.
- Reason: the directory does not publish machine-consumed artifacts.
- Revisit trigger: checked-in asset snapshots or fixtures are added.
