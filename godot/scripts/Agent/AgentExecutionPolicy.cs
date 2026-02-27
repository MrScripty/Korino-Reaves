// Agent Execution Policy
//
// Defines guardrails for agent side effects and bounded query behavior.

using System;

namespace UAssetViewer.Agent;

/// <summary>
/// Policy controlling what side effects the agent may perform and
/// the maximum bounds for read-oriented queries.
/// </summary>
public sealed record AgentExecutionPolicy
{
    /// <summary>
    /// Allows persistent asset file mutations (save/save-as/export).
    /// Disabled by default for early read-only rollout.
    /// </summary>
    public bool AllowAssetWriteOperations { get; init; }

    /// <summary>
    /// Allows in-memory property edits on loaded assets.
    /// Disabled by default for early read-only rollout.
    /// </summary>
    public bool AllowPropertyEdits { get; init; }

    /// <summary>
    /// Allows model downloads and local model library mutation.
    /// Disabled by default for early read-only rollout.
    /// </summary>
    public bool AllowModelDownloads { get; init; }

    /// <summary>
    /// Allows GUI selection/expansion mutations.
    /// Enabled by default because this is a required non-persistent control path.
    /// </summary>
    public bool AllowGuiMutation { get; init; } = true;

    /// <summary>
    /// Upper bound for project tree search result size.
    /// </summary>
    public int MaxProjectSearchResults { get; init; } = 1000;

    /// <summary>
    /// Upper bound for dependency query result size.
    /// </summary>
    public int MaxDependencyQueryResults { get; init; } = 1000;

    /// <summary>
    /// Upper bound for related-assets traversal result size.
    /// </summary>
    public int MaxDependencyRelatedResults { get; init; } = 2000;

    /// <summary>
    /// Upper bound for dependency traversal depth.
    /// </summary>
    public int MaxDependencyTraversalDepth { get; init; } = 8;

    /// <summary>
    /// Upper bound for metadata rows returned per table.
    /// </summary>
    public int MaxMetadataRows { get; init; } = 2000;

    /// <summary>
    /// Read-only default policy for safe initial rollout.
    /// </summary>
    public static AgentExecutionPolicy ReadOnlyDefault => new();

    /// <summary>
    /// Write-enabled policy that can be used for later rollout stages.
    /// </summary>
    public static AgentExecutionPolicy WriteEnabledDefault => new()
    {
        AllowAssetWriteOperations = true,
        AllowPropertyEdits = true,
        AllowModelDownloads = true
    };

    public int ClampProjectSearchLimit(int requested, int fallback) =>
        Clamp(requested, fallback, MaxProjectSearchResults);

    public int ClampDependencyQueryLimit(int requested, int fallback) =>
        Clamp(requested, fallback, MaxDependencyQueryResults);

    public int ClampDependencyRelatedLimit(int requested, int fallback) =>
        Clamp(requested, fallback, MaxDependencyRelatedResults);

    public int ClampDependencyTraversalDepth(int requested, int fallback) =>
        Clamp(requested, fallback, MaxDependencyTraversalDepth);

    public int ClampMetadataRowLimit(int requested, int fallback) =>
        Clamp(requested, fallback, MaxMetadataRows);

    public void EnsureAssetWritesAllowed(string operation)
    {
        EnsureAllowed(AllowAssetWriteOperations, operation, "asset write operations");
    }

    public void EnsurePropertyEditsAllowed(string operation)
    {
        EnsureAllowed(AllowPropertyEdits, operation, "property edits");
    }

    public void EnsureModelDownloadsAllowed(string operation)
    {
        EnsureAllowed(AllowModelDownloads, operation, "model downloads");
    }

    public void EnsureGuiMutationAllowed(string operation)
    {
        EnsureAllowed(AllowGuiMutation, operation, "GUI mutation");
    }

    private static int Clamp(int requested, int fallback, int max)
    {
        if (requested <= 0)
        {
            return fallback;
        }

        return Math.Min(requested, Math.Max(1, max));
    }

    private static void EnsureAllowed(bool allowed, string operation, string capability)
    {
        if (allowed)
        {
            return;
        }

        throw new AgentPolicyViolationException(
            $"Agent execution policy blocks '{operation}' ({capability}) in read-only mode.");
    }
}

/// <summary>
/// Raised when a side-effecting agent action is blocked by policy.
/// </summary>
public sealed class AgentPolicyViolationException : InvalidOperationException
{
    public AgentPolicyViolationException(string message)
        : base(message)
    {
    }
}
