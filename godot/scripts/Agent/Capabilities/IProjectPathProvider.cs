// Project Path Provider
//
// Provides the currently open project path to agent capabilities.

namespace UAssetViewer.Agent.Capabilities;

/// <summary>
/// Resolves the currently open project path.
/// </summary>
public interface IProjectPathProvider
{
    /// <summary>
    /// Gets the absolute path to the open project root, or null when no project is open.
    /// </summary>
    string? CurrentProjectPath { get; }
}
