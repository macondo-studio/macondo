using FluentAssertions;
using Macondo.App.Layout;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.App;

public class GraphVisualAdapterTests
{
    /// <summary>
    /// Verifica che GraphVisualAdapter converta un CodeGraph semantico in un VisualCodeGraph
    /// contenente tutti i nodi e gli archi corrispondenti, preservando il riferimento al simbolo semantico.
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

        // Tutti i simboli semantici (escluso il progetto sintetico) devono avere un nodo visuale corrispondente
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
    /// Verifica che, qualora il CodeGraph non contenga un simbolo Project esplicito,
    /// GraphVisualAdapter crei un nodo radice di progetto sintetico con ID deterministico ProjectRootId.
    /// </summary>
    [Fact]
    public void FromSemanticGraph_ShouldInjectProjectRootNode_WhenProjectSymbolIsMissing()
    {
        // Arrange: Grafo con soli moduli e classi senza SymbolKind.Project
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

        // I moduli di primo livello devono avere il nodo radice come ParentId
        var modNode = visualGraph.Nodes.First(n => n.Id == mod.Id);
        var clsNode = visualGraph.Nodes.First(n => n.Id == cls.Id);
        modNode.ParentId.Should().Be(GraphVisualAdapter.ProjectRootId);
        clsNode.ParentId.Should().Be(GraphVisualAdapter.ProjectRootId);
    }

    /// <summary>
    /// Verifica che le relazioni semantiche tra moduli appartenenti a progetti diversi
    /// vengano correttamente contrassegnate come archi cross-project (IsCrossProject = true).
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

        // Chiamata cross-project da method1 (in Project1) a method2 (in Project2)
        semanticGraph.AddCalls(method1, method2);

        // Act
        var visualGraph = GraphVisualAdapter.FromSemanticGraph(semanticGraph);

        // Assert
        var callEdge = visualGraph.Edges.FirstOrDefault(e => e.SourceId == method1.Id && e.TargetId == method2.Id);
        callEdge.Should().NotBeNull();
        callEdge!.IsCrossProject.Should().BeTrue();
    }

    /// <summary>
    /// Verifica che un CodeGraph completamente vuoto venga gestito senza errori, restituendo un VisualCodeGraph vuoto.
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
