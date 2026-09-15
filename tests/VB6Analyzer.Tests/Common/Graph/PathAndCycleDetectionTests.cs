using FluentAssertions;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Graph;

public class PathAndCycleDetectionTests
{
    [Fact]
    public void CallGraphQueries_ShouldFindExecutionPathsBetweenRoutines()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var queries = new CallGraphQueries(graph);

        var orderServiceProcess = graph.Symbols.FirstOrDefault(s => s.QualifiedName.EndsWith("OrderService.ProcessOrder", StringComparison.OrdinalIgnoreCase));
        var dbExecute = graph.Symbols.FirstOrDefault(s => s.QualifiedName.EndsWith("DatabaseHelper.Execute", StringComparison.OrdinalIgnoreCase));

        orderServiceProcess.Should().NotBeNull();
        dbExecute.Should().NotBeNull();

        // Act
        var paths = queries.FindExecutionPaths(orderServiceProcess!, dbExecute!);

        // Assert
        paths.Should().NotBeEmpty("There should be an execution path from ProcessOrder to DatabaseHelper.Execute");
        foreach (var path in paths)
        {
            path.Source.Should().Be(orderServiceProcess);
            path.Target.Should().Be(dbExecute);
            path.Length.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void CallGraphQueries_ShouldDetectCyclesInCallGraph()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var queries = new CallGraphQueries(graph);

        // Act
        var cycles = queries.DetectCycles();

        // Assert
        cycles.Should().NotBeNull();
        cycles.Should().NotBeEmpty("Cycles from 14_CallCycles.cls fixture should be detected");
    }

    [Fact]
    public void CallGraphQueries_ShouldReturnEmptyPathList_WhenTargetIsUnreachable()
    {
        // Arrange
        var graph = new CodeGraph();
        var routineA = new Symbol { Id = Guid.NewGuid(), Name = "A", QualifiedName = "Mod.A", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var routineB = new Symbol { Id = Guid.NewGuid(), Name = "B", QualifiedName = "Mod.B", FilePath = "Mod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { routineA, routineB });
        var queries = new CallGraphQueries(graph);

        // Act
        var paths = queries.FindExecutionPaths(routineA, routineB);

        // Assert
        paths.Should().BeEmpty();
    }
}
