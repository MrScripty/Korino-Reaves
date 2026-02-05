# AI Agent

**Phase**: 2 - Features
**Depends on**: Asset Agent (04), Diff Agent (05)

## Scope

Semantic Kernel integration, plugins for asset operations, pumas-library client.

## Purpose

Enable local AI agents to automate mod porting and asset manipulation:
- Browse and read assets programmatically
- Compare versions and identify changes
- Make intelligent decisions about conflict resolution
- Apply updates automatically with human oversight

## Reference Materials

- **pumas-library** (`/media/jeremy/OrangeCream/Linux Software/Pumas-Library/`):
  - Model registry patterns
  - pumas-rpc HTTP API

## Files to Create

```
godot/scripts/
├── AI/
│   ├── AgentManager.cs        # Semantic Kernel setup
│   ├── AgentConfig.cs         # Configuration
│   ├── Plugins/
│   │   ├── AssetPlugin.cs     # Asset operations
│   │   ├── DiffPlugin.cs      # Diff operations
│   │   ├── EditPlugin.cs      # Property editing
│   │   ├── NavigationPlugin.cs # Tree browsing
│   │   └── ModelPlugin.cs     # pumas-rpc client
│   ├── PumasClient.cs         # HTTP client for pumas-rpc
│   ├── Workflows/
│   │   ├── ModPortingWorkflow.cs
│   │   └── AssetExplorerWorkflow.cs
│   └── README.md
├── Bridge/handlers/
│   └── AgentHandler.cs        # IPC for agent UI
```

## Tasks

### 1. NuGet Dependencies

```xml
<PackageReference Include="Microsoft.SemanticKernel" Version="1.*" />
<PackageReference Include="Microsoft.SemanticKernel.Connectors.Ollama" Version="1.*" />
```

- [ ] Add Semantic Kernel packages
- [ ] Verify Ollama connector works

### 2. Agent Manager

```csharp
public class AgentManager
{
    private readonly Kernel _kernel;
    private readonly IAppLogger _logger;

    public AgentManager(AgentConfig config, IAppLogger logger)
    {
        _logger = logger;
        _kernel = BuildKernel(config);
    }

    private Kernel BuildKernel(AgentConfig config)
    {
        var builder = Kernel.CreateBuilder();

        // Add Ollama or other LLM
        if (config.UseOllama)
        {
            builder.AddOllamaChatCompletion(
                config.OllamaModel,
                new Uri(config.OllamaEndpoint));
        }

        // Add plugins
        builder.Plugins.AddFromObject(new AssetPlugin(_assetManager));
        builder.Plugins.AddFromObject(new DiffPlugin(_diffEngine));
        builder.Plugins.AddFromObject(new EditPlugin(_propertyService));
        builder.Plugins.AddFromObject(new NavigationPlugin(_treeService));
        builder.Plugins.AddFromObject(new ModelPlugin(_pumasClient));

        return builder.Build();
    }

    public async Task<string> ExecuteAsync(string prompt)
    {
        var result = await _kernel.InvokePromptAsync(prompt);
        return result.ToString();
    }
}
```

- [ ] Create AgentManager class
- [ ] Configure Ollama connector
- [ ] Register all plugins
- [ ] Handle errors gracefully

### 3. Asset Plugin

```csharp
public class AssetPlugin
{
    private readonly IAssetManager _assetManager;

    public AssetPlugin(IAssetManager assetManager) => _assetManager = assetManager;

    [KernelFunction("open_asset")]
    [Description("Opens a .uasset file for viewing and editing")]
    public async Task<AssetInfo> OpenAsset(
        [Description("Path to the .uasset file")] string path)
    {
        return await _assetManager.LoadAsset(path);
    }

    [KernelFunction("get_asset_info")]
    [Description("Gets information about the currently loaded asset")]
    public Task<AssetInfo?> GetAssetInfo()
    {
        return Task.FromResult(_assetManager.CurrentAsset);
    }

    [KernelFunction("save_asset")]
    [Description("Saves the current asset to disk")]
    public async Task SaveAsset()
    {
        await _assetManager.Save();
    }

    [KernelFunction("save_asset_as")]
    [Description("Saves the current asset to a new path")]
    public async Task SaveAssetAs(
        [Description("New path to save the asset")] string path)
    {
        await _assetManager.SaveAs(path);
    }
}
```

- [ ] Implement all asset functions
- [ ] Add proper descriptions
- [ ] Handle errors

### 4. Navigation Plugin

```csharp
public class NavigationPlugin
{
    [KernelFunction("get_tree")]
    [Description("Gets the asset tree structure")]
    public async Task<TreeNode[]> GetTree(
        [Description("Optional parent path to get children of")] string? parentPath = null)
    {
        return await _treeService.GetTree(parentPath);
    }

    [KernelFunction("select_node")]
    [Description("Selects a node in the asset tree")]
    public async Task SelectNode(
        [Description("ID of the node to select")] string nodeId)
    {
        await _treeService.Select(nodeId);
    }

    [KernelFunction("search_tree")]
    [Description("Searches the asset tree for nodes matching a query")]
    public async Task<TreeNode[]> SearchTree(
        [Description("Search query")] string query)
    {
        return await _treeService.Search(query);
    }
}
```

- [ ] Implement navigation functions
- [ ] Support tree search

### 5. Edit Plugin

```csharp
public class EditPlugin
{
    [KernelFunction("get_property")]
    [Description("Gets the value of a property by path")]
    public async Task<object?> GetProperty(
        [Description("Property path like 'Export[0].Properties.Health'")] string path)
    {
        return await _propertyService.GetValue(ParsePath(path));
    }

    [KernelFunction("set_property")]
    [Description("Sets the value of a property")]
    public async Task SetProperty(
        [Description("Property path")] string path,
        [Description("New value to set")] object value)
    {
        await _propertyService.SetValue(ParsePath(path), value);
    }

    [KernelFunction("get_properties")]
    [Description("Gets all properties for an export")]
    public async Task<PropertyValue[]> GetProperties(
        [Description("Export index")] int exportIndex)
    {
        return await _propertyService.GetProperties(exportIndex);
    }
}
```

- [ ] Implement property read/write
- [ ] Parse property paths
- [ ] Validate values

### 6. Diff Plugin

```csharp
public class DiffPlugin
{
    [KernelFunction("compare_assets")]
    [Description("Compare two asset versions and return differences")]
    public async Task<DiffResult> CompareAssets(
        [Description("Path to original asset")] string originalPath,
        [Description("Path to updated asset")] string updatedPath)
    {
        return await _diffEngine.ComputeDiff(originalPath, updatedPath);
    }

    [KernelFunction("detect_mod_conflicts")]
    [Description("Detect conflicts between game update and mod changes")]
    public async Task<ConflictResult> DetectConflicts(
        [Description("Path to original game asset")] string originalPath,
        [Description("Path to updated game asset")] string updatedPath,
        [Description("Path to modded asset")] string modPath)
    {
        var gameChanges = await _diffEngine.ComputeDiff(originalPath, updatedPath);
        var modChanges = await _diffEngine.ComputeDiff(originalPath, modPath);
        return await _conflictDetector.DetectConflicts(gameChanges, modChanges);
    }

    [KernelFunction("apply_mod_patches")]
    [Description("Apply non-conflicting mod changes to the updated game asset")]
    public async Task<ApplyResult> ApplyPatches(
        [Description("Path to updated game asset (base)")] string basePath,
        [Description("Patches to apply")] Patch[] patches)
    {
        return await _patchApplier.ApplyPatches(basePath, patches);
    }
}
```

- [ ] Implement diff functions
- [ ] Implement conflict detection
- [ ] Implement patch application

### 7. Pumas Client

```csharp
public class PumasClient
{
    private readonly HttpClient _client;
    private readonly string _baseUrl;

    public PumasClient(string baseUrl = "http://localhost:3001")
    {
        _client = new HttpClient();
        _baseUrl = baseUrl;
    }

    public async Task<List<ModelInfo>> ListModelsAsync()
    {
        var response = await _client.GetAsync($"{_baseUrl}/api/models");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ModelInfo>>()
            ?? new List<ModelInfo>();
    }

    public async Task<List<ModelInfo>> SearchModelsAsync(string query)
    {
        var response = await _client.GetAsync($"{_baseUrl}/api/models/search?q={Uri.EscapeDataString(query)}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<ModelInfo>>()
            ?? new List<ModelInfo>();
    }

    public async Task<string> StartDownloadAsync(HfDownloadRequest request)
    {
        var response = await _client.PostAsJsonAsync($"{_baseUrl}/api/hf/download", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<DownloadResponse>();
        return result?.DownloadId ?? throw new InvalidOperationException("No download ID returned");
    }

    public async Task<DownloadProgress> GetDownloadProgressAsync(string downloadId)
    {
        var response = await _client.GetAsync($"{_baseUrl}/api/hf/download/{downloadId}/progress");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<DownloadProgress>()
            ?? throw new InvalidOperationException("No progress returned");
    }
}
```

- [ ] Implement HTTP client
- [ ] Handle connection errors
- [ ] Add retry logic

### 8. Model Plugin

```csharp
public class ModelPlugin
{
    private readonly PumasClient _pumas;

    [KernelFunction("search_models")]
    [Description("Search for AI models in the local library")]
    public async Task<List<ModelInfo>> SearchModels(
        [Description("Search query (name, family, or description)")] string query)
    {
        return await _pumas.SearchModelsAsync(query);
    }

    [KernelFunction("list_available_models")]
    [Description("List all AI models available locally")]
    public async Task<List<ModelInfo>> ListModels()
    {
        return await _pumas.ListModelsAsync();
    }

    [KernelFunction("download_model")]
    [Description("Download a model from HuggingFace")]
    public async Task<string> DownloadModel(
        [Description("HuggingFace repository ID")] string repoId,
        [Description("Specific file to download")] string filename)
    {
        var request = new HfDownloadRequest { RepoId = repoId, Filename = filename };
        return await _pumas.StartDownloadAsync(request);
    }
}
```

- [ ] Implement model search
- [ ] Implement model listing
- [ ] Implement download trigger

### 9. Mod Porting Workflow

```csharp
public class ModPortingWorkflow
{
    private readonly AgentManager _agent;

    public async Task<WorkflowResult> ExecuteAsync(
        string originalPath,
        string updatedPath,
        string modPath,
        string outputPath)
    {
        var prompt = $@"
Port the mod from game v1.0 to v1.1.

Original game asset: {originalPath}
Updated game asset: {updatedPath}
Modded asset: {modPath}
Output path: {outputPath}

Steps:
1. Compare original and updated to find game changes
2. Compare original and modded to find mod changes
3. Detect conflicts between game and mod changes
4. For non-conflicting changes, apply to the updated base
5. Report any conflicts that need manual resolution
6. Save the result

Be careful and report your findings.
";

        return await _agent.ExecuteAsync(prompt);
    }
}
```

- [ ] Create workflow class
- [ ] Define prompts
- [ ] Handle workflow state

### 10. Agent IPC Handler

```csharp
public class AgentHandler : IMessageHandler
{
    public async Task<object> Handle(IpcMessage message)
    {
        return message.Action switch
        {
            "execute" => await ExecutePrompt(message.Payload),
            "portMod" => await PortMod(message.Payload),
            "cancel" => CancelExecution(),
            "getStatus" => GetStatus(),
            _ => throw new NotSupportedException()
        };
    }
}
```

- [ ] Implement execute prompt
- [ ] Implement workflows
- [ ] Send progress updates to UI

## Agent UI Integration

The frontend displays agent status:

```typescript
// view-models/agent.svelte.ts
export let agentStatus = $state<'idle' | 'thinking' | 'executing' | 'complete' | 'error'>('idle');
export let agentMessage = $state<string>('');
export let agentProgress = $state<number>(0);

ipc.on('agent', (data: AgentMessage) => {
    agentStatus = data.status;
    agentMessage = data.message;
    agentProgress = data.progress ?? 0;
});

export function executePrompt(prompt: string) {
    ipc.send({ type: 'agent', action: 'execute', payload: { prompt } });
}
```

## Local LLM Requirements

**Minimum specs:**
- 7B parameter model
- 8GB VRAM (GPU) or 16GB RAM (CPU)
- Tool/function calling support

**Recommended models:**
- Mistral 7B Instruct
- Llama 3.1 8B Instruct
- Qwen 2.5 7B

## Testing

- [ ] Unit test: Plugin function execution
- [ ] Integration test: Agent with mock LLM
- [ ] Integration test: pumas-rpc connection
- [ ] End-to-end: Mod porting workflow

## Acceptance Criteria

- [ ] Semantic Kernel initializes with Ollama
- [ ] All plugins registered and callable
- [ ] Agent can browse assets via prompts
- [ ] Agent can read/write properties
- [ ] Agent can compare assets
- [ ] pumas-rpc client connects
- [ ] Model search works
- [ ] Mod porting workflow executes
- [ ] Progress reported to UI
- [ ] Errors handled gracefully

## Example Agent Interaction

```
User: "Port my mod from game v1.0 to v1.1"

Agent thinking...
[open_asset] Loading original game asset (v1.0)
[open_asset] Loading updated game asset (v1.1)
[compare_assets] Computing diff between v1.0 and v1.1
  Found: 3 modified, 1 added, 0 removed
[open_asset] Loading modded asset
[compare_assets] Computing diff between original and mod
  Found: 5 modified
[detect_mod_conflicts] Analyzing conflicts
  Non-conflicting: 4 changes
  Conflicting: 1 change (Health property)
[apply_mod_patches] Applying 4 non-conflicting patches
[save_asset_as] Saving to output path

Complete!
- Applied 4 mod changes automatically
- 1 conflict needs manual resolution:
  - Health: Game changed 100→150, Mod changed 100→200
  - Recommendation: Review which value is correct
```
