using FluentAssertions;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Impact;

public class ImpactAnalysisDeepScenariosTests
{
    /// <summary>
    /// Verifica che in presenza di percorsi multipli sia diretti che indiretti verso lo stesso target
    /// (es. A chiama Target sia direttamente che tramite B -> Target),
    /// il chiamante diretto A venga classificato in DirectCallers e non venga duplicato negli IndirectCallers.
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldNotDuplicateDirectCallerInIndirectCallers_WhenMultiplePathsExist()
    {
        // Arrange
        // A -> Target (Direct)
        // A -> B -> Target (Indirect path)
        var graph = new CodeGraph();

        var symA = new Symbol { Id = Guid.NewGuid(), Name = "CallerA", QualifiedName = "Mod.CallerA", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var symB = new Symbol { Id = Guid.NewGuid(), Name = "CallerB", QualifiedName = "Mod.CallerB", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var target = new Symbol { Id = Guid.NewGuid(), Name = "CoreTarget", QualifiedName = "Mod.CoreTarget", FilePath = "Mod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { symA, symB, target });

        graph.AddCalls(symA, target);
        graph.AddCalls(symA, symB);
        graph.AddCalls(symB, target);

        var analyzer = new ImpactAnalyzer(graph);

        // Act
        var impact = analyzer.AnalyzeSymbolImpact(target);

        // Assert
        impact.DirectCallers.Should().HaveCount(2);
        impact.DirectCallers.Select(s => s.Id).Should().Contain(new[] { symA.Id, symB.Id });

        // symA non deve comparire negli indirect callers perché è già un direct caller
        impact.IndirectCallers.Should().BeEmpty();
        impact.TotalImpactedSymbolsCount.Should().Be(2);
    }

    /// <summary>
    /// Verifica che l'analisi di impatto gestisca un ciclo di chiamate tra i chiamanti a monte (A &lt;-&gt; B)
    /// che invocano il target senza entrare in loop infinito e deduplicando i risultati.
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldHandleCyclicCallerGraph_WithoutInfiniteLoop()
    {
        // Arrange
        // A <-> B (Cycle), and B -> Target
        var graph = new CodeGraph();

        var symA = new Symbol { Id = Guid.NewGuid(), Name = "CycleA", QualifiedName = "Mod.CycleA", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var symB = new Symbol { Id = Guid.NewGuid(), Name = "CycleB", QualifiedName = "Mod.CycleB", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var target = new Symbol { Id = Guid.NewGuid(), Name = "TargetProc", QualifiedName = "Mod.TargetProc", FilePath = "Mod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { symA, symB, target });

        graph.AddCalls(symA, symB);
        graph.AddCalls(symB, symA);
        graph.AddCalls(symB, target);

        var analyzer = new ImpactAnalyzer(graph);

        // Act
        var act = () => analyzer.AnalyzeSymbolImpact(target);

        // Assert
        act.Should().NotThrow();
        var impact = act();

        impact.DirectCallers.Should().ContainSingle(c => c.Id == symB.Id);
        impact.IndirectCallers.Should().ContainSingle(c => c.Symbol.Id == symA.Id);
        impact.TotalImpactedSymbolsCount.Should().Be(2);
    }

    /// <summary>
    /// Verifica che una routine di utilità invocata diffusamente da molti moduli (elevato Fan-In &gt;= 10)
    /// riceva una classificazione di rischio elevata (High o Critical).
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldAssignHighOrCriticalRisk_ForWidespreadUtilityRoutines()
    {
        // Arrange
        var graph = new CodeGraph();
        var targetHelper = new Symbol { Id = Guid.NewGuid(), Name = "FormatString", QualifiedName = "Utils.FormatString", FilePath = "Utils.bas", Kind = SymbolKind.Method };
        graph.Symbols.Add(targetHelper);

        // 12 moduli differenti che chiamano targetHelper
        for (int i = 1; i <= 12; i++)
        {
            var callerMod = new Symbol { Id = Guid.NewGuid(), Name = $"Mod_{i}", QualifiedName = $"Mod_{i}", FilePath = $"Mod_{i}.bas", Kind = SymbolKind.Module };
            var callerMethod = new Symbol { Id = Guid.NewGuid(), Name = $"Worker_{i}", QualifiedName = $"Mod_{i}.Worker_{i}", FilePath = $"Mod_{i}.bas", Kind = SymbolKind.Method };

            graph.Symbols.AddRange(new[] { callerMod, callerMethod });
            graph.AddContains(callerMod, callerMethod);
            graph.AddCalls(callerMethod, targetHelper);
        }

        var analyzer = new ImpactAnalyzer(graph);

        // Act
        var impact = analyzer.AnalyzeSymbolImpact(targetHelper);

        // Assert
        impact.DirectCallers.Should().HaveCount(12);
        impact.AffectedModules.Should().HaveCount(12);
        impact.RiskLevel.Should().Match(r => r == ImpactRiskLevel.High || r == ImpactRiskLevel.Critical);
    }

    /// <summary>
    /// Verifica che l'analisi di impatto tracci le Form impattate anche attraverso catene indirette multilivello
    /// (es. Form -> Service -> Repository -> TargetHelper).
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldPropagateAffectedForms_ThroughDeepIndirectChains()
    {
        // Arrange
        var graph = new CodeGraph();

        var form = new Symbol { Id = Guid.NewGuid(), Name = "frmCheckout", QualifiedName = "frmCheckout", FilePath = "frmCheckout.frm", Kind = SymbolKind.Form };
        var formBtnClick = new Symbol { Id = Guid.NewGuid(), Name = "btnPay_Click", QualifiedName = "frmCheckout.btnPay_Click", FilePath = "frmCheckout.frm", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { form, formBtnClick });
        graph.AddContains(form, formBtnClick);

        var serviceMethod = new Symbol { Id = Guid.NewGuid(), Name = "ProcessPayment", QualifiedName = "PaymentService.ProcessPayment", FilePath = "PaymentService.cls", Kind = SymbolKind.Method };
        var repoMethod = new Symbol { Id = Guid.NewGuid(), Name = "InsertRecord", QualifiedName = "PaymentRepo.InsertRecord", FilePath = "PaymentRepo.cls", Kind = SymbolKind.Method };
        var targetHelper = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteRawSql", QualifiedName = "DbDriver.ExecuteRawSql", FilePath = "DbDriver.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { serviceMethod, repoMethod, targetHelper });

        // Catena: formBtnClick -> serviceMethod -> repoMethod -> targetHelper
        graph.AddCalls(formBtnClick, serviceMethod);
        graph.AddCalls(serviceMethod, repoMethod);
        graph.AddCalls(repoMethod, targetHelper);

        var analyzer = new ImpactAnalyzer(graph);

        // Act
        var impact = analyzer.AnalyzeSymbolImpact(targetHelper);

        // Assert
        impact.AffectedForms.Should().ContainSingle(f => f.Id == form.Id);
        impact.AffectedForms[0].Name.Should().Be("frmCheckout");
        impact.MaxDependencyDepth.Should().Be(3);
    }

    /// <summary>
    /// Verifica che l'analisi di impatto eseguita su un'interfaccia o su una classe astratta
    /// individui le classi derivate e gli implementatori come dipendenti di tipo (TypeDependents).
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldIdentifyImplementersAndConsumers_ForInterfaceSymbols()
    {
        // Arrange
        var graph = new CodeGraph();

        var iface = new Symbol { Id = Guid.NewGuid(), Name = "IRepository", QualifiedName = "IRepository", FilePath = "IRepository.cs", Kind = SymbolKind.Interface };
        var implClassA = new Symbol { Id = Guid.NewGuid(), Name = "SqlRepository", QualifiedName = "SqlRepository", FilePath = "SqlRepository.cs", Kind = SymbolKind.Class };
        var implClassB = new Symbol { Id = Guid.NewGuid(), Name = "MongoRepository", QualifiedName = "MongoRepository", FilePath = "MongoRepository.cs", Kind = SymbolKind.Class };

        var consumerClass = new Symbol { Id = Guid.NewGuid(), Name = "OrderService", QualifiedName = "OrderService", FilePath = "OrderService.cs", Kind = SymbolKind.Class };

        graph.Symbols.AddRange(new[] { iface, implClassA, implClassB, consumerClass });

        graph.AddImplements(implClassA, iface);
        graph.AddImplements(implClassB, iface);
        graph.AddUsesType(consumerClass, iface);

        var analyzer = new ImpactAnalyzer(graph);

        // Act
        var impact = analyzer.AnalyzeSymbolImpact(iface);

        // Assert
        impact.Should().NotBeNull();
        impact.TypeDependents.Should().Contain(s => s.Id == implClassA.Id);
        impact.TypeDependents.Should().Contain(s => s.Id == implClassB.Id);
        impact.TypeDependents.Should().Contain(s => s.Id == consumerClass.Id);
    }

    /// <summary>
    /// Verifica che relazioni penzolanti (dangling relationships verso ID non presenti nei simboli)
    /// non provochino NullReferenceException durante la scansione BFS dei chiamanti.
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldBeResilientToDanglingRelationships()
    {
        // Arrange
        var graph = new CodeGraph();
        var target = new Symbol { Id = Guid.NewGuid(), Name = "TargetSub", QualifiedName = "Mod.TargetSub", FilePath = "Mod.bas", Kind = SymbolKind.Method };
        var validCaller = new Symbol { Id = Guid.NewGuid(), Name = "ValidCaller", QualifiedName = "Mod.ValidCaller", FilePath = "Mod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { target, validCaller });
        graph.AddCalls(validCaller, target);

        // Arco verso un target o sorgente inesistente
        graph.Relationships.Add(new Relationship
        {
            SourceId = Guid.NewGuid(), // Non esiste nei Symbols
            TargetId = target.Id,
            Kind = RelationshipKind.Calls
        });

        var analyzer = new ImpactAnalyzer(graph);

        // Act & Assert
        var act = () => analyzer.AnalyzeSymbolImpact(target);
        act.Should().NotThrow("Dangling relationships with missing symbol IDs must be safely ignored");

        var impact = act();
        impact.DirectCallers.Should().ContainSingle(c => c.Id == validCaller.Id);
    }
}
