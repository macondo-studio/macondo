using FluentAssertions;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Analytics.CodeClones;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class AnalyticsAndGraphExtendedTests
{
    /// <summary>
    /// Verify that DeadCodeAnalyzer locates an island of mutually recursive private methods
    /// (A calls B and B calls A), which are not reachable from any entry point or public API.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldIdentifyMutuallyRecursiveDeadIsland()
    {
        // Arrange
        var graph = new CodeGraph();

        var mod = new Symbol { Id = Guid.NewGuid(), Name = "WorkerMod", QualifiedName = "WorkerMod", FilePath = "WorkerMod.bas", Kind = SymbolKind.Module };
        var subMain = new Symbol { Id = Guid.NewGuid(), Name = "Main", QualifiedName = "WorkerMod.Main", FilePath = "WorkerMod.bas", Kind = SymbolKind.Method };

        var pingSub = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "DeadPing",
            QualifiedName = "WorkerMod.DeadPing",
            FilePath = "WorkerMod.bas",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Private,
            LineStart = 30,
            LineEnd = 40
        };

        var pongSub = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "DeadPong",
            QualifiedName = "WorkerMod.DeadPong",
            FilePath = "WorkerMod.bas",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Private,
            LineStart = 42,
            LineEnd = 52
        };

        graph.Symbols.AddRange(new[] { mod, subMain, pingSub, pongSub });
        graph.AddContains(mod, subMain);
        graph.AddContains(mod, pingSub);
        graph.AddContains(mod, pongSub);

        // Ping and Pong call each other but are not called by Main
        graph.AddCalls(pingSub, pongSub);
        graph.AddCalls(pongSub, pingSub);

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        dataset.Items.Should().Contain(i => i.Id == pingSub.Id && i.Category == DeadCodeCategory.UnreachableRoutine);
        dataset.Items.Should().Contain(i => i.Id == pongSub.Id && i.Category == DeadCodeCategory.UnreachableRoutine);
    }

    /// <summary>
    /// Verify that within a VB6 Form, control event handlers are preserved as alive,
    /// while never invoked private helper routines are correctly reported as dead code.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldPreserveFormEventHandlers_WhileDetectingUnusedPrivateHelpers()
    {
        // Arrange
        var graph = new CodeGraph();

        var form = new Symbol { Id = Guid.NewGuid(), Name = "frmEditor", QualifiedName = "frmEditor", FilePath = "frmEditor.frm", Kind = SymbolKind.Form };
        var btnSave = new Symbol { Id = Guid.NewGuid(), Name = "cmdSave", QualifiedName = "frmEditor.cmdSave", FilePath = "frmEditor.frm", Kind = SymbolKind.Field };
        var saveClick = new Symbol { Id = Guid.NewGuid(), Name = "cmdSave_Click", QualifiedName = "frmEditor.cmdSave_Click", FilePath = "frmEditor.frm", Kind = SymbolKind.Method };
        var unusedHelper = new Symbol { Id = Guid.NewGuid(), Name = "ObsoleteFormatting", QualifiedName = "frmEditor.ObsoleteFormatting", FilePath = "frmEditor.frm", Kind = SymbolKind.Method, Accessibility = Accessibility.Private };

        graph.Symbols.AddRange(new[] { form, btnSave, saveClick, unusedHelper });
        graph.AddContains(form, btnSave);
        graph.AddContains(form, saveClick);
        graph.AddContains(form, unusedHelper);

        graph.AddHandlesEvent(btnSave, saveClick);

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        // Assert: cmdSave_Click is alive because it handles the control's event, ObsoleteFormatting is dead code
        dataset.Items.Should().NotContain(i => i.Id == saveClick.Id);
        dataset.Items.Should().Contain(i => i.Id == unusedHelper.Id && i.Category == DeadCodeCategory.UnreachableRoutine);
    }

    /// <summary>
    /// Verify that ApiDiffAnalyzer identifies the reduction in accessibility from Public to Private as a Breaking Change.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenAccessibilityIsDemotedFromPublicToPrivate()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var methodOld = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ProcessTransaction",
            QualifiedName = "PaymentEngine.ProcessTransaction",
            FilePath = "PaymentEngine.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        oldGraph.Symbols.Add(methodOld);

        var methodNew = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ProcessTransaction",
            QualifiedName = "PaymentEngine.ProcessTransaction",
            FilePath = "PaymentEngine.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Private
        };
        newGraph.Symbols.Add(methodNew);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().BeGreaterThanOrEqualTo(1);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
    }

    /// <summary>
    /// Verify that CallGraphQueries detects self-invoking direct loops (Self-Loop: A -> A) and complex loops (A -> B -> C -> A).
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldDetectSelfLoopsAndMultiNodeCycles()
    {
        // Arrange
        var graph = new CodeGraph();

        var nodeA = new Symbol { Id = Guid.NewGuid(), Name = "LoopA", QualifiedName = "Mod.LoopA", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var nodeB = new Symbol { Id = Guid.NewGuid(), Name = "LoopB", QualifiedName = "Mod.LoopB", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var nodeC = new Symbol { Id = Guid.NewGuid(), Name = "LoopC", QualifiedName = "Mod.LoopC", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var selfLoopNode = new Symbol { Id = Guid.NewGuid(), Name = "SelfRecursive", QualifiedName = "Mod.SelfRecursive", FilePath = "Mod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { nodeA, nodeB, nodeC, selfLoopNode });

        // Cycle 1: A -> B -> C -> A
        graph.AddCalls(nodeA, nodeB);
        graph.AddCalls(nodeB, nodeC);
        graph.AddCalls(nodeC, nodeA);

        // Cycle 2: SelfRecursive -> SelfRecursive
        graph.AddCalls(selfLoopNode, selfLoopNode);

        var queries = new CallGraphQueries(graph);

        // Act
        var cycles = queries.DetectCycles();

        // Assert
        cycles.Should().NotBeNull();
        cycles.Should().NotBeEmpty();
        cycles.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    /// <summary>
    /// Verify that FindExecutionPaths handles disconnected graphs or non-existent paths without errors,
    /// returning an empty collection.
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldReturnEmpty_ForDisconnectedSubgraphs()
    {
        // Arrange: Two disconnected graphs (A -> B) and (C -> D)
        var graph = new CodeGraph();

        var nodeA = new Symbol { Id = Guid.NewGuid(), Name = "A", QualifiedName = "Mod.A", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var nodeB = new Symbol { Id = Guid.NewGuid(), Name = "B", QualifiedName = "Mod.B", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var nodeC = new Symbol { Id = Guid.NewGuid(), Name = "C", QualifiedName = "Mod.C", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var nodeD = new Symbol { Id = Guid.NewGuid(), Name = "D", QualifiedName = "Mod.D", FilePath = "Mod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { nodeA, nodeB, nodeC, nodeD });
        graph.AddCalls(nodeA, nodeB);
        graph.AddCalls(nodeC, nodeD);

        var queries = new CallGraphQueries(graph);

        // Act
        var paths = queries.FindExecutionPaths(nodeA, nodeD);

        // Assert
        paths.Should().BeEmpty("There is no path between disconnected subgraphs A and D");
    }

    /// <summary>
    /// Verify that CodeCloneDetector correctly filters trivial routines below the minimum lines threshold (minLineCount)
    /// and group identical clones distributed across 3 or more different modules.
    /// </summary>
    [Fact]
    public void CodeClones_ShouldGroupMultipleOccurrencesAcrossModules_AndRespectMinLineCount()
    {
        // Arrange
        var graph = new CodeGraph();

        var targetHelper = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteQuery", QualifiedName = "Db.ExecuteQuery", FilePath = "Db.cs", Kind = SymbolKind.Method };
        graph.Symbols.Add(targetHelper);

        // 3 duplicate methods with 20 lines
        var cloneMethods = new List<Symbol>();
        for (int i = 1; i <= 3; i++)
        {
            var mth = new Symbol
            {
                Id = Guid.NewGuid(),
                Name = $"DuplicateProcessor{i}",
                QualifiedName = $"Module{i}.DuplicateProcessor{i}",
                FilePath = $"Module{i}.cs",
                Kind = SymbolKind.Method,
                LineStart = 10,
                LineEnd = 30
            };
            cloneMethods.Add(mth);
            graph.Symbols.Add(mth);
            graph.AddCalls(mth, targetHelper);
        }

        // Trivial 2 line method
        var tinyMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "TinyHelper",
            QualifiedName = "ModuleTiny.TinyHelper",
            FilePath = "ModuleTiny.cs",
            Kind = SymbolKind.Method,
            LineStart = 1,
            LineEnd = 2
        };
        graph.Symbols.Add(tinyMethod);
        graph.AddCalls(tinyMethod, targetHelper);

        // Act
        var report = CodeCloneDetector.DetectClones(graph, minLineCount: 10);

        // Assert
        report.Should().NotBeNull();
        report.TotalDuplicateGroups.Should().BeGreaterOrEqualTo(1);

        var group = report.CloneGroups.FirstOrDefault(g => g.Occurrences.Count == 3);
        group.Should().NotBeNull();
        group!.Occurrences.Select(o => o.Symbol.Id).Should().Contain(cloneMethods.Select(m => m.Id));
    }
}
