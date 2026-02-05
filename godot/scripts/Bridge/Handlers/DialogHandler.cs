// Dialog Handler - Native file dialogs via Godot
//
// Handles dialog-related IPC messages to show native file picker dialogs.
// Uses Godot's FileDialog to provide platform-native file selection.

using System;
using System.Threading.Tasks;
using Godot;
using UAssetViewer.Bridge;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;

namespace UAssetViewer.Bridge.Handlers;

/// <summary>
/// Handler for dialog-related IPC messages.
/// Shows native file dialogs and sends results back to the frontend.
/// </summary>
public sealed class DialogHandler : IMessageHandler
{
    private readonly IAppLogger _logger;
    private readonly IpcDispatcher _dispatcher;
    private readonly Node _sceneRoot;
    private FileDialog? _fileDialog;
    private string? _pendingRequestId;
    private string? _pendingAction;

    public string MessageType => MessageTypes.Dialog;

    public DialogHandler(IAppLogger logger, IpcDispatcher dispatcher, Node sceneRoot)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _sceneRoot = sceneRoot ?? throw new ArgumentNullException(nameof(sceneRoot));
    }

    public bool CanHandle(string action)
    {
        return action is "showOpen" or "showSave" or "showExport" or "showImportPak" or "showOpenProject";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("[DialogHandler] HandleAsync called: action={Action}", message.Action);
        _logger.Info("[DialogHandler] Scene root valid: {Valid}, type: {Type}",
            GodotObject.IsInstanceValid(_sceneRoot),
            _sceneRoot?.GetType().Name ?? "null");

        // Dialog operations need to run on the main thread
        _logger.Info("[DialogHandler] Deferring to main thread via CallDeferred...");
        Callable.From(() => HandleDialogOnMainThread(message)).CallDeferred();

        // Return null - we'll send the response asynchronously when the dialog closes
        return Task.FromResult<IpcMessage?>(null);
    }

    private void HandleDialogOnMainThread(IpcMessage message)
    {
        _logger.Info("[DialogHandler] HandleDialogOnMainThread called on main thread, action={Action}", message.Action);

        try
        {
            switch (message.Action)
            {
                case "showOpen":
                    _logger.Info("[DialogHandler] Calling ShowOpenDialog...");
                    ShowOpenDialog(message);
                    break;
                case "showSave":
                    _logger.Info("[DialogHandler] Calling ShowSaveDialog...");
                    ShowSaveDialog(message);
                    break;
                case "showExport":
                    _logger.Info("[DialogHandler] Calling ShowExportDialog...");
                    ShowExportDialog(message);
                    break;
                case "showImportPak":
                    _logger.Info("[DialogHandler] Calling ShowImportPakDialog...");
                    ShowImportPakDialog(message);
                    break;
                case "showOpenProject":
                    _logger.Info("[DialogHandler] Calling ShowOpenProjectDialog...");
                    ShowOpenProjectDialog(message);
                    break;
                default:
                    _logger.Warning("[DialogHandler] Unknown action: {Action}", message.Action);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to show dialog");
            SendError(message.Id, ex.Message);
        }
    }

    private void ShowOpenDialog(IpcMessage message)
    {
        _logger.Info("Showing open file dialog");

        _pendingRequestId = message.Id;
        _pendingAction = "open";

        EnsureFileDialog();
        _fileDialog!.FileMode = FileDialog.FileModeEnum.OpenFile;
        _fileDialog.Title = "Open Asset or PAK";
        _fileDialog.Filters = new[] { "*.pak ; PAK Archives", "*.uasset ; UAsset Files", "*.umap ; UMap Files", "* ; All Files" };
        _fileDialog.PopupCentered();
    }

    private void ShowSaveDialog(IpcMessage message)
    {
        _logger.Info("Showing save file dialog");

        _pendingRequestId = message.Id;
        _pendingAction = "save";

        EnsureFileDialog();
        _fileDialog!.FileMode = FileDialog.FileModeEnum.SaveFile;
        _fileDialog.Title = "Save Asset As";
        _fileDialog.Filters = new[] { "*.uasset ; UAsset Files", "*.umap ; UMap Files" };
        _fileDialog.PopupCentered();
    }

    private void ShowExportDialog(IpcMessage message)
    {
        _logger.Info("Showing export file dialog");

        _pendingRequestId = message.Id;
        _pendingAction = "export";

        EnsureFileDialog();
        _fileDialog!.FileMode = FileDialog.FileModeEnum.SaveFile;
        _fileDialog.Title = "Export as JSON";
        _fileDialog.Filters = new[] { "*.json ; JSON Files" };
        _fileDialog.PopupCentered();
    }

    private void ShowImportPakDialog(IpcMessage message)
    {
        _logger.Info("[ShowImportPakDialog] Starting...");

        _pendingRequestId = message.Id;
        _pendingAction = "importPak";

        EnsureFileDialog();
        _fileDialog!.FileMode = FileDialog.FileModeEnum.OpenFile;
        _fileDialog.Title = "Import PAK Archive";
        _fileDialog.Filters = new[] { "*.pak" };

        _logger.Info("[ShowImportPakDialog] FileDialog configured: Mode={Mode}, Title={Title}, Filters={Filters}",
            _fileDialog.FileMode, _fileDialog.Title, string.Join(", ", _fileDialog.Filters));

        _logger.Info("[ShowImportPakDialog] FileDialog state before popup: Visible={Visible}, IsInsideTree={Inside}",
            _fileDialog.Visible, _fileDialog.IsInsideTree());

        _logger.Info("[ShowImportPakDialog] Calling PopupCentered...");
        _fileDialog.PopupCentered();

        _logger.Info("[ShowImportPakDialog] After PopupCentered: Visible={Visible}",
            _fileDialog.Visible);
    }

    private void ShowOpenProjectDialog(IpcMessage message)
    {
        _logger.Info("[ShowOpenProjectDialog] Starting...");

        _pendingRequestId = message.Id;
        _pendingAction = "openProject";

        EnsureFileDialog();
        _fileDialog!.FileMode = FileDialog.FileModeEnum.OpenDir;
        _fileDialog.Title = "Open Project";
        _fileDialog.Filters = Array.Empty<string>(); // No filters for directory selection

        _logger.Info("[ShowOpenProjectDialog] FileDialog configured: Mode={Mode}, Title={Title}",
            _fileDialog.FileMode, _fileDialog.Title);

        // Set initial directory to projects folder
        var projectRoot = ProjectSettings.GlobalizePath("res://").TrimEnd('/');
        projectRoot = System.IO.Path.GetDirectoryName(projectRoot) ?? projectRoot;
        var projectsDir = System.IO.Path.Combine(projectRoot, "projects");
        _logger.Info("[ShowOpenProjectDialog] Projects directory: {Dir}, exists: {Exists}", projectsDir, System.IO.Directory.Exists(projectsDir));

        if (System.IO.Directory.Exists(projectsDir))
        {
            _fileDialog.CurrentDir = projectsDir;
            _logger.Info("[ShowOpenProjectDialog] Set CurrentDir to: {Dir}", _fileDialog.CurrentDir);
        }

        _logger.Info("[ShowOpenProjectDialog] FileDialog state before popup: Visible={Visible}, IsInsideTree={Inside}",
            _fileDialog.Visible, _fileDialog.IsInsideTree());

        _logger.Info("[ShowOpenProjectDialog] Calling PopupCentered...");
        _fileDialog.PopupCentered();

        _logger.Info("[ShowOpenProjectDialog] After PopupCentered: Visible={Visible}",
            _fileDialog.Visible);
    }

    private void EnsureFileDialog()
    {
        if (_fileDialog != null && GodotObject.IsInstanceValid(_fileDialog))
        {
            _logger.Info("[EnsureFileDialog] Reusing existing FileDialog");
            return;
        }

        _logger.Info("[EnsureFileDialog] Creating new FileDialog...");

        _fileDialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            // Disable native dialogs - they can be unreliable on Linux
            UseNativeDialog = false,
            // Set a reasonable size for the Godot built-in dialog
            Size = new Godot.Vector2I(800, 600),
        };

        _logger.Info("[EnsureFileDialog] FileDialog created, UseNativeDialog={UseNative}, Size={Size}",
            _fileDialog.UseNativeDialog, _fileDialog.Size);

        _fileDialog.FileSelected += OnFileSelected;
        _fileDialog.DirSelected += OnDirSelected;
        _fileDialog.Canceled += OnDialogCanceled;

        _logger.Info("[EnsureFileDialog] Event handlers attached");
        _logger.Info("[EnsureFileDialog] Adding to scene root: {SceneRoot}", _sceneRoot.Name);

        _sceneRoot.AddChild(_fileDialog);

        _logger.Info("[EnsureFileDialog] FileDialog added to scene tree, IsInsideTree={Inside}",
            _fileDialog.IsInsideTree());
    }

    private void OnDirSelected(string path)
    {
        _logger.Info("Directory selected: {Path}", path);

        var response = new IpcMessage(
            MessageTypes.Dialog,
            "fileSelected",
            new { filePath = path, dialogAction = _pendingAction },
            _pendingRequestId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        _dispatcher.Send(response);
        ClearPending();
    }

    private void OnFileSelected(string path)
    {
        _logger.Info("File selected: {Path}", path);

        var response = new IpcMessage(
            MessageTypes.Dialog,
            "fileSelected",
            new { filePath = path, dialogAction = _pendingAction },
            _pendingRequestId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        _dispatcher.Send(response);
        ClearPending();
    }

    private void OnDialogCanceled()
    {
        _logger.Info("Dialog canceled");

        var response = new IpcMessage(
            MessageTypes.Dialog,
            "canceled",
            new { dialogAction = _pendingAction },
            _pendingRequestId,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        );

        _dispatcher.Send(response);
        ClearPending();
    }

    private void ClearPending()
    {
        _pendingRequestId = null;
        _pendingAction = null;
    }

    private void SendError(string? requestId, string errorMessage)
    {
        _dispatcher.SendError(requestId, ErrorCodes.InternalError, errorMessage);
    }
}
