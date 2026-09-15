using FluentAssertions;
using Macondo.Semantic.Graph;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Graph;

public class CallGraphQueriesTests
{
    [Fact]
    public void CallGraphQueries_ShouldReturnDirectCallersAndCallees()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var queries = new CallGraphQueries(graph);

        var dbExecute = graph.Symbols.FirstOrDefault(s => s.QualifiedName.EndsWith("DatabaseHelper.Execute", StringComparison.OrdinalIgnoreCase));
        dbExecute.Should().NotBeNull();

        // Act
        var directCallers = queries.GetDirectCallers(dbExecute!);

        // Assert
        directCallers.Should().NotBeEmpty();

        // Check callee query
        var orderServiceProcess = graph.Symbols.FirstOrDefault(s => s.QualifiedName.EndsWith("OrderService.ProcessOrder", StringComparison.OrdinalIgnoreCase));
        orderServiceProcess.Should().NotBeNull();

        var directCallees = queries.GetDirectCallees(orderServiceProcess!);
        directCallees.Should().NotBeEmpty();
    }

    [Fact]
    public void CallGraphQueries_ShouldReturnInstantiatedClasses()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var queries = new CallGraphQueries(graph);

        var orderServiceProcess = graph.Symbols.FirstOrDefault(s => s.QualifiedName.EndsWith("OrderService.ProcessOrder", StringComparison.OrdinalIgnoreCase));
        orderServiceProcess.Should().NotBeNull();

        // Act
        var instantiated = queries.GetInstantiatedClasses(orderServiceProcess!);

        // Assert
        instantiated.Should().NotBeEmpty();
    }
}
