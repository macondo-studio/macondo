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
    /// Test impact analysis on a fully isolated symbol (0 callers),
    /// confirming a zero Blast Radius and a minimal level of risk.
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
    /// Verify that ImpactAnalyzer handles graphs with mutual call loops (A -> B -> C -> A)
    /// without entering infinite recursion or raising StackOverflowException.
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
    /// Verify that the search for execution paths between two unconnected nodes in disjoint sub-graphs
    /// returns an empty list efficiently.
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
    /// Verify that the search for paths when the source matches the destination (source == target)
    /// handle the edge case without errors.
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
