using FluentAssertions;
using Macondo.Semantic.Analytics.TemporalCoupling;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class TemporalCouplingTests
{
    /// <summary>
    /// Verifica che l'analizzatore di accoppiamento temporale calcoli correttamente il numero di co-modifiche,
    /// i commit totali per file e l'indice di similarità di Jaccard a partire dalla cronologia dei changelist.
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldCalculateCoChangeAndJaccardIndex_FromCommitHistory()
    {
        // Arrange: 4 commit in cui FileA.cls e FileB.cls cambiano sempre insieme
        var changelists = new List<List<string>>
        {
            new() { "src/OrderService.cls", "src/InvoiceService.cls" },
            new() { "src/OrderService.cls", "src/InvoiceService.cls" },
            new() { "src/OrderService.cls", "src/InvoiceService.cls" },
            new() { "src/OrderService.cls", "src/InvoiceService.cls" }
        };

        // Act
        var dataset = TemporalCouplingAnalyzer.Analyze(changelists);

        // Assert
        dataset.Should().NotBeNull();
        dataset.HasGitHistory.Should().BeTrue();
        dataset.TotalCommitsAnalyzed.Should().Be(4);
        dataset.Pairs.Should().HaveCount(1);

        var pair = dataset.Pairs[0];
        pair.CoChangeCount.Should().Be(4);
        pair.TotalCommitsA.Should().Be(4);
        pair.TotalCommitsB.Should().Be(4);
        pair.JaccardIndex.Should().Be(1.0); // 4 / (4 + 4 - 4) = 1.0
        pair.ConfidenceAToB.Should().Be(1.0);
        pair.ConfidenceBToA.Should().Be(1.0);
    }

    /// <summary>
    /// Verifica che le coppie di file con alto indice di co-modifica (Jaccard >= 0.4)
    /// ma prive di qualsiasi dipendenza statica nel CodeGraph vengano segnalate come Hidden Coupling (accoppiamento nascosto).
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldIdentifyHiddenCoupling_WhenCoChangeIsHighWithoutStaticDependency()
    {
        // Arrange
        var changelists = new List<List<string>>
        {
            new() { "src/SecurityPolicy.bas", "src/UserAuditor.cls" },
            new() { "src/SecurityPolicy.bas", "src/UserAuditor.cls" },
            new() { "src/SecurityPolicy.bas", "src/UserAuditor.cls" }
        };

        // CodeGraph in cui i due moduli non hanno alcuna relazione di chiamata o istanziazione tra loro
        var graph = new CodeGraph();
        var secMod = new Symbol { Id = Guid.NewGuid(), Name = "SecurityPolicy", QualifiedName = "SecurityPolicy", FilePath = "src/SecurityPolicy.bas", Kind = SymbolKind.Module };
        var auditClass = new Symbol { Id = Guid.NewGuid(), Name = "UserAuditor", QualifiedName = "UserAuditor", FilePath = "src/UserAuditor.cls", Kind = SymbolKind.Class };
        graph.Symbols.AddRange(new[] { secMod, auditClass });

        // Act
        var dataset = TemporalCouplingAnalyzer.Analyze(changelists, graph);

        // Assert
        dataset.HiddenCouplingCount.Should().Be(1);
        dataset.Pairs.Should().HaveCount(1);
        dataset.Pairs[0].HasStaticDependency.Should().BeFalse();
        dataset.Pairs[0].JaccardIndex.Should().Be(1.0);
    }

    /// <summary>
    /// Verifica che i commit contenenti troppi file (> 35, tipici di merge massivi o refactoring generici)
    /// oppure con estensioni non rilevanti vengano scartati per non inquinare la matrice di correlazione.
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldIgnoreCommitsWithTooManyOrIrrelevantFiles()
    {
        // Arrange
        var massiveCommit = Enumerable.Range(1, 40).Select(i => $"file_{i}.cls").ToList();
        var nonCodeCommit = new List<string> { "doc/README.md", "assets/logo.png", "build/notes.txt" };
        var validCommit1 = new List<string> { "src/A.cls", "src/B.cls" };
        var validCommit2 = new List<string> { "src/A.cls", "src/B.cls" };

        var changelists = new List<List<string>> { massiveCommit, nonCodeCommit, validCommit1, validCommit2 };

        // Act
        var dataset = TemporalCouplingAnalyzer.Analyze(changelists);

        // Assert
        dataset.Pairs.Should().HaveCount(1);
        dataset.Pairs[0].FileAName.Should().Be("A.cls");
        dataset.Pairs[0].FileBName.Should().Be("B.cls");
    }

    /// <summary>
    /// Verifica che in assenza di cronologia dei commit venga restituito un dataset vuoto coerente.
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldReturnEmptyDataset_WhenNoCommitsProvided()
    {
        // Arrange
        var emptyChangelists = new List<List<string>>();

        // Act
        var dataset = TemporalCouplingAnalyzer.Analyze(emptyChangelists);

        // Assert
        dataset.Should().NotBeNull();
        dataset.HasGitHistory.Should().BeFalse();
        dataset.TotalCommitsAnalyzed.Should().Be(0);
        dataset.Pairs.Should().BeEmpty();
    }
}
