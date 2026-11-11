using FluentAssertions;
using Macondo.Semantic.Analytics.CodeClones;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Graph;

public class GraphAndFlowUncoveredEdgeCasesAndBugsHunterTests
{
    #region Deep Execution Paths and Multi-Branch Routing

    /// <summary>
    /// Verify that CallGraphQueries finds deep execution paths (linear chain > 8 levels)
    /// between the initial source node and the final target node.
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldFindDeepLinearExecutionPath()
    {
        // Arrange
        var graph = new CodeGraph();
        var nodes = new List<Symbol>();

        for (int i = 0; i < 10; i++)
        {
            var symbol = new Symbol
            {
                Id = Guid.NewGuid(),
                Name = $"Step_{i}",
                QualifiedName = $"Pipeline.Step_{i}",
                FilePath = "Pipeline.cs",
                Kind = SymbolKind.Method
            };
            nodes.Add(symbol);
            graph.Symbols.Add(symbol);
        }

        for (int i = 0; i < nodes.Count - 1; i++)
        {
            graph.AddCalls(nodes[i], nodes[i + 1]);
        }

        var queries = new CallGraphQueries(graph);

        // Act
        var paths = queries.FindExecutionPaths(nodes[0], nodes[^1]);

        // Assert
        paths.Should().NotBeEmpty();
        var longestPath = paths.OrderByDescending(p => p.Length).First();
        longestPath.Source.Should().Be(nodes[0]);
        longestPath.Target.Should().Be(nodes[^1]);
        longestPath.Length.Should().Be(9);
    }

    /// <summary>
    /// Verify that CallGraphQueries finds multiple parallel alternative paths (diamond/multi-path graph)
    /// that connect the same source to the same target.
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldFindMultipleParallelPaths_InDiamondTopology()
    {
        // Arrange
        var graph = new CodeGraph();

        var root = new Symbol { Id = Guid.NewGuid(), Name = "Root", QualifiedName = "Graph.Root", FilePath = "Graph.cs", Kind = SymbolKind.Method };
        var branchA = new Symbol { Id = Guid.NewGuid(), Name = "BranchA", QualifiedName = "Graph.BranchA", FilePath = "Graph.cs", Kind = SymbolKind.Method };
        var branchB = new Symbol { Id = Guid.NewGuid(), Name = "BranchB", QualifiedName = "Graph.BranchB", FilePath = "Graph.cs", Kind = SymbolKind.Method };
        var sink = new Symbol { Id = Guid.NewGuid(), Name = "Sink", QualifiedName = "Graph.Sink", FilePath = "Graph.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { root, branchA, branchB, sink });

        // Path 1: Root -> BranchA -> Sink
        graph.AddCalls(root, branchA);
        graph.AddCalls(branchA, sink);

        // Path 2: Root -> BranchB -> Sink
        graph.AddCalls(root, branchB);
        graph.AddCalls(branchB, sink);

        var queries = new CallGraphQueries(graph);

        // Act
        var paths = queries.FindExecutionPaths(root, sink);

        // Assert
        paths.Should().HaveCount(2, "Ci sono due percorsi alternativi indipendenti da Root a Sink");
        paths.Should().Contain(p => p.Steps.Any(s => s.Id == branchA.Id));
        paths.Should().Contain(p => p.Steps.Any(s => s.Id == branchB.Id));
    }

    #endregion

    #region DSM Deep Cyclic Loops and Self Loops

    /// <summary>
    /// Verify that DsmMatrixBuilder detects complex 5-node loops (A -> B -> C -> D -> E -> A)
    /// grouping them into a single strongly connected component (SCC).
    /// </summary>
    [Fact]
    public void DsmMatrix_ShouldDetectFiveNodeCircularDependencyLoop()
    {
        // Arrange
        var graph = new CodeGraph();
        var symbols = new List<Symbol>();

        for (int i = 1; i <= 5; i++)
        {
            var sym = new Symbol
            {
                Id = Guid.NewGuid(),
                Name = $"Mod_{i}",
                QualifiedName = $"Mod_{i}",
                FilePath = $"Mod_{i}.bas",
                Kind = SymbolKind.Module
            };
            symbols.Add(sym);
            graph.Symbols.Add(sym);
        }

        // Create cyclic ring: 1 -> 2 -> 3 -> 4 -> 5 -> 1
        graph.AddCalls(symbols[0], symbols[1]);
        graph.AddCalls(symbols[1], symbols[2]);
        graph.AddCalls(symbols[2], symbols[3]);
        graph.AddCalls(symbols[3], symbols[4]);
        graph.AddCalls(symbols[4], symbols[0]);

        // Act
        var dsm = DsmMatrixBuilder.Build(graph);

        // Assert
        dsm.Should().NotBeNull();
        dsm.StronglyConnectedComponents.Should().HaveCount(1);
        dsm.TotalCyclicDependencies.Should().Be(5);
        foreach (var sym in symbols)
        {
            var item = dsm.Items.FirstOrDefault(i => i.Id == sym.Id);
            item.Should().NotBeNull();
            item!.HasCircularDependencies.Should().BeTrue();
        }
    }

    /// <summary>
    /// Check that self-loop (recursive function that invokes itself A -> A) does not create infinite loops in the DSM analysis.
    /// </summary>
    [Fact]
    public void DsmMatrix_ShouldHandleSelfLoopRecursion_Gracefully()
    {
        // Arrange
        var graph = new CodeGraph();
        var recursiveMod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "FactorialMod",
            QualifiedName = "FactorialMod",
            FilePath = "FactorialMod.bas",
            Kind = SymbolKind.Module
        };
        graph.Symbols.Add(recursiveMod);
        graph.AddCalls(recursiveMod, recursiveMod);

        // Act
        var dsm = DsmMatrixBuilder.Build(graph);

        // Assert
        dsm.Should().NotBeNull();
        dsm.Size.Should().Be(1);
        dsm.TotalDependencies.Should().Be(0);
        dsm.Items[0].HasCircularDependencies.Should().BeFalse();
    }

    #endregion

    #region Code Clone Detection Multi-Group Scenarios

    /// <summary>
    /// Verify that CodeCloneDetector detects multiple disjoint duplicate groups across different files and lengths.
    /// </summary>
    [Fact]
    public void CodeClones_ShouldIdentifyMultipleDistinctCloneGroups()
    {
        // Arrange
        var graph = new CodeGraph();

        var sharedLog = new Symbol { Id = Guid.NewGuid(), Name = "LogEntry", QualifiedName = "Logger.LogEntry", FilePath = "Logger.cs", Kind = SymbolKind.Method };
        var sharedAlert = new Symbol { Id = Guid.NewGuid(), Name = "SendAlert", QualifiedName = "Alerts.SendAlert", FilePath = "Alerts.cs", Kind = SymbolKind.Method };

        // Group 1 (Clones A1 and A2)
        var cloneA1 = new Symbol { Id = Guid.NewGuid(), Name = "TaskA1", QualifiedName = "A1.TaskA1", FilePath = "A1.cs", Kind = SymbolKind.Method, LineStart = 10, LineEnd = 25 };
        var cloneA2 = new Symbol { Id = Guid.NewGuid(), Name = "TaskA2", QualifiedName = "A2.TaskA2", FilePath = "A2.cs", Kind = SymbolKind.Method, LineStart = 50, LineEnd = 65 };

        // Group 2 (Clones B1 and B2)
        var cloneB1 = new Symbol { Id = Guid.NewGuid(), Name = "TaskB1", QualifiedName = "B1.TaskB1", FilePath = "B1.cs", Kind = SymbolKind.Method, LineStart = 100, LineEnd = 130 };
        var cloneB2 = new Symbol { Id = Guid.NewGuid(), Name = "TaskB2", QualifiedName = "B2.TaskB2", FilePath = "B2.cs", Kind = SymbolKind.Method, LineStart = 200, LineEnd = 230 };

        graph.Symbols.AddRange(new[] { sharedLog, sharedAlert, cloneA1, cloneA2, cloneB1, cloneB2 });

        graph.AddCalls(cloneA1, sharedLog);
        graph.AddCalls(cloneA2, sharedLog);

        graph.AddCalls(cloneB1, sharedAlert);
        graph.AddCalls(cloneB2, sharedAlert);

        // Act
        var report = CodeCloneDetector.DetectClones(graph, minLineCount: 5);

        // Assert
        report.Should().NotBeNull();
        report.TotalDuplicateGroups.Should().BeGreaterOrEqualTo(2);
        report.TotalRedundantLines.Should().BeGreaterThan(0);
    }

    #endregion
}
