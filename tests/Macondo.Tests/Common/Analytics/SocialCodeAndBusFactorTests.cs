using FluentAssertions;
using Macondo.Semantic.Analytics.SocialCode;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class SocialCodeAndBusFactorTests
{
    /// <summary>
    /// Verifica il calcolo del Bus Factor e l'identificazione di codice orfano o ad alto rischio.
    /// </summary>
    [Fact]
    public void SocialCode_ShouldCalculateBusFactorAndDetectOrphanCode()
    {
        // Arrange
        var graph = new CodeGraph();
        var fileSym = new Symbol { Id = Guid.NewGuid(), Name = "CoreEngine.cs", QualifiedName = "CoreEngine.cs", FilePath = "src/CoreEngine.cs", Kind = SymbolKind.Class };
        graph.Symbols.Add(fileSym);

        var history = new List<FileCommitRecord>
        {
            new FileCommitRecord { Author = "Alice", FilePath = "src/CoreEngine.cs", LinesAdded = 200, LinesDeleted = 10, Timestamp = DateTime.UtcNow.AddDays(-10) },
            new FileCommitRecord { Author = "Alice", FilePath = "src/CoreEngine.cs", LinesAdded = 50, LinesDeleted = 5, Timestamp = DateTime.UtcNow.AddDays(-5) },
            new FileCommitRecord { Author = "Bob", FilePath = "src/CoreEngine.cs", LinesAdded = 20, LinesDeleted = 2, Timestamp = DateTime.UtcNow.AddDays(-1) }
        };

        var activeAuthors = new HashSet<string> { "Bob", "Charlie" }; // Alice ha lasciato il team

        // Act
        var dataset = SocialCodeAnalyzer.Analyze(graph, history, activeAuthors);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Files.Should().HaveCount(1);

        var fileReport = dataset.Files[0];
        fileReport.PrimaryAuthor.Should().Be("Alice");
        fileReport.PrimaryOwnership.Should().BeGreaterThan(80.0);
        fileReport.BusFactor.Should().Be(1);
        fileReport.Risk.Should().Be(BusFactorRisk.Orphan, "Alice è l'autrice principale ma non è più attiva");
    }
}
