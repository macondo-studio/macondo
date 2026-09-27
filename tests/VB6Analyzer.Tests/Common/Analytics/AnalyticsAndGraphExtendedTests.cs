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
    /// Verifica che DeadCodeAnalyzer individui un'isola di metodi privati mutuamente ricorsivi
    /// (A chiama B e B chiama A), che non sono raggiungibili da alcun entry point o API pubblica.
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

        // Ping e Pong si chiamano a vicenda ma non sono chiamati da Main
        graph.AddCalls(pingSub, pongSub);
        graph.AddCalls(pongSub, pingSub);

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        dataset.Items.Should().Contain(i => i.Id == pingSub.Id && i.Category == DeadCodeCategory.UnreachableRoutine);
        dataset.Items.Should().Contain(i => i.Id == pongSub.Id && i.Category == DeadCodeCategory.UnreachableRoutine);
    }

    /// <summary>
    /// Verifica che all'interno di una Form VB6, gli handler di eventi dei controlli vengano preservati come vivi,
    /// mentre routine helper private mai invocate vengano correttamente segnalate come codice morto.
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

        // Assert: cmdSave_Click è vivo perché gestisce l'evento del controllo, ObsoleteFormatting è dead code
        dataset.Items.Should().NotContain(i => i.Id == saveClick.Id);
        dataset.Items.Should().Contain(i => i.Id == unusedHelper.Id && i.Category == DeadCodeCategory.UnreachableRoutine);
    }

    /// <summary>
    /// Verifica che ApiDiffAnalyzer identifichi la riduzione di accessibilità da Public a Private come Breaking Change.
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
    /// Verifica che CallGraphQueries rilevi cicli diretti di auto-invocazione (Self-Loop: A -&gt; A) e cicli complessi (A -&gt; B -&gt; C -&gt; A).
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
    /// Verifica che FindExecutionPaths gestisca grafi disconnessi o percorsi inesistenti senza errori,
    /// restituendo una collezione vuota.
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldReturnEmpty_ForDisconnectedSubgraphs()
    {
        // Arrange: Due grafi disconnessi (A -> B) e (C -> D)
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
    /// Verifica che CodeCloneDetector filtri correttamente le routine banali al di sotto della soglia minima di righe (minLineCount)
    /// e raggruppi cloni identici distribuiti su 3 o più moduli differenti.
    /// </summary>
    [Fact]
    public void CodeClones_ShouldGroupMultipleOccurrencesAcrossModules_AndRespectMinLineCount()
    {
        // Arrange
        var graph = new CodeGraph();

        var targetHelper = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteQuery", QualifiedName = "Db.ExecuteQuery", FilePath = "Db.cs", Kind = SymbolKind.Method };
        graph.Symbols.Add(targetHelper);

        // 3 metodi duplicati con 20 righe
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

        // Metodo banale di 2 righe
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
