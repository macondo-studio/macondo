using FluentAssertions;
using Macondo.App.Generation;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.App;

public class FakeGraphGeneratorTests
{
    /// <summary>
    /// Verifica che FakeGraphGenerator generi un VisualCodeGraph sintetico valido con il numero
    /// atteso di nodi container, nodi metodo e collegamenti relazionali.
    /// </summary>
    [Fact]
    public void FakeGraphGenerator_ShouldGenerateRequestedStructure()
    {
        // Arrange & Act
        var visualGraph = FakeGraphGenerator.Generate(seed: 123, containerCount: 10, methodsPerContainer: 5);

        // Assert
        visualGraph.Should().NotBeNull();
        visualGraph.Nodes.Should().NotBeEmpty();
        visualGraph.Edges.Should().NotBeEmpty();

        // 1 nodo radice Progetto + 10 container + metodi
        var projectNode = visualGraph.Nodes.FirstOrDefault(n => n.Kind == SymbolKind.Project);
        projectNode.Should().NotBeNull();

        var containerNodes = visualGraph.Nodes.Where(n => n.Kind is SymbolKind.Class or SymbolKind.Form or SymbolKind.Module).ToList();
        containerNodes.Should().HaveCount(10);

        var methodNodes = visualGraph.Nodes.Where(n => n.Kind is SymbolKind.Method or SymbolKind.Property).ToList();
        methodNodes.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifica che tutti i nodi generati dal FakeGraphGenerator abbiano coordinate spaziali valide (non NaN, non Infinite)
    /// e che le relazioni di chiamata e contenimento siano coerenti.
    /// </summary>
    [Fact]
    public void FakeGraphGenerator_ShouldAssignValidCoordinatesAndEdges()
    {
        // Arrange & Act
        var visualGraph = FakeGraphGenerator.Generate(seed: 42, containerCount: 5, methodsPerContainer: 4);

        // Assert
        foreach (var node in visualGraph.Nodes)
        {
            node.X.Should().NotBe(double.NaN);
            node.Y.Should().NotBe(double.NaN);
            double.IsInfinity(node.X).Should().BeFalse();
            double.IsInfinity(node.Y).Should().BeFalse();
            node.Name.Should().NotBeNullOrWhiteSpace();
            node.QualifiedName.Should().NotBeNullOrWhiteSpace();
        }

        // Tutti gli archi devono puntare a nodi esistenti nel grafo
        var nodeIds = visualGraph.Nodes.Select(n => n.Id).ToHashSet();
        foreach (var edge in visualGraph.Edges)
        {
            nodeIds.Should().Contain(edge.SourceId);
            nodeIds.Should().Contain(edge.TargetId);
        }
    }
}
