using FluentAssertions;
using Macondo.Semantic.Analytics.SocialCode;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class SocialCodeAndBusFactorExtendedTests
{
    /// <summary>
    /// Verifica che un modulo con commit equamente distribuiti tra più autori attivi
    /// abbia un Bus Factor elevato (>= 2) e non sia considerato orfano.
    /// </summary>
    [Fact]
    public void SocialCode_ShouldCalculateHealthyBusFactor_WhenMultipleAuthorsAreActive()
    {
        // Arrange
        var graph = new CodeGraph();
        var fileSym = new Symbol { Id = Guid.NewGuid(), Name = "CoreService.cs", QualifiedName = "CoreService.cs", FilePath = "src/CoreService.cs", Kind = SymbolKind.Class };
        graph.Symbols.Add(fileSym);

        var history = new List<FileCommitRecord>
        {
            new() { Author = "Alice", FilePath = "src/CoreService.cs", LinesAdded = 100, LinesDeleted = 10, Timestamp = DateTime.UtcNow.AddDays(-30) },
            new() { Author = "Bob", FilePath = "src/CoreService.cs", LinesAdded = 90, LinesDeleted = 5, Timestamp = DateTime.UtcNow.AddDays(-20) },
            new() { Author = "Charlie", FilePath = "src/CoreService.cs", LinesAdded = 85, LinesDeleted = 8, Timestamp = DateTime.UtcNow.AddDays(-5) }
        };

        var activeAuthors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Alice", "Bob", "Charlie" };

        // Act
        var dataset = SocialCodeAnalyzer.Analyze(graph, history, activeAuthors);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Files.Should().HaveCount(1);

        var fileReport = dataset.Files[0];
        fileReport.BusFactor.Should().BeGreaterThanOrEqualTo(2);
        fileReport.Risk.Should().NotBe(BusFactorRisk.Orphan);
    }

    /// <summary>
    /// Verifica che un modulo gestito interamente da un unico autore attivo
    /// abbia Bus Factor pari a 1 e 100% di ownership primaria.
    /// </summary>
    [Fact]
    public void SocialCode_ShouldDetectSingleMaintainer_WhenOneAuthorOwnsEntireFile()
    {
        // Arrange
        var graph = new CodeGraph();
        var fileSym = new Symbol { Id = Guid.NewGuid(), Name = "PaymentEngine.cs", QualifiedName = "PaymentEngine.cs", FilePath = "src/PaymentEngine.cs", Kind = SymbolKind.Class };
        graph.Symbols.Add(fileSym);

        var history = new List<FileCommitRecord>
        {
            new() { Author = "Alice", FilePath = "src/PaymentEngine.cs", LinesAdded = 300, LinesDeleted = 20, Timestamp = DateTime.UtcNow.AddDays(-15) },
            new() { Author = "Alice", FilePath = "src/PaymentEngine.cs", LinesAdded = 150, LinesDeleted = 10, Timestamp = DateTime.UtcNow.AddDays(-2) }
        };

        var activeAuthors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Alice", "Bob" };

        // Act
        var dataset = SocialCodeAnalyzer.Analyze(graph, history, activeAuthors);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Files.Should().HaveCount(1);

        var fileReport = dataset.Files[0];
        fileReport.PrimaryAuthor.Should().Be("Alice");
        fileReport.PrimaryOwnership.Should().Be(100.0);
        fileReport.BusFactor.Should().Be(1);
        fileReport.Risk.Should().NotBe(BusFactorRisk.Orphan);
    }

    /// <summary>
    /// Verifica che l'analizzatore gestisca simultaneamente più file con diversi profili di rischio
    /// (uno orfano perché l'autore ha lasciato il team, uno con sviluppatore attivo).
    /// </summary>
    [Fact]
    public void SocialCode_ShouldClassifyMultipleFilesWithDistinctRiskProfiles()
    {
        // Arrange
        var graph = new CodeGraph();
        var orphanFile = new Symbol { Id = Guid.NewGuid(), Name = "LegacyMod.bas", QualifiedName = "LegacyMod.bas", FilePath = "src/LegacyMod.bas", Kind = SymbolKind.Module };
        var singleFile = new Symbol { Id = Guid.NewGuid(), Name = "BillingMod.bas", QualifiedName = "BillingMod.bas", FilePath = "src/BillingMod.bas", Kind = SymbolKind.Module };
        var sharedFile = new Symbol { Id = Guid.NewGuid(), Name = "CommonMod.bas", QualifiedName = "CommonMod.bas", FilePath = "src/CommonMod.bas", Kind = SymbolKind.Module };
        graph.Symbols.AddRange(new[] { orphanFile, singleFile, sharedFile });

        var history = new List<FileCommitRecord>
        {
            // LegacyMod: modificato da Dave che ha lasciato il team
            new() { Author = "Dave", FilePath = "src/LegacyMod.bas", LinesAdded = 500, LinesDeleted = 0, Timestamp = DateTime.UtcNow.AddDays(-100) },

            // BillingMod: modificato al 100% da Alice (attiva)
            new() { Author = "Alice", FilePath = "src/BillingMod.bas", LinesAdded = 250, LinesDeleted = 5, Timestamp = DateTime.UtcNow.AddDays(-10) },

            // CommonMod: modificato equamente da Alice e Bob (entrambi attivi)
            new() { Author = "Alice", FilePath = "src/CommonMod.bas", LinesAdded = 100, LinesDeleted = 5, Timestamp = DateTime.UtcNow.AddDays(-20) },
            new() { Author = "Bob", FilePath = "src/CommonMod.bas", LinesAdded = 100, LinesDeleted = 5, Timestamp = DateTime.UtcNow.AddDays(-5) }
        };

        var activeAuthors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Alice", "Bob" };

        // Act
        var dataset = SocialCodeAnalyzer.Analyze(graph, history, activeAuthors);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Files.Should().HaveCount(3);

        var rOrphan = dataset.Files.First(f => f.FilePath == "src/LegacyMod.bas");
        var rSingle = dataset.Files.First(f => f.FilePath == "src/BillingMod.bas");
        var rShared = dataset.Files.First(f => f.FilePath == "src/CommonMod.bas");

        rOrphan.Risk.Should().Be(BusFactorRisk.Orphan);
        rSingle.Risk.Should().NotBe(BusFactorRisk.Orphan);
        rShared.Risk.Should().NotBe(BusFactorRisk.Orphan);
        rShared.BusFactor.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Verifica che in assenza di cronologia dei commit o con lista vuota,
    /// SocialCodeAnalyzer restituisca un dataset vuoto senza generare eccezioni.
    /// </summary>
    [Fact]
    public void SocialCode_ShouldHandleEmptyHistoryAndEmptyGraph_Safely()
    {
        // Arrange
        var emptyGraph = new CodeGraph();
        var emptyHistory = new List<FileCommitRecord>();
        var activeAuthors = new HashSet<string> { "Alice" };

        // Act
        var dataset = SocialCodeAnalyzer.Analyze(emptyGraph, emptyHistory, activeAuthors);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Files.Should().BeEmpty();
    }
}
