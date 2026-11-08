using FluentAssertions;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Analytics.Hotspots;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Analytics.SocialCode;
using Macondo.Semantic.Analytics.TemporalCoupling;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CommonAnalyticsExhaustiveEdgeCasesTests
{
    #region Hotspots with Zero-Churn and High-Churn Disparities

    /// <summary>
    /// Verifica che HotspotAnalyzer gestisca grafi con disparità estrema di churn
    /// (alcuni moduli con 0 churn e altri con centinaia di modifiche VCS).
    /// </summary>
    [Fact]
    public void HotspotAnalyzer_ShouldHandleExtremeChurnDisparity()
    {
        // Arrange
        var graph = new CodeGraph();
        var coldMod = new Symbol { Id = Guid.NewGuid(), Name = "ColdHelper", QualifiedName = "ColdHelper", FilePath = "ColdHelper.cs", Kind = SymbolKind.Method, LineStart = 1, LineEnd = 20 };
        var hotMod = new Symbol { Id = Guid.NewGuid(), Name = "HotDispatcher", QualifiedName = "HotDispatcher", FilePath = "HotDispatcher.cs", Kind = SymbolKind.Method, LineStart = 1, LineEnd = 200 };
        graph.Symbols.AddRange(new[] { coldMod, hotMod });

        var churnHistory = new Dictionary<string, int>
        {
            ["ColdHelper.cs"] = 0,
            ["HotDispatcher.cs"] = 500
        };

        // Act
        var hotspots = HotspotAnalyzer.Analyze(graph, churnHistory);

        // Assert
        hotspots.Should().NotBeNull();
        hotspots.Items.Should().HaveCount(2);

        var coldItem = hotspots.Items.First(i => i.Id == coldMod.Id);
        var hotItem = hotspots.Items.First(i => i.Id == hotMod.Id);

        hotItem.RiskScore.Should().BeGreaterThan(coldItem.RiskScore);
        hotItem.ChurnOrFanIn.Should().Be(1000);
        coldItem.ChurnOrFanIn.Should().Be(0);
    }

    #endregion

    #region Temporal Coupling with Multi-File Clustering and High Confidence

    /// <summary>
    /// Verifica che TemporalCouplingAnalyzer calcoli correttamente metriche per gruppi di file modificati congiuntamente
    /// e rispetti la confidenza asimmetrica A->B e B->A.
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldCalculateAsymmetricConfidence()
    {
        // Arrange: FileA modificato in 4 commit; FileB modificato in 2 commit (sempre insieme ad A)
        var changelists = new List<List<string>>
        {
            new() { "src/FileA.cs", "src/FileB.cs" },
            new() { "src/FileA.cs", "src/FileB.cs" },
            new() { "src/FileA.cs" },
            new() { "src/FileA.cs" }
        };

        // Act
        var dataset = TemporalCouplingAnalyzer.Analyze(changelists);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Pairs.Should().HaveCount(1);

        var pair = dataset.Pairs[0];
        pair.CoChangeCount.Should().Be(2);
        pair.TotalCommitsA.Should().Be(4);
        pair.TotalCommitsB.Should().Be(2);

        // Confidenza da B verso A è 100% (quando B cambia, A cambia sempre)
        pair.ConfidenceBToA.Should().Be(1.0);
        // Confidenza da A verso B è 50% (2 su 4)
        pair.ConfidenceAToB.Should().Be(0.5);
    }

    #endregion

    #region Social Code with 100% Departed Authors (Complete Orphan Risk)

    /// <summary>
    /// Verifica che SocialCodeAnalyzer rilevi il massimo rischio di abbandono (Orphan)
    /// quando tutti gli sviluppatori storici hanno lasciato l'organizzazione.
    /// </summary>
    [Fact]
    public void SocialCode_ShouldFlagAllFilesAsOrphan_WhenAllAuthorsHaveDeparted()
    {
        // Arrange
        var graph = new CodeGraph();
        var fileA = new Symbol { Id = Guid.NewGuid(), Name = "CoreEngine.cs", QualifiedName = "CoreEngine.cs", FilePath = "src/CoreEngine.cs", Kind = SymbolKind.Class };
        var fileB = new Symbol { Id = Guid.NewGuid(), Name = "PaymentGateway.cs", QualifiedName = "PaymentGateway.cs", FilePath = "src/PaymentGateway.cs", Kind = SymbolKind.Class };
        graph.Symbols.AddRange(new[] { fileA, fileB });

        var history = new List<FileCommitRecord>
        {
            new() { Author = "OldDev1", FilePath = "src/CoreEngine.cs", LinesAdded = 500, LinesDeleted = 20, Timestamp = DateTime.UtcNow.AddDays(-200) },
            new() { Author = "OldDev2", FilePath = "src/PaymentGateway.cs", LinesAdded = 400, LinesDeleted = 10, Timestamp = DateTime.UtcNow.AddDays(-180) }
        };

        // Nessuno degli autori passati è presente nel team attivo attuale
        var activeAuthors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NewDevA", "NewDevB" };

        // Act
        var dataset = SocialCodeAnalyzer.Analyze(graph, history, activeAuthors);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Files.Should().HaveCount(2);
        dataset.Files.Count(f => f.Risk == BusFactorRisk.Orphan).Should().Be(2);

        foreach (var file in dataset.Files)
        {
            file.Risk.Should().Be(BusFactorRisk.Orphan);
        }
    }

    #endregion

    #region Martin Metrics: Zone of Pain and Zone of Uselessness

    /// <summary>
    /// Verifica il calcolo dei punti estremi della metrica di Martin:
    /// - Zona di Sofferenza (A=0, I=0 => D=1.0)
    /// - Zona di Inutilità (A=1.0, I=1.0 => D=1.0)
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldIdentifyZoneOfPainAndZoneOfUselessness()
    {
        // Arrange
        var graph = new CodeGraph();

        // Concrete class with many incoming dependencies and no outgoing dependencies (A=0, I=0 => Zone of Pain)
        var painClass = new Symbol { Id = Guid.NewGuid(), Name = "PainfulCoreClass", QualifiedName = "PainfulCoreClass", FilePath = "PainfulCoreClass.cs", Kind = SymbolKind.Class, LineStart = 1, LineEnd = 50 };
        graph.Symbols.Add(painClass);

        for (int i = 0; i < 5; i++)
        {
            var caller = new Symbol { Id = Guid.NewGuid(), Name = $"Caller_{i}", QualifiedName = $"Caller_{i}", FilePath = $"Caller_{i}.cs", Kind = SymbolKind.Class, LineStart = 1, LineEnd = 20 };
            graph.Symbols.Add(caller);
            graph.AddCalls(caller, painClass);
        }

        // Abstract interface with no incoming dependencies and outgoing dependencies (A=1.0, I=1.0 => Zone of Uselessness)
        var uselessInterface = new Symbol { Id = Guid.NewGuid(), Name = "IUselessInterface", QualifiedName = "IUselessInterface", FilePath = "IUselessInterface.cs", Kind = SymbolKind.Interface, LineStart = 1, LineEnd = 20 };
        var targetDep = new Symbol { Id = Guid.NewGuid(), Name = "OtherDep", QualifiedName = "OtherDep", FilePath = "OtherDep.cs", Kind = SymbolKind.Class, LineStart = 1, LineEnd = 20 };
        graph.Symbols.AddRange(new[] { uselessInterface, targetDep });
        graph.AddUsesType(uselessInterface, targetDep);

        // Act
        var dataset = MartinMetricsCalculator.Analyze(graph);

        // Assert
        var painItem = dataset.Items.First(i => i.Id == painClass.Id);
        painItem.Abstractness.Should().Be(0.0);
        painItem.Instability.Should().Be(0.0);
        painItem.Distance.Should().Be(1.0);

        var uselessItem = dataset.Items.First(i => i.Id == uselessInterface.Id);
        uselessItem.Abstractness.Should().Be(1.0);
        uselessItem.Instability.Should().Be(1.0);
        uselessItem.Distance.Should().Be(1.0);
    }

    #endregion

    #region Cohesion LCOM4: Multiple Disjoint Responsibility Clusters

    /// <summary>
    /// Verifica che una classe con 3 cluster disgiunti di metodi/campi ottenga LCOM4 = 3.
    /// </summary>
    [Fact]
    public void Cohesion_ShouldDetectThreeDisjointClusters()
    {
        // Arrange
        var graph = new CodeGraph();
        var godClass = new Symbol { Id = Guid.NewGuid(), Name = "MegaUtilityService", QualifiedName = "MegaUtilityService", FilePath = "MegaUtilityService.cs", Kind = SymbolKind.Class };
        graph.Symbols.Add(godClass);

        // 3 Clusters: (f1, m1), (f2, m2), (f3, m3)
        for (int c = 1; c <= 3; c++)
        {
            var field = new Symbol { Id = Guid.NewGuid(), Name = $"_field{c}", QualifiedName = $"MegaUtilityService._field{c}", FilePath = "MegaUtilityService.cs", Kind = SymbolKind.Field };
            var method = new Symbol { Id = Guid.NewGuid(), Name = $"Operation{c}", QualifiedName = $"MegaUtilityService.Operation{c}", FilePath = "MegaUtilityService.cs", Kind = SymbolKind.Method };
            graph.Symbols.AddRange(new[] { field, method });
            graph.AddContains(godClass, field);
            graph.AddContains(godClass, method);
            graph.AddWrites(method, field);
        }

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().HaveCount(1);
        var item = dataset.Items[0];
        item.Lcom4.Should().Be(3);
        item.IsRefactoringCandidate.Should().BeTrue();
    }

    #endregion

    #region Architectural Rules Clean Pass Verification

    /// <summary>
    /// Verifica che un'architettura perfettamente conforme non generi alcuna violazione
    /// quando valutata contro un set di regole rigide.
    /// </summary>
    [Fact]
    public void ArchitecturalRules_ShouldReportZeroViolations_WhenCodebaseIsCompliant()
    {
        // Arrange: UI -> Controller -> Service -> Repository (flusso unidirezionale pulito)
        var graph = new CodeGraph();
        var ui = new Symbol { Id = Guid.NewGuid(), Name = "MainForm", QualifiedName = "UI.MainForm", FilePath = "UI/MainForm.cs", Kind = SymbolKind.Form };
        var ctrl = new Symbol { Id = Guid.NewGuid(), Name = "OrderCtrl", QualifiedName = "Controller.OrderCtrl", FilePath = "Controller/OrderCtrl.cs", Kind = SymbolKind.Class };
        var srv = new Symbol { Id = Guid.NewGuid(), Name = "OrderSrv", QualifiedName = "Service.OrderSrv", FilePath = "Service/OrderSrv.cs", Kind = SymbolKind.Class };
        var repo = new Symbol { Id = Guid.NewGuid(), Name = "OrderRepo", QualifiedName = "Repository.OrderRepo", FilePath = "Repository/OrderRepo.cs", Kind = SymbolKind.Class };

        graph.Symbols.AddRange(new[] { ui, ctrl, srv, repo });
        graph.AddCalls(ui, ctrl);
        graph.AddCalls(ctrl, srv);
        graph.AddCalls(srv, repo);

        var rule = new ArchitecturalRule
        {
            Name = "No Direct Repo Calls from UI",
            Type = RuleType.ForbiddenDependency,
            Severity = RuleSeverity.Error,
            SourceSelector = new ComponentSelector { Name = "UI", Pattern = "*UI*" },
            TargetSelector = new ComponentSelector { Name = "Repository", Pattern = "*Repository*" }
        };

        // Act
        var report = ArchitecturalRulesEngine.Evaluate(graph, new[] { rule });

        // Assert
        report.Should().NotBeNull();
        report.TotalViolations.Should().Be(0);
        report.Violations.Should().BeEmpty();
    }

    #endregion
}
