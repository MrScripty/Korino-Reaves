// Asset Explorer Workflow
//
// Allows the AI agent to explore and describe asset contents
// in response to natural language queries.

using System;
using System.Threading;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Agent.Workflows;

/// <summary>
/// Orchestrates AI-driven asset exploration and analysis.
/// </summary>
public sealed class AssetExplorerWorkflow
{
    private const string ExplorePromptTemplate = """
        You are an Unreal Engine asset analyst.

        Asset to explore: {0}

        User question: {1}

        Use the available tools to:
        1. Open the asset file
        2. Browse the tree structure
        3. Read properties as needed
        4. Answer the user's question based on what you find

        Provide a clear, concise answer with specific property names and values.
        """;

    private readonly AgentManager _agent;
    private readonly IAppLogger _logger;

    public AssetExplorerWorkflow(AgentManager agent, IAppLogger logger)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Explores an asset and answers a question about it.
    /// </summary>
    public async Task<string> ExploreAsync(
        string assetPath,
        string question,
        CancellationToken ct = default)
    {
        using var scope = _logger.BeginScope("AssetExplorerWorkflow");

        _logger.Info("Exploring asset: {Path}, question: {Question}", assetPath, question);

        try
        {
            var prompt = string.Format(ExplorePromptTemplate, assetPath, question);
            return await _agent.ExecuteAsync(prompt, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return "Exploration cancelled.";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Asset exploration failed");
            return $"Exploration failed: {ex.Message}";
        }
    }
}
