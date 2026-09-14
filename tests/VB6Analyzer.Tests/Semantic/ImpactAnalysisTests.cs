using FluentAssertions;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Semantic;

public class ImpactAnalysisTests
{
    [Fact]
    public void ImpactAnalyzer_ShouldCalculateRoutineBlastRadius()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var queries = new CallGraphQueries(graph);
        var impactAnalyzer = new ImpactAnalyzer(graph, queries);

        var dbExecute = graph.Symbols.FirstOrDefault(s => s.QualifiedName.EndsWith("DatabaseHelper.Execute", StringComparison.OrdinalIgnoreCase));
        dbExecute.Should().NotBeNull();

        // Act
        var impact = impactAnalyzer.AnalyzeSymbolImpact(dbExecute!);

        // Assert
        impact.Should().NotBeNull();
        impact.DirectCallers.Should().NotBeEmpty();
        impact.AffectedClasses.Should().NotBeEmpty();
        impact.RiskLevel.Should().NotBe(ImpactRiskLevel.Low);
    }

    [Fact]
    public void ImpactAnalyzer_ShouldCalculateTypeBlastRadius()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var queries = new CallGraphQueries(graph);
        var impactAnalyzer = new ImpactAnalyzer(graph, queries);

        var customerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name.Equals("Customer", StringComparison.OrdinalIgnoreCase));
        customerClass.Should().NotBeNull();

        // Act
        var classImpact = impactAnalyzer.AnalyzeSymbolImpact(customerClass!);

        // Assert
        classImpact.Should().NotBeNull();
        classImpact.TargetSymbol.Should().Be(customerClass);
        classImpact.AffectedClasses.Should().NotBeEmpty();
    }
}
