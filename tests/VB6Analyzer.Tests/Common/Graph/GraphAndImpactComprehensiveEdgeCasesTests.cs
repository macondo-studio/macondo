using FluentAssertions;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Graph;

public class GraphAndImpactComprehensiveEdgeCasesTests
{
    #region Impact Analysis Edge Cases

    /// <summary>
    /// Verifica l'analisi d'impatto su un simbolo completamente isolato (0 chiamanti),
    /// confermando un Blast Radius nullo e un livello di rischio minimo.
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldReportMinimalRisk_ForIsolatedSymbol()
    {
        // Arrange
        var graph = new CodeGraph();
        var isolatedMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "OrphanUtility",
            QualifiedName = "Util.OrphanUtility",
            FilePath = "Util.cs",
            Kind = SymbolKind.Method
        };
        graph.Symbols.Add(isolatedMethod);

        var impactAnalyzer = new ImpactAnalyzer(graph);

        // Act
        var report = impactAnalyzer.AnalyzeSymbolImpact(isolatedMethod);

        // Assert
        report.Should().NotBeNull();
        report.DirectCallers.Should().BeEmpty();
        report.IndirectCallers.Should().BeEmpty();
        report.MaxDependencyDepth.Should().Be(0);
        report.RiskLevel.Should().Be(ImpactRiskLevel.Low);
    }

    /// <summary>
    /// Verifica che ImpactAnalyzer gestisca grafi con cicli di chiamate mutue (A -> B -> C -> A)
    /// senza entrare in ricorsione infinita o sollevare StackOverflowException.
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldHandleCyclicCallChains_WithoutInfiniteRecursion()
    {
        // Arrange
        var graph = new CodeGraph();

        var nodeA = new Symbol { Id = Guid.NewGuid(), Name = "NodeA", QualifiedName = "Mod.NodeA", FilePath = "Mod.cs", Kind = SymbolKind.Method };
        var nodeB = new Symbol { Id = Guid.NewGuid(), Name = "NodeB", QualifiedName = "Mod.NodeB", FilePath = "Mod.cs", Kind = SymbolKind.Method };
        var nodeC = new Symbol { Id = Guid.NewGuid(), Name = "NodeC", QualifiedName = "Mod.NodeC", FilePath = "Mod.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { nodeA, nodeB, nodeC });

        graph.AddCalls(nodeA, nodeB);
        graph.AddCalls(nodeB, nodeC);
        graph.AddCalls(nodeC, nodeA);

        var impactAnalyzer = new ImpactAnalyzer(graph);

        // Act
        var act = () => impactAnalyzer.AnalyzeSymbolImpact(nodeB);

        // Assert
        act.Should().NotThrow();
        var report = act();
        report.Should().NotBeNull();
        report.DirectCallers.Should().HaveCount(1); // NodeA
    }

    #endregion

    #region CallGraphQueries Boundary and Pathing Scenarios

    /// <summary>
    /// Verifica che la ricerca di percorsi di esecuzione tra due nodi non connessi in sotto-grafi disgiunti
    /// restituisca una lista vuota in modo efficiente.
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldReturnEmptyPaths_ForDisconnectedNodes()
    {
        // Arrange
        var graph = new CodeGraph();

        // Subgraph 1
        var node1 = new Symbol { Id = Guid.NewGuid(), Name = "Alpha", QualifiedName = "Alpha", FilePath = "Alpha.cs", Kind = SymbolKind.Method };
        var node2 = new Symbol { Id = Guid.NewGuid(), Name = "Beta", QualifiedName = "Beta", FilePath = "Beta.cs", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { node1, node2 });
        graph.AddCalls(node1, node2);

        // Subgraph 2 (disjoint)
        var node3 = new Symbol { Id = Guid.NewGuid(), Name = "Gamma", QualifiedName = "Gamma", FilePath = "Gamma.cs", Kind = SymbolKind.Method };
        var node4 = new Symbol { Id = Guid.NewGuid(), Name = "Delta", QualifiedName = "Delta", FilePath = "Delta.cs", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { node3, node4 });
        graph.AddCalls(node3, node4);

        var queries = new CallGraphQueries(graph);

        // Act
        var paths = queries.FindExecutionPaths(node1, node4);

        // Assert
        paths.Should().BeEmpty("I due nodi appartengono a grafi separati senza collegamenti reciproci");
    }

    /// <summary>
    /// Verifica che la ricerca di percorsi quando la sorgente coincide con la destinazione (source == target)
    /// gestisca il caso limite senza errori.
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldHandleSameSourceAndTarget_Gracefully()
    {
        // Arrange
        var graph = new CodeGraph();
        var node = new Symbol { Id = Guid.NewGuid(), Name = "SelfNode", QualifiedName = "SelfNode", FilePath = "Self.cs", Kind = SymbolKind.Method };
        graph.Symbols.Add(node);

        var queries = new CallGraphQueries(graph);

        // Act
        var act = () => queries.FindExecutionPaths(node, node);

        // Assert
        act.Should().NotThrow();
    }

    #endregion
}
