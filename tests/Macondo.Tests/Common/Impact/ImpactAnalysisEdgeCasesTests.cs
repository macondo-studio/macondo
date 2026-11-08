using FluentAssertions;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Impact;

public class ImpactAnalysisEdgeCasesTests
{
    /// <summary>
    /// Verifica che in un grafo con dipendenza a diamante (A -> B -> D e A -> C -> D),
    /// il calcolo del raggio di impatto deduplichi correttamente i chiamanti indiretti senza duplicazioni.
    /// </summary>
    [Fact]
    public void DiamondDependency_ShouldDeduplicateAffectedCallers()
    {
        // Arrange
        var graph = new CodeGraph();

        var symA = new Symbol { Id = Guid.NewGuid(), Name = "A", QualifiedName = "Mod.A", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var symB = new Symbol { Id = Guid.NewGuid(), Name = "B", QualifiedName = "Mod.B", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var symC = new Symbol { Id = Guid.NewGuid(), Name = "C", QualifiedName = "Mod.C", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var symD = new Symbol { Id = Guid.NewGuid(), Name = "D", QualifiedName = "Mod.D", FilePath = "Mod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { symA, symB, symC, symD });
        graph.AddCalls(symA, symB);
        graph.AddCalls(symA, symC);
        graph.AddCalls(symB, symD);
        graph.AddCalls(symC, symD);

        var analyzer = new ImpactAnalyzer(graph);

        // Act
        var impact = analyzer.AnalyzeSymbolImpact(symD);

        // Assert
        impact.DirectCallers.Should().HaveCount(2);
        impact.DirectCallers.Select(s => s.Id).Should().Contain(new[] { symB.Id, symC.Id });

        // symA deve comparire esattamente 1 volta negli indirect callers
        impact.IndirectCallers.Should().HaveCount(1);
        impact.IndirectCallers[0].Symbol.Id.Should().Be(symA.Id);

        impact.TotalImpactedSymbolsCount.Should().Be(3);
    }

    /// <summary>
    /// Verifica che routine ricorsive (A -> A) o cicli diretti di chiamata non provochino loop infiniti
    /// o StackOverflowException durante l'attraversamento BFS dei chiamanti.
    /// </summary>
    [Fact]
    public void RecursiveSelfInvocation_ShouldTerminateGracefullyWithoutStackOverflow()
    {
        // Arrange
        var graph = new CodeGraph();

        var recursiveSub = new Symbol { Id = Guid.NewGuid(), Name = "RecursiveFactorial", QualifiedName = "Math.RecursiveFactorial", FilePath = "Math.bas", Kind = SymbolKind.Method };
        var callerSub = new Symbol { Id = Guid.NewGuid(), Name = "Compute", QualifiedName = "App.Compute", FilePath = "App.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { recursiveSub, callerSub });
        graph.AddCalls(recursiveSub, recursiveSub); // Auto-chiamata ricorsiva
        graph.AddCalls(callerSub, recursiveSub);

        var analyzer = new ImpactAnalyzer(graph);

        // Act & Assert
        var act = () => analyzer.AnalyzeSymbolImpact(recursiveSub);
        act.Should().NotThrow();

        var impact = act();
        impact.DirectCallers.Should().Contain(c => c.Id == recursiveSub.Id);
        impact.DirectCallers.Should().Contain(c => c.Id == callerSub.Id);
    }

    /// <summary>
    /// Verifica che in una catena molto profonda di chiamate (15 livelli), il parametro maxDepth
    /// limiti rigorosamente il raggio d'azione dell'analisi di impatto.
    /// </summary>
    [Fact]
    public void DeepCallChain_ShouldRespectMaxDepthConstraint()
    {
        // Arrange: Catena M0 -> M1 -> M2 -> ... -> M14
        var graph = new CodeGraph();
        var symbols = new List<Symbol>();

        for (int i = 0; i < 15; i++)
        {
            var sym = new Symbol { Id = Guid.NewGuid(), Name = $"M_{i}", QualifiedName = $"Mod.M_{i}", FilePath = "Mod.bas", Kind = SymbolKind.Method };
            symbols.Add(sym);
            graph.Symbols.Add(sym);
        }

        for (int i = 0; i < 14; i++)
        {
            graph.AddCalls(symbols[i], symbols[i + 1]);
        }

        var target = symbols[14];
        var analyzer = new ImpactAnalyzer(graph);

        // Act: maxDepth = 4
        var impact = analyzer.AnalyzeSymbolImpact(target, maxDepth: 4);

        // Assert
        impact.DirectCallers.Should().HaveCount(1);
        impact.DirectCallers[0].Id.Should().Be(symbols[13].Id);

        // Con maxDepth = 4, deve esplorare solo M13 (depth 1), M12 (2), M11 (3), M10 (4)
        impact.IndirectCallers.Should().HaveCount(3); // M12, M11, M10
        impact.MaxDependencyDepth.Should().Be(4);
    }

    /// <summary>
    /// Verifica che l'analisi di impatto individui tutte le Form impattate univoche
    /// e formatti AffectedFormsSummary in modo pulito e senza duplicazioni.
    /// </summary>
    [Fact]
    public void MultiFormImpact_ShouldSummarizeUniqueFormsWithoutDuplicates()
    {
        // Arrange
        var graph = new CodeGraph();

        var utilMod = new Symbol { Id = Guid.NewGuid(), Name = "Utils", QualifiedName = "Utils", FilePath = "Utils.bas", Kind = SymbolKind.Module };
        var helperMethod = new Symbol { Id = Guid.NewGuid(), Name = "FormatCurrency", QualifiedName = "Utils.FormatCurrency", FilePath = "Utils.bas", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { utilMod, helperMethod });
        graph.AddContains(utilMod, helperMethod);

        // 3 Form che invocano helperMethod
        var formNames = new[] { "frmOrder", "frmInvoice", "frmMain" };
        foreach (var formName in formNames)
        {
            var form = new Symbol { Id = Guid.NewGuid(), Name = formName, QualifiedName = formName, FilePath = $"{formName}.frm", Kind = SymbolKind.Form };
            var formMethod1 = new Symbol { Id = Guid.NewGuid(), Name = "Btn1_Click", QualifiedName = $"{formName}.Btn1_Click", FilePath = $"{formName}.frm", Kind = SymbolKind.Method };
            var formMethod2 = new Symbol { Id = Guid.NewGuid(), Name = "Btn2_Click", QualifiedName = $"{formName}.Btn2_Click", FilePath = $"{formName}.frm", Kind = SymbolKind.Method };

            graph.Symbols.AddRange(new[] { form, formMethod1, formMethod2 });
            graph.AddContains(form, formMethod1);
            graph.AddContains(form, formMethod2);

            graph.AddCalls(formMethod1, helperMethod);
            graph.AddCalls(formMethod2, helperMethod);
        }

        var analyzer = new ImpactAnalyzer(graph);

        // Act
        var impact = analyzer.AnalyzeSymbolImpact(helperMethod);

        // Assert
        impact.AffectedForms.Should().HaveCount(3);
        impact.AffectedForms.Select(f => f.Name).Should().BeEquivalentTo(formNames);
    }
}
