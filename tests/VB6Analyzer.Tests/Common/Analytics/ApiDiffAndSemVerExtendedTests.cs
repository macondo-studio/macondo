using FluentAssertions;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class ApiDiffAndSemVerExtendedTests
{
    /// <summary>
    /// Verifica che il confronto tra due CodeGraph identici non rilevi alcuna differenza
    /// e indichi 0 modifiche e nessun impatto di rilascio.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldReturnZeroDifferences_WhenGraphsAreIdentical()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var method = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "CalculateTotal",
            QualifiedName = "Pricing.CalculateTotal",
            FilePath = "Pricing.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public,
            ReturnType = "decimal"
        };
        method.Parameters.Add(new ParameterSymbol { Name = "orderId", DeclaredTypeName = "int" });

        oldGraph.Symbols.Add(method);
        newGraph.Symbols.Add(method);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(0);
        report.AdditionsCount.Should().Be(0);
        report.TotalChanges.Should().Be(0);
    }

    /// <summary>
    /// Verifica che due CodeGraph vuoti vengano confrontati senza sollevare eccezioni.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldHandleEmptyGraphs_Gracefully()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.TotalChanges.Should().Be(0);
        report.BreakingChangesCount.Should().Be(0);
        report.AdditionsCount.Should().Be(0);
    }
}
