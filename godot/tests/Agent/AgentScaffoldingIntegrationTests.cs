using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FluentAssertions;
using UAssetViewer.Agent;
using UAssetViewer.Agent.Capabilities;
using UAssetViewer.Assets;
using UAssetViewer.Models;
using Xunit;

namespace UAssetViewer.Tests.Agent;

public sealed class AgentScaffoldingIntegrationTests : IDisposable
{
    private readonly string _projectPath;

    public AgentScaffoldingIntegrationTests()
    {
        _projectPath = Path.Combine(
            Path.GetTempPath(),
            "korino-agent-scaffold-e2e",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path.Combine(_projectPath, "Content", "Weapons"));
        File.WriteAllText(Path.Combine(_projectPath, "Content", "Weapons", "Sword.uasset"), "uasset");
        File.WriteAllText(Path.Combine(_projectPath, "Content", "Weapons", "Sword.uexp"), "uexp");
    }

    [Fact]
    public void OpenProject_ScanMetadataAndSelection_FlowsEndToEnd()
    {
        // Arrange
        var logger = new TestLogger();
        var projectProvider = new MutableProjectPathProvider(_projectPath);
        var dataAccess = new InMemoryDependencyDataAccess();
        dataAccess.Seed(
            _projectPath,
            new DependencyGraphStats(
                Exists: true,
                AssetCount: 2,
                EdgeCount: 1,
                EngineVersion: "VER_UE4_27",
                ScannedAt: new DateTime(2026, 2, 27, 0, 0, 0, DateTimeKind.Utc)),
            new Dictionary<string, DependencyEdge[]>
            {
                ["Content/Weapons/Sword.uasset"] = new[]
                {
                    new DependencyEdge("Content/VFX/Spark.uasset", "Material"),
                },
            },
            new Dictionary<string, AssetMetadataSnapshot>
            {
                ["Content/Weapons/Sword.uasset"] = CreateSwordMetadata(),
            });

        var selectionController = new InMemorySelectionStateController();
        var broadcaster = new CapturingSelectionBroadcaster();

        var registry = new AgentCapabilityRegistry(
            new ProjectExplorerCapability(projectProvider, new FileTreeBuilder(logger), logger),
            new DependencyGraphCapability(projectProvider, dataAccess, logger),
            new MetadataCapability(projectProvider, dataAccess, logger),
            new GuiSelectionCapability(selectionController, broadcaster, logger));

        // Act + Assert (open project)
        var rootNodes = registry.ProjectExplorer.GetRootNodes();
        var contentFolder = rootNodes.Should()
            .ContainSingle(node => node.Type == TreeNodeTypes.Folder && node.Name == "Content")
            .Subject;

        var swordNode = registry.ProjectExplorer.Search("Sword", limit: 10)
            .Should().ContainSingle().Subject;
        swordNode.Id.Should().Be("file:Content/Weapons/Sword.uasset");

        // Act + Assert (scan/dependency query path)
        var stats = registry.DependencyGraph.GetStats();
        stats.Exists.Should().BeTrue();
        stats.AssetCount.Should().Be(2);
        stats.EdgeCount.Should().Be(1);

        var dependencies = registry.DependencyGraph.GetDependencies("Content/Weapons/Sword.uasset");
        dependencies.Should().ContainSingle();
        dependencies[0].Path.Should().Be("Content/VFX/Spark.uasset");

        // Act + Assert (metadata query path)
        var metadata = registry.Metadata.GetAssetMetadata("Content/Weapons/Sword.uasset", rowLimit: 50);
        metadata.Should().NotBeNull();
        metadata!.Summary.AssetPath.Should().Be("Content/Weapons/Sword.uasset");
        metadata.Summary.PropertyCount.Should().Be(1);
        metadata.Properties.Should().ContainSingle(p => p.Name == "Damage");

        // Act + Assert (GUI selection path)
        var expandedState = registry.GuiSelection.ExpandNodes(new[] { contentFolder.Id });
        expandedState.ExpandedIds.Should().Contain(contentFolder.Id);

        var selectedState = registry.GuiSelection.SelectNode(swordNode.Id);
        selectedState.SelectedId.Should().Be(swordNode.Id);
        selectedState.ExpandedIds.Should().Contain(contentFolder.Id);

        broadcaster.Broadcasts.Should().HaveCount(2);
        broadcaster.Broadcasts[^1].SelectedId.Should().Be(swordNode.Id);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_projectPath))
            {
                Directory.Delete(_projectPath, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup for temporary integration test projects.
        }
    }

    private static AssetMetadataSnapshot CreateSwordMetadata()
    {
        return new AssetMetadataSnapshot(
            new AssetMetadataSummary(
                AssetPath: "Content/Weapons/Sword.uasset",
                AssetType: "uasset",
                ImportCount: 1,
                ExportCount: 1,
                PropertyCount: 1,
                EdgeCount: 1),
            new[]
            {
                new MetadataImport(0, "Spark", "Material", "/Game/VFX", false),
            },
            new[]
            {
                new MetadataExport(0, "Sword", "StaticMesh", null, 128),
            },
            new[]
            {
                new MetadataProperty(0, "Sword", "Damage", "IntProperty", "42", 42, null, null),
            },
            new[]
            {
                new MetadataEdge("Content/VFX/Spark.uasset", "Material"),
            });
    }

    private sealed class MutableProjectPathProvider : IProjectPathProvider
    {
        public MutableProjectPathProvider(string? currentProjectPath)
        {
            CurrentProjectPath = currentProjectPath;
        }

        public string? CurrentProjectPath { get; set; }
    }

    private sealed class CapturingSelectionBroadcaster : ISelectionBroadcaster
    {
        public List<SelectionState> Broadcasts { get; } = new();

        public void Broadcast(SelectionState state)
        {
            Broadcasts.Add(state);
        }
    }

    private sealed class InMemorySelectionStateController : ISelectionStateController
    {
        public SelectionState CurrentState { get; private set; } =
            new SelectionState(null, Array.Empty<string>());

        public SelectionState SelectNode(string? nodeId)
        {
            CurrentState = new SelectionState(nodeId, CurrentState.ExpandedIds, CurrentState.FocusedPropertyPath);
            return CurrentState;
        }

        public SelectionState ExpandNodes(string[] nodeIds)
        {
            var expanded = CurrentState.ExpandedIds
                .Concat(nodeIds ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            CurrentState = new SelectionState(CurrentState.SelectedId, expanded, CurrentState.FocusedPropertyPath);
            return CurrentState;
        }

        public SelectionState CollapseNodes(string[] nodeIds)
        {
            var toRemove = new HashSet<string>(nodeIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var expanded = CurrentState.ExpandedIds
                .Where(id => !toRemove.Contains(id))
                .ToArray();

            CurrentState = new SelectionState(CurrentState.SelectedId, expanded, CurrentState.FocusedPropertyPath);
            return CurrentState;
        }

        public SelectionState CollapseAll()
        {
            CurrentState = new SelectionState(CurrentState.SelectedId, Array.Empty<string>(), CurrentState.FocusedPropertyPath);
            return CurrentState;
        }
    }

    private sealed class InMemoryDependencyDataAccess : IDependencyDataAccess
    {
        private readonly Dictionary<string, DependencyGraphStats> _statsByProject =
            new(StringComparer.Ordinal);

        private readonly Dictionary<(string ProjectPath, string AssetPath), DependencyEdge[]> _dependencies =
            new();

        private readonly Dictionary<(string ProjectPath, string AssetPath), AssetMetadataSnapshot> _metadata =
            new();

        public void Seed(
            string projectPath,
            DependencyGraphStats stats,
            Dictionary<string, DependencyEdge[]> dependenciesByAsset,
            Dictionary<string, AssetMetadataSnapshot> metadataByAsset)
        {
            _statsByProject[projectPath] = stats;

            foreach (var (assetPath, edges) in dependenciesByAsset)
            {
                _dependencies[(projectPath, assetPath)] = edges;
            }

            foreach (var (assetPath, metadata) in metadataByAsset)
            {
                _metadata[(projectPath, assetPath)] = metadata;
            }
        }

        public DependencyGraphStats GetStats(string projectPath, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return _statsByProject.TryGetValue(projectPath, out var stats)
                ? stats
                : new DependencyGraphStats(false);
        }

        public DependencyEdge[] GetDependencies(string projectPath, string assetPath, int limit, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return _dependencies.TryGetValue((projectPath, assetPath), out var edges)
                ? edges.Take(limit).ToArray()
                : Array.Empty<DependencyEdge>();
        }

        public DependencyEdge[] GetDependents(string projectPath, string assetPath, int limit, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            return _dependencies
                .Where(kvp => string.Equals(kvp.Key.ProjectPath, projectPath, StringComparison.Ordinal))
                .Where(kvp => kvp.Value.Any(edge => string.Equals(edge.Path, assetPath, StringComparison.Ordinal)))
                .Select(kvp => new DependencyEdge(kvp.Key.AssetPath, "Dependent"))
                .Take(limit)
                .ToArray();
        }

        public string[] GetRelated(string projectPath, string assetPath, int maxDepth, int limit, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return GetDependencies(projectPath, assetPath, limit, ct)
                .Select(edge => edge.Path)
                .Take(limit)
                .ToArray();
        }

        public ClassSearchHit[] SearchByClass(string projectPath, string className, int limit, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            return _metadata
                .Where(kvp => string.Equals(kvp.Key.ProjectPath, projectPath, StringComparison.Ordinal))
                .SelectMany(kvp => kvp.Value.Exports.Select(exportRow => new { kvp.Key.AssetPath, Export = exportRow }))
                .Where(row => string.Equals(row.Export.ClassName, className, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .Select((row, index) => new ClassSearchHit(row.AssetPath, index, row.Export.ObjectName, row.Export.ClassName))
                .ToArray();
        }

        public PropertySearchHit[] SearchProperties(
            string projectPath,
            string propertyName,
            string? valueFilter,
            int limit,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            return _metadata
                .Where(kvp => string.Equals(kvp.Key.ProjectPath, projectPath, StringComparison.Ordinal))
                .SelectMany(kvp => kvp.Value.Properties.Select(prop => new { kvp.Key.AssetPath, Property = prop }))
                .Where(row => string.Equals(row.Property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                .Where(row => valueFilter == null ||
                              (row.Property.ValueText?.Contains(valueFilter, StringComparison.OrdinalIgnoreCase) ?? false))
                .Take(limit)
                .Select(row => new PropertySearchHit(
                    row.AssetPath,
                    row.Property.ExportName,
                    row.Property.Name,
                    row.Property.PropertyType,
                    row.Property.ValueText))
                .ToArray();
        }

        public AssetMetadataSnapshot? GetAssetMetadata(
            string projectPath,
            string assetPath,
            int rowLimit,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (!_metadata.TryGetValue((projectPath, assetPath), out var snapshot))
            {
                return null;
            }

            return snapshot with
            {
                Imports = snapshot.Imports.Take(rowLimit).ToArray(),
                Exports = snapshot.Exports.Take(rowLimit).ToArray(),
                Properties = snapshot.Properties.Take(rowLimit).ToArray(),
                Edges = snapshot.Edges.Take(rowLimit).ToArray(),
            };
        }
    }
}
