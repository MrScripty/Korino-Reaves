// Mappings Manager Tests
//
// Unit tests for MappingsManager functionality.

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using UAssetViewer.Assets;
using UAssetViewer.Infrastructure;
using Xunit;

namespace UAssetViewer.Tests.Assets;

public class MappingsManagerTests
{
    private readonly IAppLogger _logger;
    private readonly MappingsManager _mappingsManager;

    public MappingsManagerTests()
    {
        _logger = Substitute.For<IAppLogger>();
        _logger.BeginScope(Arg.Any<string>()).Returns(Substitute.For<IDisposable>());
        _mappingsManager = new MappingsManager(_logger);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => new MappingsManager(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task LoadAsync_WithNonexistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var path = "/nonexistent/path/mappings.usmap";

        // Act
        Func<Task> act = () => _mappingsManager.LoadAsync(path);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task LoadMappingsForAssetAsync_WithInvalidPath_ReturnsNull()
    {
        // Arrange
        var assetPath = "/some/fake/path/asset.uasset";

        // Act
        var result = await _mappingsManager.LoadMappingsForAssetAsync(assetPath);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CacheCount_Initially_IsZero()
    {
        // Assert
        _mappingsManager.CacheCount.Should().Be(0);
    }

    [Fact]
    public void ClearCache_WhenEmpty_DoesNotThrow()
    {
        // Act
        Action act = () => _mappingsManager.ClearCache();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Invalidate_NonexistentPath_DoesNotThrow()
    {
        // Act
        Action act = () => _mappingsManager.Invalidate("/nonexistent/path.usmap");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GetInfo_NonexistentPath_ReturnsNull()
    {
        // Act
        var info = _mappingsManager.GetInfo("/nonexistent/path.usmap");

        // Assert
        info.Should().BeNull();
    }
}

public class MappingsInfoTests
{
    [Fact]
    public void MappingsInfo_CanBeCreated()
    {
        // Arrange & Act
        var info = new MappingsInfo(
            Path: "/test/path.usmap",
            SchemaCount: 100,
            EnumCount: 50
        );

        // Assert
        info.Path.Should().Be("/test/path.usmap");
        info.SchemaCount.Should().Be(100);
        info.EnumCount.Should().Be(50);
    }
}
