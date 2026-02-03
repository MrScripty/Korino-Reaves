// Agent Configuration
//
// Configuration settings for the AI agent system including
// LLM provider settings and pumas-library connection.

namespace UAssetViewer.Agent;

/// <summary>
/// Configuration for the AI agent system.
/// </summary>
public sealed class AgentConfig
{
    /// <summary>
    /// Whether to use Ollama as the LLM provider.
    /// </summary>
    public bool UseOllama { get; init; } = true;

    /// <summary>
    /// Ollama server endpoint URL.
    /// </summary>
    public string OllamaEndpoint { get; init; } = "http://localhost:11434";

    /// <summary>
    /// Model name to use with Ollama (must support tool calling).
    /// </summary>
    public string OllamaModel { get; init; } = "mistral:7b-instruct";

    /// <summary>
    /// pumas-rpc server endpoint URL.
    /// </summary>
    public string PumasEndpoint { get; init; } = "http://localhost:3001";

    /// <summary>
    /// Maximum tokens for completion responses.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Temperature for LLM responses (0.0 = deterministic, 1.0 = creative).
    /// </summary>
    public float Temperature { get; init; } = 0.1f;

    /// <summary>
    /// Timeout in seconds for LLM requests.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// Whether to enable verbose logging of agent operations.
    /// </summary>
    public bool VerboseLogging { get; init; }

    /// <summary>
    /// Creates a default configuration.
    /// </summary>
    public static AgentConfig Default => new();

    /// <summary>
    /// Creates a configuration for development/testing.
    /// </summary>
    public static AgentConfig Development => new()
    {
        VerboseLogging = true,
        TimeoutSeconds = 300
    };
}

/// <summary>
/// LLM provider types supported by the agent system.
/// </summary>
public enum LlmProvider
{
    /// <summary>
    /// Local Ollama server.
    /// </summary>
    Ollama,

    /// <summary>
    /// OpenAI-compatible API (LM Studio, etc.).
    /// </summary>
    OpenAiCompatible
}
