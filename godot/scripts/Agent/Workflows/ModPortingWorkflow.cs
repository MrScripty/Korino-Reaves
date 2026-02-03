// Mod Porting Workflow
//
// Orchestrates the AI agent to port mods between game versions.
// Uses diff analysis to identify changes, detect conflicts,
// and apply non-conflicting patches automatically.

using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UAssetViewer.Infrastructure;

namespace UAssetViewer.Agent.Workflows;

/// <summary>
/// Result of a mod porting workflow execution.
/// </summary>
public record WorkflowResult(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("appliedChanges")] int AppliedChanges,
    [property: JsonPropertyName("conflicts")] int Conflicts,
    [property: JsonPropertyName("outputPath")] string? OutputPath = null,
    [property: JsonPropertyName("error")] string? Error = null
);

/// <summary>
/// Orchestrates AI-driven mod porting between game versions.
/// </summary>
public sealed class ModPortingWorkflow
{
    private const string PortingPromptTemplate = """
        You are a mod porting assistant for Unreal Engine assets.

        Port the mod from the original game version to the updated game version.

        Original game asset: {0}
        Updated game asset: {1}
        Modded asset (based on original): {2}
        Output path: {3}

        Follow these steps:
        1. Open the original game asset and examine its properties
        2. Compare the original and updated game assets to find what the game changed
        3. Compare the original and modded assets to find what the mod changed
        4. Detect conflicts where both the game and mod changed the same property
        5. For non-conflicting mod changes, apply them to the updated game asset
        6. Save the result to the output path
        7. Report your findings including any conflicts that need manual resolution

        Be methodical and report each step as you perform it.
        """;

    private readonly AgentManager _agent;
    private readonly IAppLogger _logger;

    public ModPortingWorkflow(AgentManager agent, IAppLogger logger)
    {
        _agent = agent ?? throw new ArgumentNullException(nameof(agent));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the mod porting workflow.
    /// </summary>
    public async Task<WorkflowResult> ExecuteAsync(
        string originalPath,
        string updatedPath,
        string modPath,
        string outputPath,
        CancellationToken ct = default)
    {
        using var scope = _logger.BeginScope("ModPortingWorkflow");

        _logger.Info("Starting mod porting: {Original} -> {Updated}, mod: {Mod}",
            originalPath, updatedPath, modPath);

        try
        {
            var prompt = string.Format(
                PortingPromptTemplate,
                originalPath, updatedPath, modPath, outputPath);

            var response = await _agent.ExecuteAsync(prompt, ct).ConfigureAwait(false);

            _logger.Info("Mod porting workflow completed");

            return new WorkflowResult(
                Success: true,
                Message: response,
                AppliedChanges: 0, // Would be parsed from agent response
                Conflicts: 0
            );
        }
        catch (OperationCanceledException)
        {
            return new WorkflowResult(false, "Workflow cancelled", 0, 0);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Mod porting workflow failed");
            return new WorkflowResult(false, "Workflow failed", 0, 0, Error: ex.Message);
        }
    }
}
