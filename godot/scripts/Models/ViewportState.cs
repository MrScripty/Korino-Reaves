namespace UAssetViewer.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Viewport display modes.
/// </summary>
public static class ViewportModes
{
    public const string Mode2D = "2d";
    public const string Mode3D = "3d";
    public const string None = "none";
}

/// <summary>
/// Content types that can be previewed in the viewport.
/// </summary>
public static class ViewportContentTypes
{
    public const string Texture = "texture";
    public const string Mesh = "mesh";
    public const string Skeleton = "skeleton";
    public const string Animation = "animation";
}

/// <summary>
/// Commands for controlling the 3D/2D viewport.
/// </summary>
public static class ViewportCommands
{
    public const string ResetCamera = "reset_camera";
    public const string FocusSelection = "focus_selection";
    public const string ToggleGrid = "toggle_grid";
    public const string ToggleWireframe = "toggle_wireframe";
    public const string SetBackground = "set_background";
    public const string ToggleDoubleSided = "toggle_double_sided";
}

/// <summary>
/// Viewport state information.
/// </summary>
/// <param name="Mode">Current preview mode</param>
/// <param name="ContentType">Type of content being previewed</param>
/// <param name="GridVisible">Whether grid is visible</param>
/// <param name="Wireframe">Whether wireframe mode is active</param>
public record ViewportState(
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("contentType")] string? ContentType,
    [property: JsonPropertyName("gridVisible")] bool GridVisible,
    [property: JsonPropertyName("wireframe")] bool Wireframe
);
