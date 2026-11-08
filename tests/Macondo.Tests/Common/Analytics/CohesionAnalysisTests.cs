using FluentAssertions;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CohesionAnalysisTests
{
    /// <summary>
    /// Verifica che una classe con 2 cluster di metodi completamente disgiunti (che accedono a campi separati)
    /// produca un LCOM4 = 2 e venga contrassegnata come candidato al refactoring.
    /// </summary>
    [Fact]
    public void Cohesion_ShouldCalculateLcom4_AndDetectMultipleClusters()
    {
        // Arrange
        var graph = new CodeGraph();
        var cls = new Symbol { Id = Guid.NewGuid(), Name = "GodService", QualifiedName = "GodService", FilePath = "GodService.cs", Kind = SymbolKind.Class };

        // Cluster 1: Customer logic
        var fldCust = new Symbol { Id = Guid.NewGuid(), Name = "_customerRepo", QualifiedName = "GodService._customerRepo", FilePath = "GodService.cs", Kind = SymbolKind.Field };
        var mthSaveCust = new Symbol { Id = Guid.NewGuid(), Name = "SaveCustomer", QualifiedName = "GodService.SaveCustomer", FilePath = "GodService.cs", Kind = SymbolKind.Method };
        var mthLoadCust = new Symbol { Id = Guid.NewGuid(), Name = "LoadCustomer", QualifiedName = "GodService.LoadCustomer", FilePath = "GodService.cs", Kind = SymbolKind.Method };

        // Cluster 2: Email logic
        var fldEmail = new Symbol { Id = Guid.NewGuid(), Name = "_smtpClient", QualifiedName = "GodService._smtpClient", FilePath = "GodService.cs", Kind = SymbolKind.Field };
        var mthSendEmail = new Symbol { Id = Guid.NewGuid(), Name = "SendEmail", QualifiedName = "GodService.SendEmail", FilePath = "GodService.cs", Kind = SymbolKind.Method };
        var mthFormatEmail = new Symbol { Id = Guid.NewGuid(), Name = "FormatEmail", QualifiedName = "GodService.FormatEmail", FilePath = "GodService.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { cls, fldCust, mthSaveCust, mthLoadCust, fldEmail, mthSendEmail, mthFormatEmail });

        graph.AddContains(cls, fldCust);
        graph.AddContains(cls, mthSaveCust);
        graph.AddContains(cls, mthLoadCust);
        graph.AddContains(cls, fldEmail);
        graph.AddContains(cls, mthSendEmail);
        graph.AddContains(cls, mthFormatEmail);

        // Relazioni Cluster 1
        graph.AddWrites(mthSaveCust, fldCust);
        graph.AddReads(mthLoadCust, fldCust);

        // Relazioni Cluster 2
        graph.AddCalls(mthSendEmail, mthFormatEmail);
        graph.AddReads(mthFormatEmail, fldEmail);

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().HaveCount(1);

        var report = dataset.Items[0];
        report.Lcom4.Should().Be(2, "La classe contiene due cluster disgiunti (Customer ed Email)");
        report.IsRefactoringCandidate.Should().BeTrue();
        report.Clusters.Should().HaveCount(2);
        report.MethodCount.Should().Be(4);
        report.FieldCount.Should().Be(2);
    }

    /// <summary>
    /// Verifica l'analisi di coesione su fixture di progetto reali.
    /// </summary>
    [Fact]
    public void Cohesion_ShouldAnalyzeProjectFixtures_Successfully()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().NotBeEmpty();
        dataset.AverageLcom4.Should().BeGreaterOrEqualTo(0.0);
        dataset.AverageTcc.Should().BeInRange(0.0, 1.0);
    }
}
