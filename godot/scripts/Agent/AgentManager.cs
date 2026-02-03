// Agent Manager - Semantic Kernel orchestration
//
// Initializes the Semantic Kernel with an Ollama LLM connector,
// registers all plugins, and provides prompt execution.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Ollama;
using UAssetViewer.Agent.Plugins;
using UAssetViewer.Assets;
using UAssetViewer.Infrastructure;
using UAssetViewer.Services;

namespace UAssetViewer.Agent;

/// <summary>
/// Manages the Semantic Kernel instance and AI agent operations.
/// Registers asset, navigation, edit, diff, and model plugins.
/// </summary>
public sealed class AgentManager : IDisposable
{
    private readonly Kernel _kernel;
    private readonly IAppLogger _logger;
    private readonly AgentConfig _config;
    private bool _disposed;

    private AgentManager(Kernel kernel, AgentConfig config, IAppLogger logger)
    {
        _kernel = kernel;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Creates and initializes an AgentManager with all plugins registered.
    /// </summary>
    public static AgentManager Create(
        AgentConfig config,
        IAssetService assetService,
        ITreeService treeService,
        PropertyService propertyService,
        IModelLibrary modelLibrary,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(assetService);
        ArgumentNullException.ThrowIfNull(treeService);
        ArgumentNullException.ThrowIfNull(propertyService);
        ArgumentNullException.ThrowIfNull(modelLibrary);
        ArgumentNullException.ThrowIfNull(logger);

        logger.Info("Initializing AgentManager with Ollama model: {Model}", config.OllamaModel);

        var builder = Kernel.CreateBuilder();

        if (config.UseOllama)
        {
            builder.AddOllamaChatCompletion(
                config.OllamaModel,
                new Uri(config.OllamaEndpoint));
        }

        builder.Plugins.AddFromObject(new AssetPlugin(assetService), "Asset");
        builder.Plugins.AddFromObject(new NavigationPlugin(treeService), "Navigation");
        builder.Plugins.AddFromObject(new EditPlugin(propertyService, assetService), "Edit");
        builder.Plugins.AddFromObject(new ModelPlugin(modelLibrary), "Model");

        var kernel = builder.Build();

        logger.Info("AgentManager initialized with {Count} plugins", kernel.Plugins.Count);
        return new AgentManager(kernel, config, logger);
    }

    /// <summary>
    /// Registers the DiffPlugin. Called separately because the diff engine
    /// is initialized after the asset layer.
    /// </summary>
    public void RegisterDiffPlugin(object diffEngine)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _kernel.Plugins.AddFromObject(new DiffPlugin(diffEngine), "Diff");
        _logger.Info("DiffPlugin registered");
    }

    /// <summary>
    /// Executes a natural language prompt using the AI agent.
    /// The agent can call any registered plugin function.
    /// </summary>
    public async Task<string> ExecuteAsync(string prompt, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var scope = _logger.BeginScope("AgentExecute");
        _logger.Info("Executing prompt: {Prompt}", prompt.Length > 100 ? prompt[..100] + "..." : prompt);

        try
        {
            var settings = new OllamaPromptExecutionSettings
            {
                Temperature = _config.Temperature,
            };

            var result = await _kernel.InvokePromptAsync(
                prompt,
                new KernelArguments(settings),
                cancellationToken: ct).ConfigureAwait(false);

            var response = result.ToString();
            _logger.Info("Agent response length: {Length}", response.Length);
            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.Info("Agent execution cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Agent execution failed");
            throw;
        }
    }

    /// <summary>
    /// Gets the underlying Kernel for advanced scenarios.
    /// </summary>
    public Kernel Kernel => _kernel;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
