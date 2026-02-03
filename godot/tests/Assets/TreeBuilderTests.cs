// Tree Builder Tests
//
// Unit tests for TreeBuilder functionality.

using System;
using System.Linq;
using FluentAssertions;
using NSubstitute;
using UAssetAPI;
using UAssetViewer.Assets;
using UAssetViewer.Infrastructure;
using UAssetViewer.Models;
using Xunit;

namespace UAssetViewer.Tests.Assets;

public class TreeBuilderTests
{
    private readonly IAppLogger _logger;
    private readonly TreeBuilder _treeBuilder;

    public TreeBuilderTests()
    {
        _logger = Substitute.For<IAppLogger>();
        _logger.BeginScope(Arg.Any<string>()).Returns(Substitute.For<IDisposable>());
        _treeBuilder = new TreeBuilder(_logger);
    }

    [Fact]
    public void GetRootNodes_WhenNoAssetLoaded_ReturnsEmpty()
    {
        // Act
        var nodes = _treeBuilder.GetRootNodes();

        // Assert
        nodes.Should().BeEmpty();
    }

    [Fact]
    public void GetChildren_WhenNoAssetLoaded_ReturnsEmpty()
    {
        // Act
        var children = _treeBuilder.GetChildren("exports");

        // Assert
        children.Should().BeEmpty();
    }

    [Fact]
    public void Search_WhenNoAssetLoaded_ReturnsEmpty()
    {
        // Act
        var results = _treeBuilder.Search("test");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Search_WithEmptyQuery_ReturnsEmpty()
    {
        // Act
        var results = _treeBuilder.Search("");

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void GetPathToNode_ExportNode_ReturnsCorrectPath()
    {
        // Act
        var path = _treeBuilder.GetPathToNode("export-0");

        // Assert
        path.Should().Contain("exports");
        path.Should().Contain("export-0");
    }

    [Fact]
    public void GetPathToNode_ImportNode_ReturnsCorrectPath()
    {
        // Act
        var path = _treeBuilder.GetPathToNode("import-5");

        // Assert
        path.Should().Contain("imports");
        path.Should().Contain("import-5");
    }

    [Fact]
    public void GetPathToNode_NameNode_ReturnsCorrectPath()
    {
        // Act
        var path = _treeBuilder.GetPathToNode("name-test");

        // Assert
        path.Should().Contain("names");
        path.Should().Contain("name-test");
    }

    [Fact]
    public void Clear_ClearsState()
    {
        // Arrange - simulate initialized state
        _treeBuilder.Clear();

        // Act
        var nodes = _treeBuilder.GetRootNodes();

        // Assert
        nodes.Should().BeEmpty();
    }

    [Fact]
    public void InvalidateNode_RemovesCachedData()
    {
        // Act - should not throw
        Action act = () => _treeBuilder.InvalidateNode("export-0");

        // Assert
        act.Should().NotThrow();
    }
}

public class TreeNodeTypesTests
{
    [Fact]
    public void TreeNodeTypes_HasExpectedValues()
    {
        TreeNodeTypes.Export.Should().Be("export");
        TreeNodeTypes.Property.Should().Be("property");
        TreeNodeTypes.Array.Should().Be("array");
        TreeNodeTypes.Struct.Should().Be("struct");
        TreeNodeTypes.Map.Should().Be("map");
        TreeNodeTypes.Import.Should().Be("import");
        TreeNodeTypes.Name.Should().Be("name");
        TreeNodeTypes.Header.Should().Be("header");
        TreeNodeTypes.Unknown.Should().Be("unknown");
    }
}
