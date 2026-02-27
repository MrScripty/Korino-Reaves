# Agent - AI Agent Framework

## Purpose

AI agent integration using Microsoft Semantic Kernel with local LLM support
via Ollama. Enables automated asset operations through natural language prompts,
including mod porting between game versions.

## Contents

- `AgentConfig.cs` - Configuration for LLM provider and model settings
- `AgentManager.cs` - Semantic Kernel initialization and prompt execution
- `AgentHandler.cs` - IPC handler for frontend communication
- `IModelLibrary.cs` - Interface for pumas-core model management
- `PumasModelLibrary.cs` - pumas-core UniFFI integration via generated C# bindings
- `Plugins/` - Semantic Kernel plugins exposing app functionality to the AI
- `Workflows/` - Pre-built agent workflows for common tasks
- `Generated/` - Auto-generated UniFFI C# bindings (do not edit manually)

### Plugins

- `AssetPlugin.cs` - Asset file operations (open, save, export)
- `NavigationPlugin.cs` - Asset tree browsing and search
- `EditPlugin.cs` - Property reading and writing
- `DiffPlugin.cs` - Asset comparison and conflict detection
- `ModelPlugin.cs` - AI model library management via pumas-core
- `ProjectPlugin.cs` - Project file tree exploration
- `DependencyGraphPlugin.cs` - Dependency graph traversal and search
- `MetadataPlugin.cs` - Asset metadata snapshot queries
- `GuiPlugin.cs` - GUI selection and expansion controls

### Workflows

- `ModPortingWorkflow.cs` - Automated mod porting between game versions
- `AssetExplorerWorkflow.cs` - AI-driven asset analysis and Q&A

## Design Decisions

- **Semantic Kernel** chosen for native C# support and excellent tool-calling API
- **Ollama** as default LLM runtime for local, privacy-preserving inference
- **pumas-core via UniFFI** for model management with auto-generated C# bindings
- **DiffPlugin uses reflection** to avoid compile-time coupling with the Diff module
- **AgentManager.Create()** factory pattern ensures all dependencies are validated upfront

## Dependencies

- Internal: `Services/`, `Assets/`, `Bridge/`, `Models/`, `Infrastructure/`
- External: `Microsoft.SemanticKernel`, `Microsoft.SemanticKernel.Connectors.Ollama`
- Native: `libpumas_uniffi.so` (pumas-core via UniFFI, symlinked from pumas-library build)

## Execution Policy

Agent runtime now uses an execution policy with a read-only default:

- Asset write operations disabled (`save_asset`, `save_asset_as`, `export_json`)
- Property edits disabled (`set_property`)
- Model downloads disabled (`download_model`)
- GUI mutation enabled by default (`select_node`, expand/collapse actions)

Environment variables can override policy at startup:

- `KORINO_AGENT_ENABLED` (feature flag; defaults to enabled when unset)
- `KORINO_AGENT_ALLOW_WRITES` (master switch for write/edit/download)
- `KORINO_AGENT_ALLOW_ASSET_WRITES`
- `KORINO_AGENT_ALLOW_PROPERTY_EDITS`
- `KORINO_AGENT_ALLOW_MODEL_DOWNLOADS`
- `KORINO_AGENT_ALLOW_GUI_MUTATION`
- `KORINO_AGENT_MAX_PROJECT_SEARCH_RESULTS`
- `KORINO_AGENT_MAX_DEPENDENCY_RESULTS`
- `KORINO_AGENT_MAX_DEPENDENCY_RELATED_RESULTS`
- `KORINO_AGENT_MAX_DEPENDENCY_DEPTH`
- `KORINO_AGENT_MAX_METADATA_ROWS`

## Usage Examples

```csharp
// Initialize the agent
var config = AgentConfig.Default;
var agent = AgentManager.Create(config, assetService, treeService, propertyService, modelLibrary, logger);

// Execute a prompt
var result = await agent.ExecuteAsync("What properties does Export[0] have?");

// Run a mod porting workflow
var workflow = new ModPortingWorkflow(agent, logger);
var result = await workflow.ExecuteAsync(originalPath, updatedPath, modPath, outputPath);
```

## Local LLM Requirements

- 7B+ parameter model with tool-calling support
- 8GB VRAM (GPU) or 16GB RAM (CPU inference)
- Recommended: Mistral 7B Instruct, Llama 3.1 8B, Qwen 2.5 7B
