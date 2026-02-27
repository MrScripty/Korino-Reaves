using System;
using System.IO;
using FluentAssertions;
using UAssetViewer.Agent.Capabilities;
using UAssetViewer.Assets;
using UAssetViewer.Models;
using Xunit;

namespace UAssetViewer.Tests.Agent.Capabilities;

public sealed class ProjectExplorerCapabilityTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectExplorerCapabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "korino-project-explorer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void GetRootNodes_WithOpenProject_ReturnsFileAndFolderNodes()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(_tempDir, "Content"));
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "test");

        var capability = CreateCapability(_tempDir);

        // Act
        var roots = capability.GetRootNodes();

        // Assert
        roots.Should().Contain(node => node.Type == TreeNodeTypes.Folder && node.Name == "Content");
        roots.Should().Contain(node => node.Type == TreeNodeTypes.File && node.Name == "readme.txt");
    }

    [Fact]
    public void GetChildren_WithValidFolderNode_ReturnsDirectChildren()
    {
        // Arrange
        var contentDir = Path.Combine(_tempDir, "Content");
        Directory.CreateDirectory(contentDir);
        File.WriteAllText(Path.Combine(contentDir, "A.uasset"), "x");

        var capability = CreateCapability(_tempDir);
        var contentNode = capability.GetRootNodes().Should().ContainSingle(
            node => node.Type == TreeNodeTypes.Folder && node.Name == "Content").Subject;

        // Act
        var children = capability.GetChildren(contentNode.Id);

        // Assert
        children.Should().NotBeEmpty();
        children.Should().Contain(c => c.Name == "A");
    }

    [Fact]
    public void Search_RespectsLimit()
    {
        // Arrange
        for (int i = 0; i < 8; i++)
        {
            File.WriteAllText(Path.Combine(_tempDir, $"file_{i}.txt"), "x");
        }

        var capability = CreateCapability(_tempDir);

        // Act
        var results = capability.Search("file_", limit: 3);

        // Assert
        results.Length.Should().Be(3);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temp test directories.
        }
    }

    private static ProjectExplorerCapability CreateCapability(string projectPath)
    {
        return new ProjectExplorerCapability(
            new StaticProjectPathProvider(projectPath),
            new FileTreeBuilder(new TestLogger()),
            new TestLogger());
    }

    private sealed class StaticProjectPathProvider : IProjectPathProvider
    {
        public StaticProjectPathProvider(string? path)
        {
            CurrentProjectPath = path;
        }

        public string? CurrentProjectPath { get; }
    }
}
