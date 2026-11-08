using FluentAssertions;
using Macondo.Semantic.Analytics.CodeClones;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CodeCloneDetectionExtendedTests
{
    /// <summary>
    /// Verifica che routine completamente distinte (lunghezze differenti, chiamate a metodi diversi)
    /// non vengano identificate come duplicati o cloni (0 gruppi di cloni).
    /// </summary>
    [Fact]
    public void CodeClones_ShouldReturnZeroGroups_WhenRoutinesAreDistinct()
    {
        // Arrange
        var graph = new CodeGraph();

        var helperA = new Symbol { Id = Guid.NewGuid(), Name = "LogToDisk", QualifiedName = "HelperA.LogToDisk", FilePath = "HelperA.cs", Kind = SymbolKind.Method };
        var helperB = new Symbol { Id = Guid.NewGuid(), Name = "SendNetworkPacket", QualifiedName = "HelperB.SendNetworkPacket", FilePath = "HelperB.cs", Kind = SymbolKind.Method };

        var mth1 = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "RoutineA",
            QualifiedName = "ModA.RoutineA",
            FilePath = "ModA.cs",
            Kind = SymbolKind.Method,
            LineStart = 1,
            LineEnd = 20
        };

        var mth2 = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "RoutineB",
            QualifiedName = "ModB.RoutineB",
            FilePath = "ModB.cs",
            Kind = SymbolKind.Method,
            LineStart = 50,
            LineEnd = 150
        };

        graph.Symbols.AddRange(new[] { helperA, helperB, mth1, mth2 });
        graph.AddCalls(mth1, helperA);
        graph.AddCalls(mth2, helperB);

        // Act
        var report = CodeCloneDetector.DetectClones(graph, minLineCount: 5);

        // Assert
        report.Should().NotBeNull();
        report.TotalDuplicateGroups.Should().Be(0);
        report.TotalRedundantLines.Should().Be(0);
        report.CloneGroups.Should().BeEmpty();
    }

    /// <summary>
    /// Verifica che routine inferiori alla soglia minima specificata (minLineCount)
    /// vengano escluse dal rilevamento dei cloni anche se identiche.
    /// </summary>
    [Fact]
    public void CodeClones_ShouldIgnoreRoutinesBelowMinLineThreshold()
    {
        // Arrange
        var graph = new CodeGraph();
        var target = new Symbol { Id = Guid.NewGuid(), Name = "DoNothing", QualifiedName = "Mod.DoNothing", FilePath = "Mod.cs", Kind = SymbolKind.Method };

        var tiny1 = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "TinyA",
            QualifiedName = "ModA.TinyA",
            FilePath = "ModA.cs",
            Kind = SymbolKind.Method,
            LineStart = 1,
            LineEnd = 3 // 3 lines
        };

        var tiny2 = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "TinyB",
            QualifiedName = "ModB.TinyB",
            FilePath = "ModB.cs",
            Kind = SymbolKind.Method,
            LineStart = 10,
            LineEnd = 12 // 3 lines
        };

        graph.Symbols.AddRange(new[] { target, tiny1, tiny2 });
        graph.AddCalls(tiny1, target);
        graph.AddCalls(tiny2, target);

        // Act (richiede almeno 6 righe)
        var report = CodeCloneDetector.DetectClones(graph, minLineCount: 6);

        // Assert
        report.TotalDuplicateGroups.Should().Be(0);
    }

    /// <summary>
    /// Verifica che il detector individui molteplici gruppi distinti di duplicazione (es. 2 coppie separate di cloni).
    /// </summary>
    [Fact]
    public void CodeClones_ShouldDetectMultipleIndependentCloneGroups()
    {
        // Arrange
        var graph = new CodeGraph();

        var shared1 = new Symbol { Id = Guid.NewGuid(), Name = "SharedCalc", QualifiedName = "Calc.SharedCalc", FilePath = "Calc.cs", Kind = SymbolKind.Method };
        var shared2 = new Symbol { Id = Guid.NewGuid(), Name = "SharedPrint", QualifiedName = "Print.SharedPrint", FilePath = "Print.cs", Kind = SymbolKind.Method };

        // Group 1: Calc duplicates (20 lines each)
        var calcA = new Symbol { Id = Guid.NewGuid(), Name = "CalcA", QualifiedName = "M1.CalcA", FilePath = "M1.cs", Kind = SymbolKind.Method, LineStart = 1, LineEnd = 20 };
        var calcB = new Symbol { Id = Guid.NewGuid(), Name = "CalcB", QualifiedName = "M2.CalcB", FilePath = "M2.cs", Kind = SymbolKind.Method, LineStart = 1, LineEnd = 20 };

        // Group 2: Print duplicates (35 lines each)
        var printA = new Symbol { Id = Guid.NewGuid(), Name = "PrintA", QualifiedName = "M3.PrintA", FilePath = "M3.cs", Kind = SymbolKind.Method, LineStart = 1, LineEnd = 35 };
        var printB = new Symbol { Id = Guid.NewGuid(), Name = "PrintB", QualifiedName = "M4.PrintB", FilePath = "M4.cs", Kind = SymbolKind.Method, LineStart = 1, LineEnd = 35 };

        graph.Symbols.AddRange(new[] { shared1, shared2, calcA, calcB, printA, printB });
        graph.AddCalls(calcA, shared1);
        graph.AddCalls(calcB, shared1);
        graph.AddCalls(printA, shared2);
        graph.AddCalls(printB, shared2);

        // Act
        var report = CodeCloneDetector.DetectClones(graph, minLineCount: 5);

        // Assert
        report.Should().NotBeNull();
        report.TotalDuplicateGroups.Should().Be(2);
        report.CloneGroups.Should().HaveCount(2);
        report.TotalRedundantLines.Should().BeGreaterThan(0);
    }
}
