// Unit tests for DiffEngine

using System.Linq;
using FluentAssertions;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetViewer.Diff;
using UAssetViewer.Models;
using Xunit;

namespace UAssetViewer.Tests.Diff;

public class DiffEngineTests
{
    private readonly TestLogger _logger;
    private readonly DiffEngine _engine;

    public DiffEngineTests()
    {
        _logger = new TestLogger();
        _engine = new DiffEngine(_logger);
    }

    [Fact]
    public void ComputeDiff_IdenticalAssets_ShouldReturnNoChanges()
    {
        // Arrange
        var (baseAsset, targetAsset) = CreateIdenticalAssets();

        // Act
        var result = _engine.ComputeDiff(baseAsset, targetAsset);

        // Assert
        result.Should().NotBeNull();
        result.Changes.Should().BeEmpty();
        result.Summary.Added.Should().Be(0);
        result.Summary.Removed.Should().Be(0);
        result.Summary.Modified.Should().Be(0);
    }

    [Fact]
    public void ComputeDiff_AddedProperty_ShouldDetect()
    {
        // Arrange
        var (baseAsset, targetAsset) = CreateAssetsWithAddedProperty();

        // Act
        var result = _engine.ComputeDiff(baseAsset, targetAsset);

        // Assert
        result.Changes.Should().ContainSingle(c => c.ChangeType == DiffChangeTypes.Added);
        result.Summary.Added.Should().Be(1);
    }

    [Fact]
    public void ComputeDiff_RemovedProperty_ShouldDetect()
    {
        // Arrange
        var (baseAsset, targetAsset) = CreateAssetsWithRemovedProperty();

        // Act
        var result = _engine.ComputeDiff(baseAsset, targetAsset);

        // Assert
        result.Changes.Should().ContainSingle(c => c.ChangeType == DiffChangeTypes.Removed);
        result.Summary.Removed.Should().Be(1);
    }

    [Fact]
    public void ComputeDiff_ModifiedIntProperty_ShouldDetect()
    {
        // Arrange
        var (baseAsset, targetAsset) = CreateAssetsWithModifiedIntProperty();

        // Act
        var result = _engine.ComputeDiff(baseAsset, targetAsset);

        // Assert
        result.Changes.Should().ContainSingle(c => c.ChangeType == DiffChangeTypes.Modified);
        var change = result.Changes.First(c => c.ChangeType == DiffChangeTypes.Modified);
        change.OldValue.Should().Be(100);
        change.NewValue.Should().Be(150);
        result.Summary.Modified.Should().Be(1);
    }

    [Fact]
    public void ComputeDiff_ModifiedStringProperty_ShouldDetect()
    {
        // Arrange
        var (baseAsset, targetAsset) = CreateAssetsWithModifiedStringProperty();

        // Act
        var result = _engine.ComputeDiff(baseAsset, targetAsset);

        // Assert
        result.Changes.Should().ContainSingle(c => c.ChangeType == DiffChangeTypes.Modified);
        var change = result.Changes.First(c => c.ChangeType == DiffChangeTypes.Modified);
        change.OldValue.Should().Be("Hello");
        change.NewValue.Should().Be("World");
    }

    [Fact]
    public void ComputeDiff_ModifiedBoolProperty_ShouldDetect()
    {
        // Arrange
        var (baseAsset, targetAsset) = CreateAssetsWithModifiedBoolProperty();

        // Act
        var result = _engine.ComputeDiff(baseAsset, targetAsset);

        // Assert
        result.Changes.Should().ContainSingle(c => c.ChangeType == DiffChangeTypes.Modified);
        var change = result.Changes.First(c => c.ChangeType == DiffChangeTypes.Modified);
        change.OldValue.Should().Be(true);
        change.NewValue.Should().Be(false);
    }

    [Fact]
    public void ComputeDiff_AddedExport_ShouldDetect()
    {
        // Arrange
        var (baseAsset, targetAsset) = CreateAssetsWithAddedExport();

        // Act
        var result = _engine.ComputeDiff(baseAsset, targetAsset);

        // Assert
        result.Changes.Should().Contain(c => c.ChangeType == DiffChangeTypes.Added);
        result.Summary.Added.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComputeDiff_RemovedExport_ShouldDetect()
    {
        // Arrange
        var (baseAsset, targetAsset) = CreateAssetsWithRemovedExport();

        // Act
        var result = _engine.ComputeDiff(baseAsset, targetAsset);

        // Assert
        result.Changes.Should().Contain(c => c.ChangeType == DiffChangeTypes.Removed);
        result.Summary.Removed.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetChangesForPath_ShouldFilterCorrectly()
    {
        // Arrange
        var (baseAsset, targetAsset) = CreateAssetsWithMultipleChanges();
        var result = _engine.ComputeDiff(baseAsset, targetAsset);

        // Act
        var exportChanges = _engine.GetChangesForPath(result, new[] { "Export[0]" });

        // Assert
        exportChanges.Should().NotBeEmpty();
        exportChanges.Should().OnlyContain(c => c.Path[0] == "Export[0]");
    }

    [Fact]
    public void ComputeDiff_ShouldPopulateSummary()
    {
        // Arrange
        var (baseAsset, targetAsset) = CreateAssetsWithMultipleChanges();

        // Act
        var result = _engine.ComputeDiff(baseAsset, targetAsset);

        // Assert
        result.Summary.Should().NotBeNull();
        result.Summary.Added.Should().BeGreaterOrEqualTo(0);
        result.Summary.Removed.Should().BeGreaterOrEqualTo(0);
        result.Summary.Modified.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public void ComputeDiff_ShouldSetVersionInfo()
    {
        // Arrange
        var baseAsset = CreateMinimalAsset("base.uasset");
        var targetAsset = CreateMinimalAsset("target.uasset");

        // Act
        var result = _engine.ComputeDiff(baseAsset, targetAsset);

        // Assert
        result.BaseVersion.Should().Contain("base");
        result.TargetVersion.Should().Contain("target");
    }

    // Helper methods to create test assets

    private static (UAsset, UAsset) CreateIdenticalAssets()
    {
        var asset1 = CreateMinimalAsset("test1.uasset");
        var asset2 = CreateMinimalAsset("test2.uasset");

        AddIntProperty(asset1, "Health", 100);
        AddIntProperty(asset2, "Health", 100);

        return (asset1, asset2);
    }

    private static (UAsset, UAsset) CreateAssetsWithAddedProperty()
    {
        var asset1 = CreateMinimalAsset("base.uasset");
        var asset2 = CreateMinimalAsset("target.uasset");

        AddIntProperty(asset1, "Health", 100);
        AddIntProperty(asset2, "Health", 100);
        AddIntProperty(asset2, "Shield", 50); // Added

        return (asset1, asset2);
    }

    private static (UAsset, UAsset) CreateAssetsWithRemovedProperty()
    {
        var asset1 = CreateMinimalAsset("base.uasset");
        var asset2 = CreateMinimalAsset("target.uasset");

        AddIntProperty(asset1, "Health", 100);
        AddIntProperty(asset1, "Shield", 50); // Will be removed
        AddIntProperty(asset2, "Health", 100);

        return (asset1, asset2);
    }

    private static (UAsset, UAsset) CreateAssetsWithModifiedIntProperty()
    {
        var asset1 = CreateMinimalAsset("base.uasset");
        var asset2 = CreateMinimalAsset("target.uasset");

        AddIntProperty(asset1, "Health", 100);
        AddIntProperty(asset2, "Health", 150); // Modified

        return (asset1, asset2);
    }

    private static (UAsset, UAsset) CreateAssetsWithModifiedStringProperty()
    {
        var asset1 = CreateMinimalAsset("base.uasset");
        var asset2 = CreateMinimalAsset("target.uasset");

        AddStringProperty(asset1, "Name", "Hello");
        AddStringProperty(asset2, "Name", "World"); // Modified

        return (asset1, asset2);
    }

    private static (UAsset, UAsset) CreateAssetsWithModifiedBoolProperty()
    {
        var asset1 = CreateMinimalAsset("base.uasset");
        var asset2 = CreateMinimalAsset("target.uasset");

        AddBoolProperty(asset1, "IsEnabled", true);
        AddBoolProperty(asset2, "IsEnabled", false); // Modified

        return (asset1, asset2);
    }

    private static (UAsset, UAsset) CreateAssetsWithAddedExport()
    {
        var asset1 = CreateMinimalAsset("base.uasset");
        var asset2 = CreateMinimalAsset("target.uasset");

        // asset2 has additional export
        AddExport(asset2, "NewExport");

        return (asset1, asset2);
    }

    private static (UAsset, UAsset) CreateAssetsWithRemovedExport()
    {
        var asset1 = CreateMinimalAsset("base.uasset");
        var asset2 = CreateMinimalAsset("target.uasset");

        // asset1 has additional export that asset2 doesn't have
        AddExport(asset1, "OldExport");

        return (asset1, asset2);
    }

    private static (UAsset, UAsset) CreateAssetsWithMultipleChanges()
    {
        var asset1 = CreateMinimalAsset("base.uasset");
        var asset2 = CreateMinimalAsset("target.uasset");

        AddIntProperty(asset1, "Health", 100);
        AddIntProperty(asset1, "Mana", 50);
        AddIntProperty(asset1, "OldStat", 25);

        AddIntProperty(asset2, "Health", 150); // Modified
        AddIntProperty(asset2, "Mana", 50);    // Unchanged
        AddIntProperty(asset2, "NewStat", 75); // Added (OldStat removed)

        return (asset1, asset2);
    }

    private static UAsset CreateMinimalAsset(string filePath)
    {
        var asset = new UAsset(EngineVersion.VER_UE5_1);
        asset.FilePath = filePath;

        // Add a basic export
        var export = new NormalExport(asset, new byte[0]);
        export.ObjectName = new FName(asset, "TestObject");
        asset.Exports.Add(export);

        return asset;
    }

    private static void AddIntProperty(UAsset asset, string name, int value)
    {
        if (asset.Exports.Count == 0 || asset.Exports[0] is not NormalExport export)
        {
            return;
        }

        var prop = new IntPropertyData(new FName(asset, name))
        {
            Value = value
        };
        export.Data.Add(prop);
    }

    private static void AddStringProperty(UAsset asset, string name, string value)
    {
        if (asset.Exports.Count == 0 || asset.Exports[0] is not NormalExport export)
        {
            return;
        }

        var prop = new StrPropertyData(new FName(asset, name))
        {
            Value = new FString(value)
        };
        export.Data.Add(prop);
    }

    private static void AddBoolProperty(UAsset asset, string name, bool value)
    {
        if (asset.Exports.Count == 0 || asset.Exports[0] is not NormalExport export)
        {
            return;
        }

        var prop = new BoolPropertyData(new FName(asset, name))
        {
            Value = value
        };
        export.Data.Add(prop);
    }

    private static void AddExport(UAsset asset, string name)
    {
        var export = new NormalExport(asset, new byte[0]);
        export.ObjectName = new FName(asset, name);
        asset.Exports.Add(export);
    }
}

public class ConflictDetectorTests
{
    private readonly TestLogger _logger;
    private readonly DiffEngine _diffEngine;
    private readonly ConflictDetector _detector;

    public ConflictDetectorTests()
    {
        _logger = new TestLogger();
        _diffEngine = new DiffEngine(_logger);
        _detector = new ConflictDetector(_logger, _diffEngine);
    }

    [Fact]
    public void DetectConflicts_NoOverlap_ShouldReturnAllNonConflicting()
    {
        // Arrange
        var gameChanges = CreateDiffResult(new[]
        {
            new DiffChange(new[] { "Export[0]", "Health" }, DiffChangeTypes.Modified, 100, 150)
        });

        var modChanges = CreateDiffResult(new[]
        {
            new DiffChange(new[] { "Export[0]", "Shield" }, DiffChangeTypes.Modified, 50, 75)
        });

        // Act
        var result = _detector.DetectConflicts(gameChanges, modChanges);

        // Assert
        result.NonConflicting.Should().HaveCount(1);
        result.Conflicting.Should().BeEmpty();
        result.Structural.Should().BeEmpty();
    }

    [Fact]
    public void DetectConflicts_SamePath_ShouldReturnConflict()
    {
        // Arrange
        var gameChanges = CreateDiffResult(new[]
        {
            new DiffChange(new[] { "Export[0]", "Health" }, DiffChangeTypes.Modified, 100, 150)
        });

        var modChanges = CreateDiffResult(new[]
        {
            new DiffChange(new[] { "Export[0]", "Health" }, DiffChangeTypes.Modified, 100, 200)
        });

        // Act
        var result = _detector.DetectConflicts(gameChanges, modChanges);

        // Assert
        result.Conflicting.Should().HaveCount(1);
        result.NonConflicting.Should().BeEmpty();
    }

    [Fact]
    public void DetectConflicts_SameChange_ShouldNotConflict()
    {
        // Arrange
        var gameChanges = CreateDiffResult(new[]
        {
            new DiffChange(new[] { "Export[0]", "Health" }, DiffChangeTypes.Modified, 100, 150)
        });

        var modChanges = CreateDiffResult(new[]
        {
            new DiffChange(new[] { "Export[0]", "Health" }, DiffChangeTypes.Modified, 100, 150)
        });

        // Act
        var result = _detector.DetectConflicts(gameChanges, modChanges);

        // Assert
        result.Conflicting.Should().BeEmpty();
        result.NonConflicting.Should().HaveCount(1);
    }

    private static DiffResult CreateDiffResult(DiffChange[] changes)
    {
        return new DiffResult(
            BaseVersion: "base",
            TargetVersion: "target",
            Changes: changes,
            Summary: new DiffSummary(
                changes.Count(c => c.ChangeType == DiffChangeTypes.Added),
                changes.Count(c => c.ChangeType == DiffChangeTypes.Removed),
                changes.Count(c => c.ChangeType == DiffChangeTypes.Modified),
                0
            )
        );
    }
}

public class PatchGeneratorTests
{
    private readonly TestLogger _logger;
    private readonly PatchGenerator _generator;

    public PatchGeneratorTests()
    {
        _logger = new TestLogger();
        _generator = new PatchGenerator(_logger);
    }

    [Fact]
    public void GeneratePatchesFromThreeWay_ShouldCreatePatchesForSafeChanges()
    {
        // Arrange
        var threeWayResult = new ThreeWayDiffResult(
            OriginalVersion: "v1.0",
            UpdatedVersion: "v1.1",
            ModdedVersion: "mod",
            GameChanges: new[]
            {
                new DiffChange(new[] { "Export[0]", "Health" }, DiffChangeTypes.Modified, 100, 150)
            },
            ModChanges: new[]
            {
                new DiffChange(new[] { "Export[0]", "Shield" }, DiffChangeTypes.Added, null, 50)
            },
            Conflicts: System.Array.Empty<DiffConflict>(),
            SafeToApply: new[]
            {
                new DiffChange(new[] { "Export[0]", "Shield" }, DiffChangeTypes.Added, null, 50)
            }
        );

        // Act
        var patchSet = _generator.GeneratePatchesFromThreeWay(threeWayResult);

        // Assert
        patchSet.Patches.Should().HaveCount(1);
        patchSet.Patches[0].Operation.Should().Be(PatchOperations.Add);
        patchSet.Patches[0].RequiresReview.Should().BeFalse();
        patchSet.AutoApplyCount.Should().Be(1);
        patchSet.ReviewCount.Should().Be(0);
    }

    [Fact]
    public void GeneratePatchesFromThreeWay_ShouldFlagConflictsForReview()
    {
        // Arrange
        var threeWayResult = new ThreeWayDiffResult(
            OriginalVersion: "v1.0",
            UpdatedVersion: "v1.1",
            ModdedVersion: "mod",
            GameChanges: System.Array.Empty<DiffChange>(),
            ModChanges: System.Array.Empty<DiffChange>(),
            Conflicts: new[]
            {
                new DiffConflict(
                    new[] { "Export[0]", "Health" },
                    100, 150, 200, null
                )
            },
            SafeToApply: System.Array.Empty<DiffChange>()
        );

        // Act
        var patchSet = _generator.GeneratePatchesFromThreeWay(threeWayResult);

        // Assert
        patchSet.Patches.Should().HaveCount(1);
        patchSet.Patches[0].RequiresReview.Should().BeTrue();
        patchSet.ReviewCount.Should().Be(1);
        patchSet.AutoApplyCount.Should().Be(0);
    }
}
