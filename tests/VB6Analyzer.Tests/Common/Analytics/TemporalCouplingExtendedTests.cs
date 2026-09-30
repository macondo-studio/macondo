using FluentAssertions;
using Macondo.Semantic.Analytics.TemporalCoupling;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class TemporalCouplingExtendedTests
{
    /// <summary>
    /// Verifica il calcolo asimmetrico dei valori di confidenza (Confidence A -> B vs Confidence B -> A)
    /// quando due file hanno un numero totale di commit differente.
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldCalculateAsymmetricConfidence()
    {
        // Arrange: FileA modificato in 10 commit, FileB solo in 2 commit, e hanno 2 co-modifiche insieme.
        // Confidence(A -> B) = 2 / 10 = 0.2 (20%)
        // Confidence(B -> A) = 2 / 2 = 1.0 (100%)
        var changelists = new List<List<string>>();

        // 2 commit congiunti
        changelists.Add(new List<string> { "src/FileA.cls", "src/FileB.cls" });
        changelists.Add(new List<string> { "src/FileA.cls", "src/FileB.cls" });

        // 8 commit solo per FileA con FileC
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
    /// Verifica che i commit contenenti un singolo file vengano ignorati nella formazione di coppie di co-modifica.
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
    /// Verifica che quando esiste una dipendenza statica esplicita nel CodeGraph tra due file co-modificati,
    /// la coppia abbia HasStaticDependency = true e non venga conteggiata come Hidden Coupling.
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
