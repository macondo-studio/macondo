using FluentAssertions;
using Macondo.App.Layout;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.App;

public class GraphVisualAdapterTests
{
    /// <summary>
    /// Verify that GraphVisualAdapter converts a semantic CodeGraph to a VisualCodeGraph
    /// containing all the corresponding nodes and edges, preserving the reference to the semantic symbol.
    /// </summary>
    [Fact]
    public void FromSemanticGraph_ShouldConvertSymbolsAndRelationshipsToVisualGraph()
    {
        // Arrange
        var (semanticGraph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var visualGraph = GraphVisualAdapter.FromSemanticGraph(semanticGraph, "TestProject");

        // Assert
        visualGraph.Should().NotBeNull();
        visualGraph.SemanticGraph.Should().BeSameAs(semanticGraph);
        visualGraph.Nodes.Should().NotBeEmpty();
        visualGraph.Edges.Should().NotBeEmpty();

        // All semantic symbols (excluding the synthetic design) must have a corresponding visual node
        foreach (var sym in semanticGraph.Symbols)
        {
            var node = visualGraph.Nodes.FirstOrDefault(n => n.Id == sym.Id);
            node.Should().NotBeNull();
            node!.Name.Should().Be(sym.Name);
            node.Kind.Should().Be(sym.Kind);
            node.SemanticSymbol.Should().BeSameAs(sym);
            node.X.Should().NotBe(double.NaN);
            node.Y.Should().NotBe(double.NaN);
        }
    }

    /// <summary>
    /// Verify that if the CodeGraph does not contain an explicit Project symbol,
    /// GraphVisualAdapter creates a synthetic project root node with deterministic ID ProjectRootId.
    /// </summary>
    [Fact]
    public void FromSemanticGraph_ShouldInjectProjectRootNode_WhenProjectSymbolIsMissing()
    {
        // Arrange: Graph with only modules and classes without SymbolKind.Project
        var semanticGraph = new CodeGraph();
        var mod = new Symbol { Id = Guid.NewGuid(), Name = "Utils", QualifiedName = "Utils", FilePath = "Utils.bas", Kind = SymbolKind.Module };
        var cls = new Symbol { Id = Guid.NewGuid(), Name = "Customer", QualifiedName = "Customer", FilePath = "Customer.cls", Kind = SymbolKind.Class };
        semanticGraph.Symbols.AddRange(new[] { mod, cls });

        // Act
        var visualGraph = GraphVisualAdapter.FromSemanticGraph(semanticGraph, "MySyntheticApp");

        // Assert
        var rootNode = visualGraph.Nodes.FirstOrDefault(n => n.Id == GraphVisualAdapter.ProjectRootId);
        rootNode.Should().NotBeNull();
        rootNode!.Name.Should().Be("MySyntheticApp");
        rootNode.Kind.Should().Be(SymbolKind.Project);

        // Top-level modules must have the root node as the ParentId
        var modNode = visualGraph.Nodes.First(n => n.Id == mod.Id);
        var clsNode = visualGraph.Nodes.First(n => n.Id == cls.Id);
        modNode.ParentId.Should().Be(GraphVisualAdapter.ProjectRootId);
        clsNode.ParentId.Should().Be(GraphVisualAdapter.ProjectRootId);
    }

    /// <summary>
    /// Verify semantic relationships between modules belonging to different projects
    /// are correctly marked as cross-project arcs (IsCrossProject = true).
    /// </summary>
    [Fact]
    public void FromSemanticGraph_ShouldIdentifyCrossProjectEdges()
    {
        // Arrange
        var semanticGraph = new CodeGraph();

        var proj1 = new Symbol { Id = Guid.NewGuid(), Name = "Project1", QualifiedName = "Project1", FilePath = "Project1.vbp", Kind = SymbolKind.Project };
        var mod1 = new Symbol { Id = Guid.NewGuid(), Name = "Mod1", QualifiedName = "Project1.Mod1", FilePath = "Mod1.bas", Kind = SymbolKind.Module };
        var method1 = new Symbol { Id = Guid.NewGuid(), Name = "Do1", QualifiedName = "Project1.Mod1.Do1", FilePath = "Mod1.bas", Kind = SymbolKind.Method };

        var proj2 = new Symbol { Id = Guid.NewGuid(), Name = "Project2", QualifiedName = "Project2", FilePath = "Project2.vbp", Kind = SymbolKind.Project };
        var mod2 = new Symbol { Id = Guid.NewGuid(), Name = "Mod2", QualifiedName = "Project2.Mod2", FilePath = "Mod2.bas", Kind = SymbolKind.Module };
        var method2 = new Symbol { Id = Guid.NewGuid(), Name = "Do2", QualifiedName = "Project2.Mod2.Do2", FilePath = "Mod2.bas", Kind = SymbolKind.Method };

        semanticGraph.Symbols.AddRange(new[] { proj1, mod1, method1, proj2, mod2, method2 });
        semanticGraph.AddContains(proj1, mod1);
        semanticGraph.AddContains(mod1, method1);
        semanticGraph.AddContains(proj2, mod2);
        semanticGraph.AddContains(mod2, method2);

        // Cross-project call from method1 (in Project1) to method2 (in Project2)
        semanticGraph.AddCalls(method1, method2);

        // Act
        var visualGraph = GraphVisualAdapter.FromSemanticGraph(semanticGraph);

        // Assert
        var callEdge = visualGraph.Edges.FirstOrDefault(e => e.SourceId == method1.Id && e.TargetId == method2.Id);
        callEdge.Should().NotBeNull();
        callEdge!.IsCrossProject.Should().BeTrue();
    }

    /// <summary>
    /// Verify that a completely empty CodeGraph is handled without errors by returning an empty VisualCodeGraph.
    /// </summary>
    [Fact]
    public void FromSemanticGraph_ShouldHandleEmptyGraph_Gracefully()
    {
        // Arrange
        var semanticGraph = new CodeGraph();

        // Act
        var visualGraph = GraphVisualAdapter.FromSemanticGraph(semanticGraph);

        // Assert
        visualGraph.Should().NotBeNull();
        visualGraph.Nodes.Should().BeEmpty();
        visualGraph.Edges.Should().BeEmpty();
    }
}
