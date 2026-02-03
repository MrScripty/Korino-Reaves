// Asset Loader Tests
//
// Unit tests for AssetLoader functionality.

using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using NSubstitute;
using UAssetViewer.Assets;
using UAssetViewer.Infrastructure;
using Xunit;

namespace UAssetViewer.Tests.Assets;

public class AssetLoaderTests
{
    private readonly IAppLogger _logger;
    private readonly AssetLoader _assetLoader;

    public AssetLoaderTests()
    {
        _logger = Substitute.For<IAppLogger>();
        _logger.BeginScope(Arg.Any<string>()).Returns(Substitute.For<IDisposable>());
        _assetLoader = new AssetLoader(_logger);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => new AssetLoader(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task LoadAsync_WithNonexistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var path = "/nonexistent/path/asset.uasset";

        // Act
        Func<Task> act = () => _assetLoader.LoadAsync(path);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public void CanLoad_WithNonexistentFile_ReturnsFalse()
    {
        // Arrange
        var path = "/nonexistent/path/asset.uasset";

        // Act
        var result = _assetLoader.CanLoad(path);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Save_WithNullAsset_ThrowsArgumentNullException()
    {
        // Arrange
        var path = "/test/path/asset.uasset";

        // Act
        Action act = () => _assetLoader.Save(null!, path);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("asset");
    }
}
