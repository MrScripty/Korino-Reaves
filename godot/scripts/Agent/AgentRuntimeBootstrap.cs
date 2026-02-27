// Agent Runtime Bootstrap
//
// Composes capability registry and agent runtime with safe fallback behavior.

using System;
using System.IO;
using UAssetViewer.Agent.Capabilities;
using UAssetViewer.Assets;
using UAssetViewer.Bridge;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Agent;

/// <summary>
/// Factory for initializing agent runtime services.
/// </summary>
public static class AgentRuntimeBootstrap
{
    /// <summary>
    /// Creates runtime context and returns a handler that is always safe to register.
    /// If initialization fails, the returned handler reports unavailable state.
    /// </summary>
    public static AgentRuntimeContext Create(
        IAppLogger logger,
        IpcDispatcher dispatcher,
        AssetManager assetManager,
        string launcherRoot)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(assetManager);
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherRoot);

        var config = BuildConfig(logger);
        var capabilities = BuildCapabilities(logger, dispatcher, config.ExecutionPolicy);
        var modelLibrary = InitializeModelLibrary(logger, launcherRoot);
        var manager = InitializeManager(logger, config, assetManager, capabilities, modelLibrary);
        var handler = new AgentHandler(logger, manager, dispatcher.Send);

        logger.Info(
            "Agent runtime bootstrap complete (manager={Manager}, capabilities={Capabilities}, modelLibraryAvailable={ModelAvailable}, writeOps={WriteOps}, propertyEdits={PropertyEdits}, modelDownloads={ModelDownloads}, guiMutation={GuiMutation})",
            manager != null,
            capabilities != null,
            modelLibrary.IsAvailable,
            config.ExecutionPolicy.AllowAssetWriteOperations,
            config.ExecutionPolicy.AllowPropertyEdits,
            config.ExecutionPolicy.AllowModelDownloads,
            config.ExecutionPolicy.AllowGuiMutation);

        return new AgentRuntimeContext(capabilities, manager, modelLibrary, handler);
    }

    private static AgentCapabilityRegistry? BuildCapabilities(
        IAppLogger logger,
        IpcDispatcher dispatcher,
        AgentExecutionPolicy policy)
    {
        var projectHandler = dispatcher.GetHandler<ProjectHandler>();
        var selectionHandler = dispatcher.GetHandler<SelectionHandler>();
        if (projectHandler == null || selectionHandler == null)
        {
            logger.Warning(
                "Skipping agent capability registry - required handlers missing (project={HasProject}, selection={HasSelection})",
                projectHandler != null,
                selectionHandler != null);
            return null;
        }

        var projectPathProvider = new ProjectHandlerPathProvider(projectHandler);
        var dataAccess = new DependencyDatabaseDataAccess(logger);
        var selectionController = new SelectionHandlerController(selectionHandler);
        var selectionBroadcaster = new IpcSelectionBroadcaster(dispatcher);

        return new AgentCapabilityRegistry(
            new ProjectExplorerCapability(projectPathProvider, new FileTreeBuilder(logger), logger, policy),
            new DependencyGraphCapability(projectPathProvider, dataAccess, logger, policy),
            new MetadataCapability(projectPathProvider, dataAccess, logger, policy),
            new GuiSelectionCapability(selectionController, selectionBroadcaster, logger));
    }

    private static IModelLibrary InitializeModelLibrary(IAppLogger logger, string launcherRoot)
    {
        try
        {
            var normalizedRoot = Path.GetFullPath(launcherRoot);
            var library = new PumasModelLibrary(normalizedRoot, logger);
            library.InitializeAsync().GetAwaiter().GetResult();
            logger.Info("Agent model library initialized");
            return library;
        }
        catch (Exception ex)
        {
            logger.Warning("Agent model library unavailable: {Message}", ex.Message);
            return new NoOpModelLibrary($"Model library unavailable: {ex.Message}");
        }
    }

    private static AgentManager? InitializeManager(
        IAppLogger logger,
        AgentConfig config,
        AssetManager assetManager,
        AgentCapabilityRegistry? capabilities,
        IModelLibrary modelLibrary)
    {
        try
        {
            var propertyService = new PropertyService(logger);
            var manager = AgentManager.Create(
                config,
                assetManager,
                assetManager,
                propertyService,
                capabilities,
                modelLibrary,
                logger);
            return manager;
        }
        catch (Exception ex)
        {
            logger.Warning("Agent manager unavailable: {Message}", ex.Message);
            return null;
        }
    }

    private static AgentConfig BuildConfig(IAppLogger logger)
    {
        var defaults = AgentExecutionPolicy.ReadOnlyDefault;
        var allowWrites = GetEnvironmentBool("KORINO_AGENT_ALLOW_WRITES");

        var policy = defaults with
        {
            AllowAssetWriteOperations = GetEnvironmentBool("KORINO_AGENT_ALLOW_ASSET_WRITES")
                ?? allowWrites
                ?? defaults.AllowAssetWriteOperations,
            AllowPropertyEdits = GetEnvironmentBool("KORINO_AGENT_ALLOW_PROPERTY_EDITS")
                ?? allowWrites
                ?? defaults.AllowPropertyEdits,
            AllowModelDownloads = GetEnvironmentBool("KORINO_AGENT_ALLOW_MODEL_DOWNLOADS")
                ?? allowWrites
                ?? defaults.AllowModelDownloads,
            AllowGuiMutation = GetEnvironmentBool("KORINO_AGENT_ALLOW_GUI_MUTATION")
                ?? defaults.AllowGuiMutation,
            MaxProjectSearchResults = GetEnvironmentInt("KORINO_AGENT_MAX_PROJECT_SEARCH_RESULTS", defaults.MaxProjectSearchResults),
            MaxDependencyQueryResults = GetEnvironmentInt("KORINO_AGENT_MAX_DEPENDENCY_RESULTS", defaults.MaxDependencyQueryResults),
            MaxDependencyRelatedResults = GetEnvironmentInt("KORINO_AGENT_MAX_DEPENDENCY_RELATED_RESULTS", defaults.MaxDependencyRelatedResults),
            MaxDependencyTraversalDepth = GetEnvironmentInt("KORINO_AGENT_MAX_DEPENDENCY_DEPTH", defaults.MaxDependencyTraversalDepth),
            MaxMetadataRows = GetEnvironmentInt("KORINO_AGENT_MAX_METADATA_ROWS", defaults.MaxMetadataRows)
        };

        logger.Info(
            "Agent execution policy configured (readOnly={ReadOnly}, assetWrites={AssetWrites}, propertyEdits={PropertyEdits}, modelDownloads={ModelDownloads}, guiMutation={GuiMutation})",
            !policy.AllowAssetWriteOperations && !policy.AllowPropertyEdits && !policy.AllowModelDownloads,
            policy.AllowAssetWriteOperations,
            policy.AllowPropertyEdits,
            policy.AllowModelDownloads,
            policy.AllowGuiMutation);

        return new AgentConfig
        {
            ExecutionPolicy = policy
        };
    }

    private static bool? GetEnvironmentBool(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private static int GetEnvironmentInt(string key, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
