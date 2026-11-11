using FluentAssertions;
using Macondo.Semantic.Analytics.CodeClones;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CodeCloneDetectionTests
{
    /// <summary>
    /// Verify that two routines with identical lengths and calls are grouped together as code clones.
    /// </summary>
    [Fact]
    public void CodeClones_ShouldDetectExactAndParameterizedClones()
    {
        // Arrange
        var graph = new CodeGraph();

        var sharedHelper = new Symbol { Id = Guid.NewGuid(), Name = "LogMessage", QualifiedName = "Helper.LogMessage", FilePath = "Helper.cs", Kind = SymbolKind.Method };

        var mth1 = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ProcessInvoiceA",
            QualifiedName = "ModuleA.ProcessInvoiceA",
            FilePath = "ModuleA.cs",
            Kind = SymbolKind.Method,
            LineStart = 10,
            LineEnd = 30
        };

        var mth2 = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ProcessInvoiceB",
            QualifiedName = "ModuleB.ProcessInvoiceB",
            FilePath = "ModuleB.cs",
            Kind = SymbolKind.Method,
            LineStart = 100,
            LineEnd = 120
        };

        graph.Symbols.AddRange(new[] { sharedHelper, mth1, mth2 });
        graph.AddCalls(mth1, sharedHelper);
        graph.AddCalls(mth2, sharedHelper);

        // Act
        var report = CodeCloneDetector.DetectClones(graph, minLineCount: 5);

        // Assert
        report.Should().NotBeNull();
        report.TotalDuplicateGroups.Should().BeGreaterOrEqualTo(1);
        report.TotalRedundantLines.Should().BeGreaterThan(0);
        report.CloneGroups[0].Occurrences.Should().HaveCount(2);
    }
}
