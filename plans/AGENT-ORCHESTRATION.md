# Agent Orchestration & Git Workflow

This document defines the procedure for starting agents, execution order, parallelization rules, and git commit standards.

---

## Agent Execution Order

### Overview

```text
┌─────────────────────────────────────────────────────────────────────────┐
│                           PHASE 1: FOUNDATIONS                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  Step 1 (BLOCKING):                                                     │
│  ┌─────────────────────────────────────┐                                │
│  │  00-shared-contracts                │  ← Must complete FIRST         │
│  │  Defines immutable IPC contracts    │    before any parallel work    │
│  └─────────────────────────────────────┘                                │
│                         │                                               │
│                         ▼                                               │
│  Step 2 (PARALLEL):                                                     │
│  ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐            │
│  │ 01-backend      │ │ 02-frontend     │ │ 03-tooling      │            │
│  │ CEF, IPC, C#    │ │ Svelte UI       │ │ Linting, hooks  │            │
│  └────────┬────────┘ └────────┬────────┘ └─────────────────┘            │
│           │                   │                                         │
│           └─────────┬─────────┘                                         │
│                     ▼                                                   │
│  ┌─────────────────────────────────────┐                                │
│  │  SYNC POINT: IPC Integration Test   │  ← Backend + Frontend must     │
│  │  Verify bidirectional communication │    pass before Phase 2         │
│  └─────────────────────────────────────┘                                │
│                                                                         │
├─────────────────────────────────────────────────────────────────────────┤
│                           PHASE 2: FEATURES                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  Step 3 (SEQUENTIAL):                                                   │
│  ┌─────────────────────────────────────┐                                │
│  │  04-asset-agent                     │  ← Depends on backend          │
│  │  UAssetAPI/CUE4Parse integration    │    completion                  │
│  └─────────────────────────────────────┘                                │
│                         │                                               │
│                         ▼                                               │
│  Step 4 (PARALLEL):                                                     │
│  ┌─────────────────────────┐ ┌─────────────────────────┐                │
│  │ 05-diff-agent           │ │ 06-ai-agent             │                │
│  │ Diff engine, mod porting│ │ Semantic Kernel, pumas  │                │
│  └─────────────────────────┘ └─────────────────────────┘                │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### Dependency Matrix

| Agent | Depends On | Can Run With |
| ----- | ---------- | ------------ |
| 00-shared-contracts | Nothing | Nothing (must complete first) |
| 01-backend | 00-shared-contracts | 02-frontend, 03-tooling |
| 02-frontend | 00-shared-contracts | 01-backend, 03-tooling |
| 03-tooling | Nothing | 01-backend, 02-frontend |
| 04-asset | 01-backend, 00-shared-contracts | Nothing (sequential) |
| 05-diff | 04-asset, 02-frontend | 06-ai |
| 06-ai | 04-asset, 05-diff | 05-diff |

### Execution Timeline

```text
TIME ──────────────────────────────────────────────────────────────────────────►

STEP 1 ║  00-shared-contracts  ║
       ║  (run alone, wait)    ║
       ╚═══════════════════════╝
                │
                ▼
STEP 2 ╔═══════════════════════╦═══════════════════════╦═══════════════════════╗
       ║  01-backend           ║  02-frontend          ║  03-tooling           ║
       ║  (Tab 1)              ║  (Tab 2)              ║  (Tab 3)              ║
       ╚═══════════╦═══════════╩═══════════╦═══════════╩═══════════════════════╝
                   │                       │
                   └───────────┬───────────┘
                               ▼
       ╔═══════════════════════════════════════════════╗
       ║  SYNC: IPC Integration Test                   ║
       ║  (Backend + Frontend must pass together)      ║
       ╚═══════════════════════════════════════════════╝
                               │
                               ▼
STEP 3 ║  04-asset-agent       ║
       ║  (run alone, wait)    ║
       ╚═══════════════════════╝
                │
                ▼
STEP 4 ╔═══════════════════════════════════╦═══════════════════════════════════╗
       ║  05-diff-agent                    ║  06-ai-agent                      ║
       ║  (Tab 1)                          ║  (Tab 2)                          ║
       ╚═══════════════════════════════════╩═══════════════════════════════════╝
                               │
                               ▼
                            DONE
```

### VS Code Tabs Setup

| Step | Action | Tabs Open |
| ---- | ------ | --------- |
| 1 | Open 1 tab, run `00-shared-contracts`, wait for completion | 1 |
| 2 | Open 3 tabs simultaneously for `01`, `02`, `03` | 3 |
| - | Wait for all 3 to complete + pass IPC test | - |
| 3 | Open 1 tab, run `04-asset`, wait for completion | 1 |
| 4 | Open 2 tabs simultaneously for `05`, `06` | 2 |
| - | Wait for both to complete | - |

---

## Agent Initialization Prompts

Copy and paste these prompts into a new Claude Code tab to start each agent.

### 00 - Shared Contracts Agent

```text
You are the Shared Contracts Agent for the Korino-Reaves project.

Read these files first:
- plans/ARCHITECTURE.md
- plans/AGENT-ORCHESTRATION.md
- plans/00-shared-contracts.md

Your role: Define the immutable IPC contracts and shared data models that all other agents will depend on. You MUST complete before any other agents can start.

Follow the git commit standards in AGENT-ORCHESTRATION.md. Use scope "contracts" and include "Agent: 00-shared-contracts" in commit footers.

Begin by reading the required files, then execute your assigned tasks.
```

### 01 - Backend Agent

```text
You are the Backend Agent for the Korino-Reaves project.

Read these files first:
- plans/ARCHITECTURE.md
- plans/AGENT-ORCHESTRATION.md
- plans/01-backend-agent.md

Your role: Implement CEF integration, IPC handling, and Godot C# infrastructure. You run in parallel with 02-frontend and 03-tooling.

Your owned directories:
- godot/scripts/Cef/
- godot/scripts/Bridge/
- godot/scenes/

Follow the git commit standards in AGENT-ORCHESTRATION.md. Use scope "backend" and include "Agent: 01-backend" in commit footers.

Begin by reading the required files, then execute your assigned tasks.
```

### 02 - Frontend Agent

```text
You are the Frontend Agent for the Korino-Reaves project.

Read these files first:
- plans/ARCHITECTURE.md
- plans/AGENT-ORCHESTRATION.md
- plans/02-frontend-agent.md

Your role: Implement the Svelte UI layer - components, view models, and styling. You run in parallel with 01-backend and 03-tooling.

Your owned directories:
- svelte-ui/src/ (except bridge/types.ts)
- svelte-ui/static/

CRITICAL: The frontend is a pure presentation layer. ALL data comes from the C# backend via IPC. No business logic in frontend.

Follow the git commit standards in AGENT-ORCHESTRATION.md. Use scope "frontend" and include "Agent: 02-frontend" in commit footers.

Begin by reading the required files, then execute your assigned tasks.
```

### 03 - Tooling Agent

```text
You are the Tooling Agent for the Korino-Reaves project.

Read these files first:
- plans/ARCHITECTURE.md
- plans/AGENT-ORCHESTRATION.md
- plans/03-tooling-agent.md

Your role: Set up code quality automation - linting, formatting, and git hooks. You run in parallel with 01-backend and 02-frontend.

Your owned files:
- .editorconfig
- .eslintrc.js
- .prettierrc.json
- lefthook.yml
- Directory.Build.props

Follow the git commit standards in AGENT-ORCHESTRATION.md. Use scope "tooling" and include "Agent: 03-tooling" in commit footers.

Begin by reading the required files, then execute your assigned tasks.
```

### 04 - Asset Agent

```text
You are the Asset Agent for the Korino-Reaves project.

Read these files first:
- plans/ARCHITECTURE.md
- plans/AGENT-ORCHESTRATION.md
- plans/04-asset-agent.md

Your role: Integrate UAssetAPI and CUE4Parse for asset loading, tree building, and property editing. You run alone after Phase 1 completes.

Your owned directories:
- godot/scripts/Assets/
- godot/scripts/Rendering/

Follow the git commit standards in AGENT-ORCHESTRATION.md. Use scope "asset" and include "Agent: 04-asset" in commit footers.

Begin by reading the required files, then execute your assigned tasks.
```

### 05 - Diff Agent

```text
You are the Diff Agent for the Korino-Reaves project.

Read these files first:
- plans/ARCHITECTURE.md
- plans/AGENT-ORCHESTRATION.md
- plans/05-diff-agent.md

Your role: Implement the diff engine, conflict detection, and mod porting workflow. You run in parallel with 06-ai after 04-asset completes.

Your owned directories:
- godot/scripts/Diff/
- svelte-ui/src/lib/components/diff/

Follow the git commit standards in AGENT-ORCHESTRATION.md. Use scope "diff" and include "Agent: 05-diff" in commit footers.

Begin by reading the required files, then execute your assigned tasks.
```

### 06 - AI Agent

```text
You are the AI Agent for the Korino-Reaves project.

Read these files first:
- plans/ARCHITECTURE.md
- plans/AGENT-ORCHESTRATION.md
- plans/06-ai-agent.md

Your role: Integrate Microsoft Semantic Kernel and pumas-library for local AI automation. You run in parallel with 05-diff after 04-asset completes.

Your owned directories:
- godot/scripts/Agent/
- svelte-ui/src/lib/bridge/agent-api.ts

Follow the git commit standards in AGENT-ORCHESTRATION.md. Use scope "ai" and include "Agent: 06-ai" in commit footers.

Begin by reading the required files, then execute your assigned tasks.
```

---

## Parallelization Rules

### Directory Ownership

Each agent owns specific directories. Agents MUST NOT modify files outside their designated areas.

| Agent | Owned Directories |
| ----- | ----------------- |
| 00-shared-contracts | `svelte-ui/src/lib/bridge/types.ts`, `godot/scripts/Models/`, `plans/` |
| 01-backend | `godot/scripts/Cef/`, `godot/scripts/Bridge/`, `godot/scenes/` |
| 02-frontend | `svelte-ui/src/` (except `bridge/types.ts`), `svelte-ui/static/` |
| 03-tooling | `.editorconfig`, `.eslintrc.js`, `.prettierrc.json`, `lefthook.yml`, `Directory.Build.props` |
| 04-asset | `godot/scripts/Assets/`, `godot/scripts/Rendering/` |
| 05-diff | `godot/scripts/Diff/`, `svelte-ui/src/lib/components/diff/` |
| 06-ai | `godot/scripts/Agent/`, `svelte-ui/src/lib/bridge/agent-api.ts` |

### Conflict Prevention

1. **Check before writing**: Before creating/modifying any file, verify it's within your owned directories
2. **Shared contracts are immutable**: Once 00-shared-contracts completes, `types.ts` and `Models/` are frozen
3. **IPC message types are append-only**: New message types can be added, existing ones cannot be modified
4. **Track your files**: Each agent must maintain a list of files it created/modified

### Communication Between Agents

Agents communicate via:

1. **Shared contracts** - Pre-defined interfaces in `types.ts` and `Models/`
2. **Sync points** - Explicit checkpoints where agents must wait
3. **File markers** - Stub files indicating completion (e.g., `.backend-ready`)

---

## Git Commit Standards

### Conventional Commits Format

All commits must follow the [Conventional Commits](https://www.conventionalcommits.org/) specification:

```text
<type>(<scope>): <description>

[optional body]

[optional footer(s)]
```

### Commit Types

| Type | Usage | Example |
| ---- | ----- | ------- |
| `feat` | New feature | `feat(backend): add CEF initialization` |
| `fix` | Bug fix | `fix(frontend): correct null payload handling` |
| `refactor` | Code restructuring (no behavior change) | `refactor(asset): extract texture decoding` |
| `chore` | Build, tooling, config | `chore(tooling): configure ESLint rules` |
| `docs` | Documentation only | `docs(contracts): add IPC message examples` |
| `style` | Formatting, whitespace | `style(frontend): fix indentation` |
| `test` | Adding/updating tests | `test(backend): add IPC handler tests` |
| `perf` | Performance improvement | `perf(asset): optimize tree building` |

### Scopes (By Agent)

| Scope | Agent | Usage |
| ----- | ----- | ----- |
| `contracts` | 00 | Shared types, interfaces, models |
| `backend` | 01 | CEF, IPC handlers, Godot C# core |
| `frontend` | 02 | Svelte components, view models, styling |
| `tooling` | 03 | Linting, formatting, git hooks |
| `asset` | 04 | UAssetAPI, CUE4Parse, asset loading |
| `diff` | 05 | Diff engine, conflict detection |
| `ai` | 06 | Semantic Kernel, pumas-library integration |

### Agent Commit Rules

**CRITICAL: Agents must follow these rules to avoid committing each other's work.**

1. **Commit frequently**: Commit after completing each logical step, not in bulk
2. **Stage specific files only**: Use `git add <file1> <file2>` - NEVER use `git add .` or `git add -A`
3. **Verify before committing**: Always run `git status` to ensure only your files are staged
4. **Include agent footer**: Add `Agent: <agent-id>` in the commit footer
5. **Stay in your lane**: Only commit files within your owned directories

### Pre-Commit Checklist

Before every commit, agents must:

```bash
# 1. Check status - ensure only your files are staged
git status

# 2. Review diff - verify changes are yours
git diff --cached

# 3. Verify directory ownership
# All staged files must be in your owned directories

# 4. Run local checks
dotnet format --verify-no-changes  # For C# files
npm run lint                        # For TS/Svelte files
```

### Commit Message Examples

**Feature commit:**

```text
feat(backend): implement CefManager singleton

Add CEF lifecycle management with:
- Offscreen rendering configuration
- Message pump integration with Godot _Process
- Subprocess path configuration

Agent: 01-backend
```

**Bug fix commit:**

```text
fix(frontend): handle null payload in IPC messages

Previously, messages with null payloads caused JSON parse errors.
Now gracefully handles null/undefined payloads.

Fixes #42

Agent: 02-frontend
```

**Refactor commit:**

```text
refactor(asset): split AssetLoader into focused classes

Extract responsibilities:
- AssetLoader: orchestration only
- PakReader: PAK file handling
- MappingsLoader: .usmap support

No behavior changes.

Agent: 04-asset
```

### Handling Merge Conflicts

If parallel agents create merge conflicts:

1. **Stop both agents** immediately
2. **Identify the conflict** - usually a shared file that shouldn't be shared
3. **Review directory ownership** - one agent is likely out of bounds
4. **Resolve manually** - human intervention required
5. **Update ownership rules** if necessary

---

## Sync Points & Verification

### Phase 1 Sync Point: IPC Integration Test

Before Phase 2 can begin, backend and frontend must pass this test:

```typescript
// Test: Bidirectional IPC communication
// 1. Frontend sends: { type: 'test', action: 'ping', payload: { timestamp: Date.now() } }
// 2. Backend receives, responds: { type: 'test', action: 'pong', payload: { ... } }
// 3. Frontend receives pong within 1000ms
// PASS: Round-trip completes successfully
```

### Agent Completion Markers

Each agent creates a marker file on completion:

```text
.agent-complete/
├── 00-contracts.done
├── 01-backend.done
├── 02-frontend.done
├── 03-tooling.done
├── 04-asset.done
├── 05-diff.done
└── 06-ai.done
```

### Verification Commands

```bash
# Check which agents are complete
ls -la .agent-complete/

# Verify no uncommitted changes from other agents
git status --porcelain

# View commit history by agent
git log --grep="Agent: 01-backend" --oneline
git log --grep="Agent: 02-frontend" --oneline
```

---

## Troubleshooting

### Agent Modified Wrong File

```bash
# 1. Identify the bad commit
git log --oneline -10

# 2. Check which files were changed
git show <commit-hash> --stat

# 3. If recent, revert
git revert <commit-hash>

# 4. Agent should re-do work in correct location
```

### Two Agents Modified Same File

```bash
# 1. Check blame to see who touched it
git blame <file>

# 2. Review ownership rules
# 3. One agent must back out their changes
# 4. Update AGENT-ORCHESTRATION.md if ownership was unclear
```

### Agent Committed Unstaged Work

```bash
# This happens when using `git add .` - DON'T DO THIS

# 1. Identify affected commits
git log --name-only

# 2. Interactive rebase to split commits (human intervention)
# 3. Reassign files to correct agent commits
```

---

## Quick Reference

### Execution Summary

```text
┌─────────────────────────────────────────────────────────────────┐
│  STEP 1:  00-shared-contracts  (alone)                          │
├─────────────────────────────────────────────────────────────────┤
│  STEP 2:  01-backend  │  02-frontend  │  03-tooling  (parallel) │
├─────────────────────────────────────────────────────────────────┤
│  SYNC:    IPC Integration Test                                  │
├─────────────────────────────────────────────────────────────────┤
│  STEP 3:  04-asset  (alone)                                     │
├─────────────────────────────────────────────────────────────────┤
│  STEP 4:  05-diff  │  06-ai  (parallel)                         │
└─────────────────────────────────────────────────────────────────┘
```

### Golden Rules

1. **Contracts first** - Nothing runs until 00 completes
2. **Own your directory** - Never modify files outside your area
3. **Commit often** - Small, focused commits are easier to manage
4. **Stage explicitly** - Never use `git add .` or `git add -A`
5. **Verify before push** - Always check `git status` and `git diff --cached`
