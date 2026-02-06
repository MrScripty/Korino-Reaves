// Project Config - Persistent per-project settings
//
// Stored at projects/<name>/usr/config.json alongside UE_data/.
// Keeps user configuration separate from extracted game files.

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UAssetViewer.Models;

/// <summary>
/// Persistent configuration for a project.
/// Stored at projects/{name}/usr/config.json.
/// </summary>
public sealed class ProjectConfig
{
    [JsonPropertyName("gameVersion")]
    public string? GameVersion { get; set; }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Resolves the config file path from a project's UE_data path.
    /// The project root is the parent of UE_data, and config lives in usr/.
    /// </summary>
    private static string GetConfigPath(string projectPath)
    {
        // projectPath points to UE_data (or the project root itself)
        // Go up to the project root, then into usr/config.json
        var projectRoot = projectPath;

        // If projectPath ends with UE_data, go up one level
        var dirName = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(dirName, "UE_data", StringComparison.OrdinalIgnoreCase))
        {
            projectRoot = Path.GetDirectoryName(projectPath) ?? projectPath;
        }

        return Path.Combine(projectRoot, "usr", "config.json");
    }

    /// <summary>
    /// Loads config from the project's usr/config.json.
    /// Returns a default config if the file doesn't exist.
    /// </summary>
    public static ProjectConfig Load(string projectPath)
    {
        var configPath = GetConfigPath(projectPath);

        if (!File.Exists(configPath))
        {
            return new ProjectConfig();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            return JsonSerializer.Deserialize<ProjectConfig>(json, SerializerOptions) ?? new ProjectConfig();
        }
        catch
        {
            return new ProjectConfig();
        }
    }

    /// <summary>
    /// Saves config to the project's usr/config.json.
    /// Creates the usr/ directory if it doesn't exist.
    /// </summary>
    public static void Save(string projectPath, ProjectConfig config)
    {
        var configPath = GetConfigPath(projectPath);
        var configDir = Path.GetDirectoryName(configPath);

        if (configDir != null && !Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(configPath, json);
    }
}
