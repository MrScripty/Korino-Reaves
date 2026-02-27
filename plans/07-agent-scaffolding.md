# Agent Scaffolding Plan (Project Graph + Metadata + GUI Selection)

## Goal

Introduce backend scaffolding so a future AI agent can:

1. Explore project files.
2. Traverse dependency relationships.
3. Read rich asset metadata.
4. Select/focus files in the GUI.

This plan is intentionally additive: no full agent behavior implementation yet.

## Current State (What Already Exists)

### Strong Existing Building Blocks

- File/project browsing:
  - `ProjectHandler` opens projects and pushes file tree updates.
  - `FilesystemHandler` provides directory listing and file info APIs.
  - `FileTreeBuilder` already normalizes UE file groups (`.uasset/.uexp/.ubulk`).
- Dependency graph + metadata:
  - `DependencyScanner` builds a SQLite metadata graph.
  - `DependencyDatabase` supports rich query surface (deps, dependents, imports, exports, properties, asset tables).
  - `DependencyHandler` already exposes query and scan operations over IPC.
- GUI selection:
  - `SelectionHandler` owns canonical selection/expand state.
  - `TreeHandler` supports expansion and file-browser navigation actions.
- Frontend state model:
  - Svelte view-models follow backend-owned data pattern and already react to `project`, `tree`, `selection`, and `dependency` updates.

### Gaps for Agent Readiness

- Agent runtime is not integrated into the app wiring:
  - `AgentHandler` exists but is not registered in dispatcher setup.
- Agent plugins are asset-focused only:
  - No plugin/capability surface for project file exploration, dependency graph traversal, or GUI selection.
- Missing stable internal capability boundary:
  - Current handlers expose functionality, but agent code would otherwise couple directly to handler/IPC details.
- No explicit guardrails for agent actions:
  - Need read-only defaults, bounded result sizes, and explicit write/selection intent policies.
- Agent status contract is incomplete for UI orchestration:
  - Need streamed progress/events, not only single final responses.

## Target Architecture

### Layering

```text
Agent Runtime (Semantic Kernel / orchestration)
            |
            v
Agent Capability Interfaces (stable contracts, no IPC types)
            |
            v
Capability Adapters (wrap existing services/handlers/database)
            |
            v
Existing Core Systems (Project/FileTree, Dependency DB, Selection/Tree, Asset Manager)
```

### New Scaffolding Components

Create in `godot/scripts/Agent/Capabilities/`:

- `IProjectExplorerCapability`
  - list root/children by project path or node ID
  - search files by name/pattern
  - get file node details (type, size, modified, companion info)
- `IDependencyGraphCapability`
  - ensure/trigger scan status
  - get direct dependencies/dependents
  - get bounded related cluster
  - query by class/property
- `IMetadataCapability`
  - fetch per-asset tables from dependency DB
  - return compact summaries + paged table slices
- `IGuiSelectionCapability`
  - select node by canonical node ID
  - expand/collapse branch
  - reveal/focus file node in tree

Create in `godot/scripts/Agent/`:

- `AgentCapabilityRegistry.cs`
  - resolves capability implementations once, shared by plugins/workflows.
- `AgentExecutionPolicy.cs`
  - read-only default.
  - bounded list/query sizes.
  - optional allow-list for side-effecting actions.

Create plugin surfaces in `godot/scripts/Agent/Plugins/`:

- `ProjectPlugin.cs`
- `DependencyGraphPlugin.cs`
- `MetadataPlugin.cs`
- `GuiPlugin.cs`

These plugins should call capability interfaces, not handlers directly.

## Data and ID Conventions

Use existing node ID conventions as canonical references:

- project tree file node: `file:<relative/path>`
- project tree folder node: `folder:<relative/path>`
- asset tree export node: `export-<index>`

Add a shared helper for safe ID parsing/validation to avoid duplicated string parsing.

## Efficiency and Non-Interference Rules

1. Reuse existing scanner/database. Do not build a second graph store.
2. Keep capability calls bounded:
   - page size defaults (ex: 100 rows)
   - max depth defaults for traversal (ex: 2-3)
3. Add cancellation support for long-running queries/scans.
4. Keep all changes additive:
   - existing UI IPC routes remain unchanged.
   - handlers keep current responsibilities.
5. Cache only lightweight summaries in agent layer; keep source-of-truth in current systems.

## Integration Plan (Phased)

### Phase 1: Capability Boundary

- Add capability interfaces + concrete adapters wrapping current handlers/services/database.
- Add unit tests for each interface behavior with mocked dependencies.
- No UI changes required.

Exit criteria:

- Agent layer can explore files/dependencies/metadata/selection via interfaces only.
- No direct handler/IPC parsing logic inside plugins.

### Phase 2: Agent Wiring

- Register `AgentHandler` during dispatcher setup.
- Build `AgentManager` using new plugins from capability registry.
- Add diff plugin registration path in main wiring (feature-flagged if needed).

Exit criteria:

- Agent requests can execute end-to-end without changing existing UI workflows.

### Phase 3: Agent Event Contract

- Standardize agent event actions:
  - `agent:status`
  - `agent:step`
  - `agent:result`
  - `agent:error`
- Emit progress updates from workflows/capabilities.
- Update frontend `agent-api.ts` to subscribe to action-specific messages.

Exit criteria:

- UI receives deterministic status/progress lifecycle for each request ID.

### Phase 4: Guardrails + Observability

- Implement `AgentExecutionPolicy` checks per tool call.
- Add structured telemetry tags for:
  - capability name
  - duration
  - row/result counts
  - cancellation reason
- Add integration tests:
  - open project -> scan -> query dependency tables -> select file node.

Exit criteria:

- predictable resource usage, auditable behavior, no regressions in existing project UI flow.

## Suggested File Additions (Scaffolding Only)

- `godot/scripts/Agent/Capabilities/IProjectExplorerCapability.cs`
- `godot/scripts/Agent/Capabilities/IDependencyGraphCapability.cs`
- `godot/scripts/Agent/Capabilities/IMetadataCapability.cs`
- `godot/scripts/Agent/Capabilities/IGuiSelectionCapability.cs`
- `godot/scripts/Agent/Capabilities/*Capability.cs` (adapters)
- `godot/scripts/Agent/AgentCapabilityRegistry.cs`
- `godot/scripts/Agent/AgentExecutionPolicy.cs`
- `godot/tests/Agent/Capabilities/*Tests.cs`

## Risks and Mitigations

- Risk: Capability duplication with handlers.
  - Mitigation: adapters call existing services/DB; avoid copy-paste logic.
- Risk: Large token/context payloads from metadata tables.
  - Mitigation: summary-first responses + pagination.
- Risk: Agent side effects disrupting user workflow.
  - Mitigation: read-only default + explicit side-effect policy.
- Risk: Contract drift between TS and C#.
  - Mitigation: keep IPC contracts additive; put agent event actions behind typed wrappers.

## Definition of Done for Scaffolding

- Agent-specific capabilities exist behind stable interfaces.
- Main app can register and run agent handler without replacing current UI behavior.
- File exploration, dependency traversal, metadata queries, and GUI selection are callable through capability plugins.
- Tests cover capability behavior and one end-to-end integration path.
