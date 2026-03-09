# Data

## Purpose
Persistence layer for project metadata, dependency graphs, edit history, and import caches.

## Contents
| File/Folder | Description |
|-------------|-------------|
| `AssetCache.cs` | Import-cache persistence and cache validity checks. |
| `DependencyDatabase.cs` | SQLite-backed dependency graph and metadata store. |
| `EditDatabase.cs` | Persisted asset edit tracking. |

## Problem
Asset extraction, dependency analysis, and editing generate state that must survive across sessions and support efficient lookups.

## Constraints
- Data must stay compatible with project-local state on disk.
- SQLite-backed operations can grow large for real game projects.

## Decision
Use local SQLite-backed stores and cache directories under project roots, with dedicated types for each persisted concern.

## Alternatives Rejected
- Keep all metadata in memory only: rejected because scans and imports are too expensive to recompute every run.

## Invariants
- Dependency and cache data remain project-scoped.
- Persistence logic stays separate from IPC handlers and UI view models.

## Revisit Triggers
- Database size or performance forces sharding or a different store.
- Multiple persisted schemas need explicit migration infrastructure.

## Dependencies
**Internal:** `godot/scripts/Assets`, `godot/scripts/Bridge`, `godot/scripts/Rendering`.
**External:** `Microsoft.Data.Sqlite`.

## Related ADRs
- None identified as of 2026-03-09.
- Reason: schema ownership is local to the persistence layer today.
- Revisit trigger: formal migration/versioning policy is introduced.

## Usage Examples
```csharp
using var db = new DependencyDatabase(logger);
db.Open(projectPath);
```

## API Consumer Contract
- Internal runtime services and handlers are the intended consumers.
- Callers must open the database/cache against a project path before issuing queries.
- Failures should be surfaced as project-local data errors, not silently ignored.

## Structured Producer Contract
- Dependency DB, edit DB, and cache artifacts are project-local persisted outputs.
- Schema changes require compatibility review and migration rules for existing project state.
- Cache validity semantics must stay aligned with game-version handling.
