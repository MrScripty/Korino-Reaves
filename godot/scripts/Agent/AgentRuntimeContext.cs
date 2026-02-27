// Agent Runtime Context
//
// Holds initialized runtime services for agent operations.

using System;

namespace UAssetViewer.Agent;

/// <summary>
/// Runtime context for agent integration.
/// </summary>
public sealed class AgentRuntimeContext : IDisposable
{
    private readonly IDisposable? _modelLibraryDisposable;
    private bool _disposed;

    public AgentRuntimeContext(
        AgentCapabilityRegistry? capabilities,
        AgentManager? manager,
        IModelLibrary modelLibrary,
        AgentHandler handler)
    {
        Capabilities = capabilities;
        Manager = manager;
        ModelLibrary = modelLibrary ?? throw new ArgumentNullException(nameof(modelLibrary));
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _modelLibraryDisposable = modelLibrary as IDisposable;
    }

    /// <summary>
    /// Composed capability registry for future plugin integration.
    /// </summary>
    public AgentCapabilityRegistry? Capabilities { get; }

    /// <summary>
    /// Initialized agent manager, or null when initialization failed.
    /// </summary>
    public AgentManager? Manager { get; }

    /// <summary>
    /// Backing model library used by the manager.
    /// </summary>
    public IModelLibrary ModelLibrary { get; }

    /// <summary>
    /// IPC handler registered with dispatcher.
    /// </summary>
    public AgentHandler Handler { get; }

    /// <summary>
    /// True when agent manager was created successfully.
    /// </summary>
    public bool IsInitialized => Manager != null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Manager?.Dispose();
        _modelLibraryDisposable?.Dispose();
    }
}
