// Model Plugin - Semantic Kernel functions for AI model management
//
// Exposes pumas-core model library operations to the AI agent.

using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.SemanticKernel;

namespace UAssetViewer.Agent.Plugins;

/// <summary>
/// Semantic Kernel plugin for managing local AI models via pumas-core.
/// </summary>
public sealed class ModelPlugin
{
    private readonly IModelLibrary _library;

    public ModelPlugin(IModelLibrary library)
    {
        _library = library;
    }

    [KernelFunction("list_local_models")]
    [Description("Lists all AI models available in the local model library.")]
    public async Task<string> ListModels()
    {
        var models = await _library.ListModelsAsync().ConfigureAwait(false);
        return JsonSerializer.Serialize(models);
    }

    [KernelFunction("search_local_models")]
    [Description("Searches the local model library using full-text search.")]
    public async Task<string> SearchLocalModels(
        [Description("Search query (model name, family, or tags)")] string query,
        [Description("Maximum number of results")] int limit = 10)
    {
        var result = await _library.SearchModelsAsync(query, limit).ConfigureAwait(false);
        return JsonSerializer.Serialize(result);
    }

    [KernelFunction("search_huggingface")]
    [Description("Searches HuggingFace for AI models. Requires network connectivity.")]
    public async Task<string> SearchHuggingFace(
        [Description("Search query")] string query,
        [Description("Optional model type filter (e.g. 'text-generation')")] string? kind = null,
        [Description("Maximum number of results")] int limit = 5)
    {
        var models = await _library.SearchHuggingFaceAsync(query, kind, limit).ConfigureAwait(false);
        return JsonSerializer.Serialize(models);
    }

    [KernelFunction("download_model")]
    [Description("Starts downloading a model from HuggingFace. Returns a download ID to track progress.")]
    public async Task<string> DownloadModel(
        [Description("HuggingFace repository ID (e.g. 'TheBloke/Mistral-7B-Instruct-v0.2-GGUF')")] string repoId,
        [Description("Model family name")] string family,
        [Description("Official model name")] string officialName,
        [Description("Quantization variant (e.g. 'Q4_K_M')")] string? quant = null,
        [Description("Specific filename to download")] string? filename = null)
    {
        var request = new DownloadRequest(repoId, family, officialName, Quant: quant, Filename: filename);
        var downloadId = await _library.StartDownloadAsync(request).ConfigureAwait(false);
        return JsonSerializer.Serialize(new { downloadId, status = "started" });
    }

    [KernelFunction("check_download_progress")]
    [Description("Gets the progress of an active model download.")]
    public async Task<string> CheckDownloadProgress(
        [Description("Download ID returned by download_model")] string downloadId)
    {
        var progress = await _library.GetDownloadProgressAsync(downloadId).ConfigureAwait(false);
        return progress != null
            ? JsonSerializer.Serialize(progress)
            : JsonSerializer.Serialize(new { status = "not_found" });
    }

    [KernelFunction("is_model_library_available")]
    [Description("Checks if the model library is connected and operational.")]
    public bool IsAvailable()
    {
        return _library.IsAvailable;
    }
}
