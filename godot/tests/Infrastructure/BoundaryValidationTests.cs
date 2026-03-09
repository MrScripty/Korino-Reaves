using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;
using Xunit;

namespace UAssetViewer.Tests.Infrastructure;

public sealed class BoundaryValidationTests : IDisposable
{
    private readonly string _tempRoot;

    public BoundaryValidationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"uassetviewer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void TryParseIncomingMessage_AcceptsKnownType()
    {
        var json = """
            {"type":"project","action":"open","payload":{"projectPath":"/tmp/project"},"id":"req-1","timestamp":123}
            """;

        var parsed = IpcMessageValidator.TryParseIncomingMessage(json, out var message, out var error);

        parsed.Should().BeTrue(error);
        message.Should().NotBeNull();
        message!.Type.Should().Be(MessageTypes.Project);
        message.Action.Should().Be("open");
    }

    [Fact]
    public void TryParseIncomingMessage_RejectsUnknownType()
    {
        var json = """
            {"type":"mystery","action":"open","payload":{}}
            """;

        var parsed = IpcMessageValidator.TryParseIncomingMessage(json, out _, out var error);

        parsed.Should().BeFalse();
        error.Should().Contain("Unknown IPC message type");
    }

    [Fact]
    public void TryParseIncomingMessage_RejectsNonStringId()
    {
        var json = """
            {"type":"project","action":"open","payload":{},"id":123}
            """;

        var parsed = IpcMessageValidator.TryParseIncomingMessage(json, out _, out var error);

        parsed.Should().BeFalse();
        error.Should().Contain("id must be a string");
    }

    [Fact]
    public void TryDeserializePayload_DeserializesJsonElement()
    {
        var payload = JsonSerializer.SerializeToElement(new { filePath = "/tmp/project/Asset.uasset" });

        var parsed = InputValidator.TryDeserializePayload<OpenAssetRequest>(payload, out var request, out var error);

        parsed.Should().BeTrue(error);
        request.Should().NotBeNull();
        request!.FilePath.Should().Be("/tmp/project/Asset.uasset");
    }

    [Fact]
    public void TryResolveWithinRoot_AcceptsExistingPathInsideRoot()
    {
        var projectsRoot = Path.Combine(_tempRoot, "projects");
        var projectPath = Path.Combine(projectsRoot, "Example", "UE_data");
        Directory.CreateDirectory(projectPath);

        var parsed = PathValidator.TryResolveWithinRoot(
            projectPath,
            projectsRoot,
            out var validatedPath,
            out var error,
            requireExists: true,
            allowFiles: false,
            allowDirectories: true);

        parsed.Should().BeTrue(error);
        validatedPath.Should().Be(Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    [Fact]
    public void TryResolveWithinRoot_RejectsPathOutsideRoot()
    {
        var projectsRoot = Path.Combine(_tempRoot, "projects");
        var outsideRoot = Path.Combine(_tempRoot, "outside");
        Directory.CreateDirectory(projectsRoot);
        Directory.CreateDirectory(outsideRoot);

        var parsed = PathValidator.TryResolveWithinRoot(
            outsideRoot,
            projectsRoot,
            out _,
            out var error,
            requireExists: true,
            allowFiles: false,
            allowDirectories: true);

        parsed.Should().BeFalse();
        error.Should().Contain("outside the allowed roots");
    }

    [Fact]
    public void TryResolveWithinRoot_AcceptsNewFileInsideRootWhenParentExists()
    {
        var projectsRoot = Path.Combine(_tempRoot, "projects");
        var exportDir = Path.Combine(projectsRoot, "exports");
        Directory.CreateDirectory(exportDir);
        var exportPath = Path.Combine(exportDir, "asset.json");

        var parsed = PathValidator.TryResolveWithinRoot(
            exportPath,
            projectsRoot,
            out var validatedPath,
            out var error,
            requireExists: false,
            allowFiles: true,
            allowDirectories: false);

        parsed.Should().BeTrue(error);
        validatedPath.Should().Be(Path.GetFullPath(exportPath));
    }
}
