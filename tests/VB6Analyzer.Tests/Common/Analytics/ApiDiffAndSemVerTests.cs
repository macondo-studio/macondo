using FluentAssertions;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class ApiDiffAndSemVerTests
{
    /// <summary>
    /// Verifica che la rimozione di un metodo pubblico venga classificata come Major / Breaking Change.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenPublicMethodIsRemoved()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var publicMethodOld = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "CalculateDiscount",
            QualifiedName = "PricingService.CalculateDiscount",
            FilePath = "PricingService.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        oldGraph.Symbols.Add(publicMethodOld);

        // Nel newGraph il metodo pubblico è stato rimosso

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(1);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
        report.Differences[0].ChangeKind.Should().Be(ApiChangeKind.RemovedSymbol);
    }

    /// <summary>
    /// Verifica che l'aggiunta di una nuova interfaccia/metodo pubblico venga classificata come Minor.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectMinorAddition_WhenNewPublicApiIsAdded()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var newMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ExportToPdf",
            QualifiedName = "ReportService.ExportToPdf",
            FilePath = "ReportService.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        newGraph.Symbols.Add(newMethod);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(0);
        report.AdditionsCount.Should().Be(1);
        report.RecommendedBump.Should().Be(ApiChangeImpact.Minor);
    }
}
