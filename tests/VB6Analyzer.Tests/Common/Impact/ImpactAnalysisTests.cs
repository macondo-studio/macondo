using FluentAssertions;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Impact;

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

    [Fact]
    public void ImpactAnalyzer_ShouldThrottleByMaxDepth()
    {
        // Arrange: Chain of calls A -> B -> C -> D (target D)
        var graph = new CodeGraph();
        var a = new Symbol { Id = Guid.NewGuid(), Name = "A", QualifiedName = "M.A", FilePath = "M.bas", Kind = SymbolKind.Method };
        var b = new Symbol { Id = Guid.NewGuid(), Name = "B", QualifiedName = "M.B", FilePath = "M.bas", Kind = SymbolKind.Method };
        var c = new Symbol { Id = Guid.NewGuid(), Name = "C", QualifiedName = "M.C", FilePath = "M.bas", Kind = SymbolKind.Method };
        var d = new Symbol { Id = Guid.NewGuid(), Name = "D", QualifiedName = "M.D", FilePath = "M.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { a, b, c, d });
        graph.AddCalls(a, b, 1);
        graph.AddCalls(b, c, 2);
        graph.AddCalls(c, d, 3);

        var impactAnalyzer = new ImpactAnalyzer(graph);

        // Act
        var impactDepth1 = impactAnalyzer.AnalyzeSymbolImpact(d, maxDepth: 1);
        var impactDepth3 = impactAnalyzer.AnalyzeSymbolImpact(d, maxDepth: 3);

        // Assert
        impactDepth1.DirectCallers.Should().HaveCount(1);
        impactDepth1.DirectCallers[0].Id.Should().Be(c.Id);
        impactDepth1.IndirectCallers.Should().BeEmpty();

        impactDepth3.DirectCallers.Should().HaveCount(1);
        impactDepth3.IndirectCallers.Should().HaveCount(2); // b and a
    }

    [Fact]
    public void ImpactAnalyzer_ShouldHandleIsolatedSymbolsWithZeroCallers()
    {
        // Arrange
        var graph = new CodeGraph();
        var isolated = new Symbol { Id = Guid.NewGuid(), Name = "OrphanMethod", QualifiedName = "Mod.OrphanMethod", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        graph.Symbols.Add(isolated);

        var impactAnalyzer = new ImpactAnalyzer(graph);

        // Act
        var impact = impactAnalyzer.AnalyzeSymbolImpact(isolated);

        // Assert
        impact.Should().NotBeNull();
        impact.DirectCallers.Should().BeEmpty();
        impact.IndirectCallers.Should().BeEmpty();
        impact.MaxDependencyDepth.Should().Be(0);
        impact.RiskLevel.Should().Be(ImpactRiskLevel.Low);
    }

    [Fact]
    public void ImpactAnalyzer_ShouldCalculateFieldAndConstantImpact()
    {
        // Arrange
        var graph = new CodeGraph();
        var mod = new Symbol { Id = Guid.NewGuid(), Name = "ConfigModule", QualifiedName = "ConfigModule", FilePath = "ConfigModule.bas", Kind = SymbolKind.Module };
        var constSym = new Symbol { Id = Guid.NewGuid(), Name = "API_KEY", QualifiedName = "ConfigModule.API_KEY", FilePath = "ConfigModule.bas", Kind = SymbolKind.Constant };
        var userMethod = new Symbol { Id = Guid.NewGuid(), Name = "Connect", QualifiedName = "Worker.Connect", FilePath = "Worker.cls", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { mod, constSym, userMethod });
        graph.AddContains(mod, constSym);
        graph.AddUsesType(userMethod, constSym);

        var impactAnalyzer = new ImpactAnalyzer(graph);

        // Act
        var impact = impactAnalyzer.AnalyzeSymbolImpact(constSym);

        // Assert
        impact.Should().NotBeNull();
        impact.AffectedModules.Should().Contain(m => m.Id == mod.Id);
        impact.TypeDependents.Should().Contain(d => d.Id == userMethod.Id);
    }
}
