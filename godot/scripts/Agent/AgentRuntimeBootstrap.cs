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

        var capabilities = BuildCapabilities(logger, dispatcher);
        var modelLibrary = InitializeModelLibrary(logger, launcherRoot);
        var manager = InitializeManager(logger, assetManager, capabilities, modelLibrary);
        var handler = new AgentHandler(logger, manager, dispatcher.Send);

        logger.Info(
            "Agent runtime bootstrap complete (manager={Manager}, capabilities={Capabilities}, modelLibraryAvailable={ModelAvailable})",
            manager != null, capabilities != null, modelLibrary.IsAvailable);

        return new AgentRuntimeContext(capabilities, manager, modelLibrary, handler);
    }

    private static AgentCapabilityRegistry? BuildCapabilities(IAppLogger logger, IpcDispatcher dispatcher)
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
            new ProjectExplorerCapability(projectPathProvider, new FileTreeBuilder(logger), logger),
            new DependencyGraphCapability(projectPathProvider, dataAccess, logger),
            new MetadataCapability(projectPathProvider, dataAccess, logger),
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
        AssetManager assetManager,
        AgentCapabilityRegistry? capabilities,
        IModelLibrary modelLibrary)
    {
        try
        {
            var propertyService = new PropertyService(logger);
            var manager = AgentManager.Create(
                AgentConfig.Default,
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
}
