// Project Handler - Project Operations
//
// Handles project-related IPC messages for opening and managing
// extracted PAK contents as projects. Also manages EGame version
// state and persistent per-project configuration.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CUE4Parse.UE4.Versions;
using Godot;
using UAssetViewer.Assets;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Information about a project (extracted PAK contents).
/// </summary>
public sealed record ProjectInfo(
    string Name,
    string Path,
    int FileCount,
    string? LastModified = null
);

/// <summary>
/// Request to open a project.
/// </summary>
public sealed record OpenProjectRequest(
    string ProjectPath
);

/// <summary>
/// Handler for project-related IPC messages.
/// Manages opening and listing extracted PAK projects.
/// Also manages EGame version state and persistent config.
/// </summary>
public sealed class ProjectHandler : IMessageHandler
{
    private static readonly Regex PascalCaseSplitter = new(@"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|(?<=[a-zA-Z])(?=\d)", RegexOptions.Compiled);

    private readonly IAppLogger _logger;
    private readonly FileTreeBuilder _fileTreeBuilder;
    private readonly IpcDispatcher _dispatcher;
    private readonly string _projectsRoot;
    private ProjectInfo? _currentProject;

    // EGame version state
    private EGame _selectedGameVersion = EGame.GAME_UE4_27;
    private EGame _autoDetectedVersion = EGame.GAME_UE4_27;
    private bool _isAutoDetect = true;

    // Cached game version entries (built once)
    private GameVersionEntry[]? _cachedGameVersions;

    /// <summary>
    /// Gets the currently open project, or null if no project is open.
    /// </summary>
    public ProjectInfo? CurrentProject => _currentProject;

    /// <summary>
    /// Gets the effective EGame version (auto-detected or manually selected).
    /// </summary>
    public EGame EffectiveGameVersion => _isAutoDetect ? _autoDetectedVersion : _selectedGameVersion;

    /// <summary>
    /// Event raised when the game version changes.
    /// </summary>
    public event Action<EGame>? GameVersionChanged;

    /// <summary>
    /// Event raised when a project is opened.
    /// </summary>
    public event Action<ProjectInfo>? ProjectOpened;

    /// <summary>
    /// Event raised when a project is closed.
    /// </summary>
    public event Action? ProjectClosed;

    public string MessageType => MessageTypes.Project;

    public ProjectHandler(IAppLogger logger, IpcDispatcher dispatcher)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _fileTreeBuilder = new FileTreeBuilder(logger);
        _projectsRoot = GetProjectsDirectoryPath();
    }

    public bool CanHandle(string action)
    {
        return action is "open" or "list" or "close" or "getTree"
            or "getGameVersions" or "setGameVersion" or "getGameVersion";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("ProjectHandler received: action={Action}", message.Action);

        return message.Action switch
        {
            "open" => HandleOpen(message),
            "list" => HandleList(message),
            "close" => HandleClose(message),
            "getTree" => HandleGetTree(message),
            "getGameVersions" => HandleGetGameVersions(message),
            "setGameVersion" => HandleSetGameVersion(message),
            "getGameVersion" => HandleGetGameVersion(message),
            _ => Task.FromResult<IpcMessage?>(null),
        };
    }

    private async Task<IpcMessage?> HandleOpen(IpcMessage message)
    {
        try
        {
            if (!InputValidator.TryDeserializePayload<OpenProjectRequest>(message.Payload, out var parsedRequest, out var payloadError))
            {
                return CreateErrorResponse(message, payloadError, ErrorCodes.InvalidRequest);
            }

            var request = parsedRequest!;

            if (!PathValidator.TryResolveWithinRoot(
                    request.ProjectPath,
                    _projectsRoot,
                    out var projectPath,
                    out var pathError,
                    requireExists: true,
                    allowFiles: false,
                    allowDirectories: true))
            {
                return CreateErrorResponse(message, pathError, ErrorCodes.InvalidRequest);
            }

            _logger.Info("Opening project: {Path}", projectPath);

            // Get project name from directory
            var projectName = System.IO.Path.GetFileName(projectPath);

            // Count files
            var fileCount = await Task.Run(() => CountFiles(projectPath));

            // Build project info
            _currentProject = new ProjectInfo(
                projectName,
                projectPath,
                fileCount,
                Directory.GetLastWriteTime(projectPath).ToString("o")
            );

            // Load persistent config and set game version state
            var config = ProjectConfig.Load(projectPath);
            _autoDetectedVersion = DetectUeVersion(projectPath);
            _logger.Info("Auto-detected UE version: {Version}", _autoDetectedVersion);

            if (!string.IsNullOrEmpty(config.GameVersion) &&
                config.GameVersion != "AUTO" &&
                Enum.TryParse<EGame>(config.GameVersion, out var savedVersion))
            {
                _selectedGameVersion = savedVersion;
                _isAutoDetect = false;
                _logger.Info("Loaded saved game version: {Version}", savedVersion);
            }
            else
            {
                _isAutoDetect = true;
                _logger.Info("Using auto-detected version: {Version}", _autoDetectedVersion);
            }

            // Build and send the file tree
            var treeNodes = await Task.Run(() => _fileTreeBuilder.BuildFileTree(projectPath));

            // Send tree update
            _dispatcher.Send(MessageTypes.Tree, "update", new { nodes = treeNodes });

            // Broadcast game version state
            BroadcastGameVersionState();

            // Notify subscribers
            ProjectOpened?.Invoke(_currentProject);

            // Return project opened response
            return new IpcMessage(
                MessageTypes.Project,
                "opened",
                _currentProject,
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            );
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to open project");
            return CreateErrorResponse(message, ex.Message);
        }
    }

    private Task<IpcMessage?> HandleList(IpcMessage message)
    {
        try
        {
            var projects = new List<ProjectInfo>();

            if (Directory.Exists(_projectsRoot))
            {
                foreach (var dir in Directory.GetDirectories(_projectsRoot))
                {
                    var name = System.IO.Path.GetFileName(dir);
                    var ueDataPath = System.IO.Path.Combine(dir, "UE_data");

                    if (Directory.Exists(ueDataPath))
                    {
                        var fileCount = CountFiles(ueDataPath);
                        var lastModified = Directory.GetLastWriteTime(dir).ToString("o");
                        projects.Add(new ProjectInfo(name, ueDataPath, fileCount, lastModified));
                    }
                }
            }

            return Task.FromResult<IpcMessage?>(new IpcMessage(
                MessageTypes.Project,
                "list",
                new { projects },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to list projects");
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, ex.Message));
        }
    }

    private Task<IpcMessage?> HandleClose(IpcMessage message)
    {
        _currentProject = null;

        // Reset version state
        _isAutoDetect = true;
        _selectedGameVersion = EGame.GAME_UE4_27;
        _autoDetectedVersion = EGame.GAME_UE4_27;

        // Clear the tree
        _dispatcher.Send(MessageTypes.Tree, "update", new { nodes = Array.Empty<TreeNode>() });

        // Notify subscribers
        ProjectClosed?.Invoke();

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Project,
            "closed",
            null,
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleGetTree(IpcMessage message)
    {
        if (_currentProject == null)
        {
            return Task.FromResult<IpcMessage?>(new IpcMessage(
                MessageTypes.Tree,
                "update",
                new { nodes = Array.Empty<TreeNode>() },
                message.Id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            ));
        }

        var treeNodes = _fileTreeBuilder.BuildFileTree(_currentProject.Path);

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Tree,
            "update",
            new { nodes = treeNodes },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    // -----------------------------------------------------------------
    // Game Version IPC Actions
    // -----------------------------------------------------------------

    private Task<IpcMessage?> HandleGetGameVersions(IpcMessage message)
    {
        var versions = GetGameVersionEntries();

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Project,
            "gameVersions",
            new { versions },
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleSetGameVersion(IpcMessage message)
    {
        var version = ParsePayloadString(message.Payload, "version");
        if (version == null)
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, "Missing version"));
        }

        var previousVersion = EffectiveGameVersion;

        if (version == "AUTO")
        {
            _isAutoDetect = true;
            _logger.Info("Game version set to AUTO (effective: {Version})", _autoDetectedVersion);
        }
        else if (Enum.TryParse<EGame>(version, out var parsed))
        {
            _selectedGameVersion = parsed;
            _isAutoDetect = false;
            _logger.Info("Game version set to: {Version}", parsed);
        }
        else
        {
            return Task.FromResult<IpcMessage?>(CreateErrorResponse(message, $"Unknown game version: {version}"));
        }

        // Persist to config
        if (_currentProject != null)
        {
            var config = ProjectConfig.Load(_currentProject.Path);
            config.GameVersion = _isAutoDetect ? null : version;
            ProjectConfig.Save(_currentProject.Path, config);
        }

        // Fire event if the effective version actually changed
        if (EffectiveGameVersion != previousVersion)
        {
            GameVersionChanged?.Invoke(EffectiveGameVersion);
        }

        BroadcastGameVersionState();

        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Project,
            "gameVersion",
            BuildGameVersionState(),
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    private Task<IpcMessage?> HandleGetGameVersion(IpcMessage message)
    {
        return Task.FromResult<IpcMessage?>(new IpcMessage(
            MessageTypes.Project,
            "gameVersion",
            BuildGameVersionState(),
            message.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        ));
    }

    // -----------------------------------------------------------------
    // Game Version Helpers
    // -----------------------------------------------------------------

    /// <summary>
    /// Builds the current game version state for IPC.
    /// </summary>
    private GameVersionState BuildGameVersionState()
    {
        return new GameVersionState(
            _isAutoDetect ? "AUTO" : _selectedGameVersion.ToString(),
            _autoDetectedVersion.ToString(),
            _isAutoDetect
        );
    }

    /// <summary>
    /// Broadcasts the current game version state to the frontend.
    /// </summary>
    private void BroadcastGameVersionState()
    {
        _dispatcher.Send(MessageTypes.Project, "gameVersion", BuildGameVersionState());
    }

    /// <summary>
    /// Enumerates all EGame values and returns them as labeled, grouped entries.
    /// Results are cached after first call.
    /// </summary>
    public GameVersionEntry[] GetGameVersionEntries()
    {
        if (_cachedGameVersions != null) return _cachedGameVersions;

        var entries = new List<GameVersionEntry>();
        var allValues = Enum.GetValues<EGame>();

        // Build list of base UE versions for grouping
        var baseVersions = allValues
            .Where(v => IsBaseUeVersion(v))
            .OrderBy(v => (uint)v)
            .ToArray();

        foreach (var value in allValues)
        {
            var name = value.ToString();

            // Skip aliases and placeholders
            if (name == "GAME_UE4_LATEST" || name == "GAME_UE5_LATEST") continue;
            if (name.Contains("Placeholder")) continue;

            var label = FormatGameLabel(name);
            var group = FindGroup(value, baseVersions);

            entries.Add(new GameVersionEntry(name, label, group));
        }

        _cachedGameVersions = entries.ToArray();
        return _cachedGameVersions;
    }

    /// <summary>
    /// Returns true for base UE engine version entries (GAME_UE4_N, GAME_UE5_N).
    /// </summary>
    private static bool IsBaseUeVersion(EGame value)
    {
        var name = value.ToString();
        return Regex.IsMatch(name, @"^GAME_UE[45]_\d+$");
    }

    /// <summary>
    /// Finds the UE version group for a given EGame value.
    /// </summary>
    private static string FindGroup(EGame value, EGame[] baseVersions)
    {
        var name = value.ToString();

        // Special case: if it IS a base version, it's its own group
        if (IsBaseUeVersion(value))
        {
            return FormatBaseVersionGroup(name);
        }

        // Special case: UE5_EA
        if (name == "GAME_UE5_EA") return "UE5.0";

        // Find the highest base version that is <= this value
        EGame? group = null;
        foreach (var bv in baseVersions)
        {
            if ((uint)bv <= (uint)value)
                group = bv;
            else
                break;
        }

        return group.HasValue ? FormatBaseVersionGroup(group.Value.ToString()) : "Other";
    }

    /// <summary>
    /// Formats a base UE version name into a group label: GAME_UE4_27 → "UE4.27"
    /// </summary>
    private static string FormatBaseVersionGroup(string name)
    {
        // GAME_UE4_27 → UE4.27
        var match = Regex.Match(name, @"GAME_(UE[45])_(\d+)");
        if (match.Success)
        {
            return $"{match.Groups[1].Value}.{match.Groups[2].Value}";
        }
        return name;
    }

    /// <summary>
    /// Formats a game version enum name into a human-readable label.
    /// </summary>
    private static string FormatGameLabel(string name)
    {
        // Strip GAME_ prefix
        var stripped = name.StartsWith("GAME_") ? name.Substring(5) : name;

        // Base UE versions: UE4_27 → "UE4.27 (Generic)"
        var baseMatch = Regex.Match(stripped, @"^(UE[45])_(\d+)$");
        if (baseMatch.Success)
        {
            return $"{baseMatch.Groups[1].Value}.{baseMatch.Groups[2].Value} (Generic)";
        }

        // UE4_25_Plus → "UE4.25+"
        if (stripped == "UE4_25_Plus") return "UE4.25+";

        // UE5_EA → "UE5 Early Access"
        if (stripped == "UE5_EA") return "UE5 Early Access";

        // Game names: split PascalCase, handle numbers
        // Remove trailing version suffixes like _PRE_11_2, _Old, _CBT1
        var label = PascalCaseSplitter.Replace(stripped, " ");

        // Clean up underscores and numbers in the middle
        label = label.Replace("_", " ").Trim();

        // Collapse multiple spaces
        label = Regex.Replace(label, @"\s+", " ");

        return label;
    }

    // -----------------------------------------------------------------
    // UE Version Detection (public static for reuse)
    // -----------------------------------------------------------------

    /// <summary>
    /// Detects UE version by reading the package header from the first .uasset found.
    /// Reads the FileVersionUE4 field to determine the engine version more precisely.
    /// Falls back to heuristics based on the legacy file version.
    /// </summary>
    public static EGame DetectUeVersion(string projectPath)
    {
        try
        {
            var uassetFiles = Directory.EnumerateFiles(projectPath, "*.uasset", SearchOption.AllDirectories);
            foreach (var filePath in uassetFiles)
            {
                using var fs = File.OpenRead(filePath);
                if (fs.Length < 20) continue;

                var header = new byte[20];
                if (fs.Read(header, 0, 20) < 20) continue;

                // Check magic number (little-endian 0x9E2A83C1)
                uint magic = BitConverter.ToUInt32(header, 0);
                if (magic != 0x9E2A83C1) continue;

                // Read legacy file version
                int legacyVersion = BitConverter.ToInt32(header, 4);

                // UE5 packages use legacy version <= -8
                if (legacyVersion <= -8)
                {
                    return EGame.GAME_UE5_3;
                }

                // Read FileVersionUE4 (offset 0x0C)
                int fileVersionUE4 = BitConverter.ToInt32(header, 12);

                // For versioned packages (FileVersionUE4 > 0), map to EGame
                if (fileVersionUE4 > 0)
                {
                    return MapFileVersionToEGame(fileVersionUE4);
                }

                // Unversioned package (FileVersionUE4 == 0) — can't determine
                // exact version from header alone. Default to UE4.27 and let
                // the version fallback in PreviewManager handle it.
                return EGame.GAME_UE4_27;
            }
        }
        catch
        {
            // Fall through to default
        }

        return EGame.GAME_UE4_27;
    }

    /// <summary>
    /// Maps a FileVersionUE4 value to the closest EGame enum.
    /// UE4 versions: 4.0=342, 4.10=381, 4.14=508, 4.17=504(?), 4.20=514, 4.25=518, 4.27=522
    /// </summary>
    private static EGame MapFileVersionToEGame(int fileVersionUE4)
    {
        // These thresholds are approximate — based on VER_UE4_* constants
        if (fileVersionUE4 >= 522) return EGame.GAME_UE4_27;
        if (fileVersionUE4 >= 518) return EGame.GAME_UE4_25;
        if (fileVersionUE4 >= 516) return EGame.GAME_UE4_22;
        if (fileVersionUE4 >= 514) return EGame.GAME_UE4_20;
        if (fileVersionUE4 >= 510) return EGame.GAME_UE4_17;
        if (fileVersionUE4 >= 508) return EGame.GAME_UE4_14;
        if (fileVersionUE4 >= 381) return EGame.GAME_UE4_10;
        return EGame.GAME_UE4_0;
    }

    /// <summary>
    /// Sets the game version state directly (used by PakHandler after import).
    /// Saves the config and fires the change event.
    /// </summary>
    public void SetGameVersionFromImport(string projectPath, string? gameVersion)
    {
        if (!PathValidator.TryResolveWithinRoot(
                projectPath,
                _projectsRoot,
                out var validatedProjectPath,
                out _,
                requireExists: true,
                allowFiles: false,
                allowDirectories: true))
        {
            throw new InvalidOperationException($"Project path is outside the managed projects root: {projectPath}");
        }

        if (!string.IsNullOrEmpty(gameVersion) &&
            gameVersion != "AUTO" &&
            Enum.TryParse<EGame>(gameVersion, out var parsed))
        {
            _selectedGameVersion = parsed;
            _isAutoDetect = false;
        }
        else
        {
            _isAutoDetect = true;
        }

        // Persist
        var config = ProjectConfig.Load(validatedProjectPath);
        config.GameVersion = _isAutoDetect ? null : gameVersion;
        ProjectConfig.Save(validatedProjectPath, config);
    }

    private static string GetProjectsDirectoryPath()
    {
        var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('/');
        projectRoot = System.IO.Path.GetDirectoryName(projectRoot) ?? projectRoot;
        return System.IO.Path.Combine(projectRoot, "projects");
    }

    private static int CountFiles(string path)
    {
        try
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Count();
        }
        catch
        {
            return 0;
        }
    }

    private static string? ParsePayloadString(object? payload, string propertyName)
    {
        if (InputValidator.TryGetRequiredString(payload, propertyName, out var value, out _))
        {
            return value;
        }

        return null;
    }

    private static IpcMessage CreateErrorResponse(
        IpcMessage request,
        string errorMessage,
        string code = ErrorCodes.InternalError)
    {
        return new IpcMessage(
            MessageTypes.Error,
            "error",
            new ErrorResponse(code, errorMessage, request.Id),
            request.Id,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );
    }
}
