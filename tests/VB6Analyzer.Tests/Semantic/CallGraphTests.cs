using FluentAssertions;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Semantic;

public class CallGraphTests
{
    [Fact]
    public void CallGraphBuilder_ShouldGenerateCallsAndInstantiatesOnFixtures()
    {
        // Arrange & Act
        var (graph, extractionResults) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Assert
        graph.Symbols.Should().NotBeEmpty();
        graph.Relationships.Should().NotBeEmpty();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls).ToList();
        var instantiates = graph.Relationships.Where(r => r.Kind == RelationshipKind.Instantiates).ToList();
        var usesTypes = graph.Relationships.Where(r => r.Kind == RelationshipKind.UsesType).ToList();

        calls.Should().NotBeEmpty("CallGraph should contain CALLS relationships");
        instantiates.Should().NotBeEmpty("CallGraph should contain INSTANTIATES relationships");
        usesTypes.Should().NotBeEmpty("CallGraph should contain USES_TYPE relationships");
    }

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
