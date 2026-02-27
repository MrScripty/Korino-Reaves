using System.Threading.Tasks;
using Godot;
using UAssetViewer.Assets;
using UAssetViewer.Bridge;
using UAssetViewer.Bridge.Handlers;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers
{
    public sealed class AssetHandler : IMessageHandler
    {
        public AssetHandler(IAppLogger logger, AssetManager assetManager) { }
        public string MessageType => MessageTypes.Asset;
        public bool CanHandle(string action) => false;
        public Task<IpcMessage?> HandleAsync(IpcMessage message) => Task.FromResult<IpcMessage?>(null);
    }

    public sealed class SelectionHandler : IMessageHandler
    {
        public SelectionHandler(IAppLogger logger) { }
        public string MessageType => MessageTypes.Selection;
        public bool CanHandle(string action) => false;
        public Task<IpcMessage?> HandleAsync(IpcMessage message) => Task.FromResult<IpcMessage?>(null);
        public SelectionState CurrentState { get; } = new(null, System.Array.Empty<string>());
        public SelectionState SelectNode(string? nodeId) => CurrentState;
        public SelectionState ExpandNodes(string[] nodeIds) => CurrentState;
        public SelectionState CollapseNodes(string[] nodeIds) => CurrentState;
        public SelectionState CollapseAll() => CurrentState;
    }

    public sealed class TreeHandler : IMessageHandler
    {
        public TreeHandler(IAppLogger logger, AssetManager assetManager, IpcDispatcher dispatcher, SelectionHandler selectionHandler) { }
        public string MessageType => MessageTypes.Tree;
        public bool CanHandle(string action) => false;
        public Task<IpcMessage?> HandleAsync(IpcMessage message) => Task.FromResult<IpcMessage?>(null);
    }

    public sealed class PakHandler : IMessageHandler
    {
        public PakHandler(IAppLogger logger, IpcDispatcher dispatcher) { }
        public string MessageType => MessageTypes.Pak;
        public bool CanHandle(string action) => false;
        public Task<IpcMessage?> HandleAsync(IpcMessage message) => Task.FromResult<IpcMessage?>(null);
    }

    public sealed class ProjectHandler : IMessageHandler
    {
        public ProjectHandler(IAppLogger logger, IpcDispatcher dispatcher) { }
        public string MessageType => MessageTypes.Project;
        public bool CanHandle(string action) => false;
        public Task<IpcMessage?> HandleAsync(IpcMessage message) => Task.FromResult<IpcMessage?>(null);
    }

    public sealed class FilesystemHandler : IMessageHandler
    {
        public FilesystemHandler(IAppLogger logger) { }
        public string MessageType => "filesystem";
        public bool CanHandle(string action) => false;
        public Task<IpcMessage?> HandleAsync(IpcMessage message) => Task.FromResult<IpcMessage?>(null);
    }

    public sealed class DependencyHandler : IMessageHandler
    {
        public DependencyHandler(IAppLogger logger, IpcDispatcher dispatcher) { }
        public string MessageType => MessageTypes.Dependency;
        public bool CanHandle(string action) => false;
        public Task<IpcMessage?> HandleAsync(IpcMessage message) => Task.FromResult<IpcMessage?>(null);
    }

    public sealed class DialogHandler : IMessageHandler
    {
        public DialogHandler(IAppLogger logger, IpcDispatcher dispatcher, Node sceneRoot) { }
        public string MessageType => MessageTypes.Dialog;
        public bool CanHandle(string action) => false;
        public Task<IpcMessage?> HandleAsync(IpcMessage message) => Task.FromResult<IpcMessage?>(null);
    }
}

namespace UAssetViewer.Diff
{
    public sealed class DiffHandler : IMessageHandler
    {
        public DiffHandler(IAppLogger logger, AssetManager assetManager) { }
        public string MessageType => MessageTypes.Diff;
        public bool CanHandle(string action) => false;
        public Task<IpcMessage?> HandleAsync(IpcMessage message) => Task.FromResult<IpcMessage?>(null);
    }
}
