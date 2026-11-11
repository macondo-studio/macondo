using FluentAssertions;
using Macondo.Semantic.Analytics.TemporalCoupling;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class TemporalCouplingExtendedTests
{
    /// <summary>
    /// Check the asymmetric calculation of confidence values (Confidence A -> B vs Confidence B -> A)
    /// when two files have a different total number of commits.
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldCalculateAsymmetricConfidence()
    {
        // Arrange: FileA modified in 10 commits, FileB only in 2 commits, and have 2 co-modifications together.
        // Confidence(A -> B) = 2 / 10 = 0.2 (20%)
        // Confidence(B -> A) = 2 / 2 = 1.0 (100%)
        var changelists = new List<List<string>>();

        // 2 commit congiunti
        changelists.Add(new List<string> { "src/FileA.cls", "src/FileB.cls" });
        changelists.Add(new List<string> { "src/FileA.cls", "src/FileB.cls" });

        // 8 commits only for FileA with FileC
        for (int i = 0; i < 8; i++)
        {
            changelists.Add(new List<string> { "src/FileA.cls", "src/FileC.cls" });
        }

        // Act
        var dataset = TemporalCouplingAnalyzer.Analyze(changelists);

        // Assert
        dataset.Should().NotBeNull();
        var pairAB = dataset.Pairs.FirstOrDefault(p =>
            (p.FileAName.Contains("FileA") && p.FileBName.Contains("FileB")) ||
            (p.FileAName.Contains("FileB") && p.FileBName.Contains("FileA")));

        pairAB.Should().NotBeNull();
        pairAB!.CoChangeCount.Should().Be(2);

        if (pairAB.FileAName.Contains("FileA"))
        {
            pairAB.ConfidenceAToB.Should().BeApproximately(0.2, 0.01);
            pairAB.ConfidenceBToA.Should().BeApproximately(1.0, 0.01);
        }
        else
        {
            pairAB.ConfidenceAToB.Should().BeApproximately(1.0, 0.01);
            pairAB.ConfidenceBToA.Should().BeApproximately(0.2, 0.01);
        }
    }

    /// <summary>
    /// Ensures that commits containing a single file are ignored when forming co-modification pairs.
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldIgnoreSingleFileCommits()
    {
        // Arrange
        var changelists = new List<List<string>>
        {
            new() { "src/SingleFile1.cls" },
            new() { "src/SingleFile2.cls" },
            new() { "src/SingleFile3.cls" }
        };

        // Act
        var dataset = TemporalCouplingAnalyzer.Analyze(changelists);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Pairs.Should().BeEmpty();
    }

    /// <summary>
    /// Verify that when there is an explicit static dependency in the CodeGraph between two co-modified files,
    /// the pair has HasStaticDependency = true and is not counted as Hidden Coupling.
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldRecognizeStaticDependency_AndNotFlagAsHiddenCoupling()
    {
        // Arrange
        var changelists = new List<List<string>>
        {
            new() { "src/OrderService.cls", "src/InvoiceService.cls" },
            new() { "src/OrderService.cls", "src/InvoiceService.cls" }
        };

        var graph = new CodeGraph();
        var orderMod = new Symbol { Id = Guid.NewGuid(), Name = "OrderService", QualifiedName = "OrderService", FilePath = "src/OrderService.cls", Kind = SymbolKind.Class };
        var orderMethod = new Symbol { Id = Guid.NewGuid(), Name = "CreateOrder", QualifiedName = "OrderService.CreateOrder", FilePath = "src/OrderService.cls", Kind = SymbolKind.Method };
        var invMod = new Symbol { Id = Guid.NewGuid(), Name = "InvoiceService", QualifiedName = "InvoiceService", FilePath = "src/InvoiceService.cls", Kind = SymbolKind.Class };
        var invMethod = new Symbol { Id = Guid.NewGuid(), Name = "CreateInvoice", QualifiedName = "InvoiceService.CreateInvoice", FilePath = "src/InvoiceService.cls", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { orderMod, orderMethod, invMod, invMethod });
        graph.AddContains(orderMod, orderMethod);
        graph.AddContains(invMod, invMethod);

        // Dipendenza statica esplicita: OrderService -> InvoiceService
        graph.AddCalls(orderMethod, invMethod);

        // Act
        var dataset = TemporalCouplingAnalyzer.Analyze(changelists, graph);

        // Assert
        dataset.Pairs.Should().HaveCount(1);
        dataset.Pairs[0].HasStaticDependency.Should().BeTrue();
        dataset.HiddenCouplingCount.Should().Be(0, "Poiché esiste una dipendenza statica esplicita, non è un accoppiamento nascosto");
    }
}
