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
        return action is "showOpen" or "showSave" or "showExport";
    }

    public Task<IpcMessage?> HandleAsync(IpcMessage message)
    {
        _logger.Info("DialogHandler received: action={Action}", message.Action);

        // Dialog operations need to run on the main thread
        Callable.From(() => HandleDialogOnMainThread(message)).CallDeferred();

        // Return null - we'll send the response asynchronously when the dialog closes
        return Task.FromResult<IpcMessage?>(null);
    }

    private void HandleDialogOnMainThread(IpcMessage message)
    {
        try
        {
            switch (message.Action)
            {
                case "showOpen":
                    ShowOpenDialog(message);
                    break;
                case "showSave":
                    ShowSaveDialog(message);
                    break;
                case "showExport":
                    ShowExportDialog(message);
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

    private void EnsureFileDialog()
    {
        if (_fileDialog != null && GodotObject.IsInstanceValid(_fileDialog))
        {
            return;
        }

        _fileDialog = new FileDialog
        {
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
        };

        _fileDialog.FileSelected += OnFileSelected;
        _fileDialog.Canceled += OnDialogCanceled;

        _sceneRoot.AddChild(_fileDialog);
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
