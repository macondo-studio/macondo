using FluentAssertions;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CommonAnalyticsDeepMatrixAndRulesTests
{
    #region ApiDiff Parameter Mutations and Signatures

    /// <summary>
    /// Verifica che l'aggiunta di un nuovo parametro obbligatorio a un metodo pubblico
    /// venga classificata come Breaking Change (Major), mentre un parametro opzionale no.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDistinguishBetweenMandatoryAndOptionalParameterAdditions()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraphMandatory = new CodeGraph();
        var newGraphOptional = new CodeGraph();

        var oldMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "FetchData",
            QualifiedName = "Service.FetchData",
            FilePath = "Service.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        oldMethod.Parameters.Add(new ParameterSymbol { Name = "query", DeclaredTypeName = "String" });
        oldGraph.Symbols.Add(oldMethod);

        // Versione con nuovo parametro obbligatorio
        var newMethodMandatory = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "FetchData",
            QualifiedName = "Service.FetchData",
            FilePath = "Service.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        newMethodMandatory.Parameters.Add(new ParameterSymbol { Name = "query", DeclaredTypeName = "String" });
        newMethodMandatory.Parameters.Add(new ParameterSymbol { Name = "authContext", DeclaredTypeName = "Context", IsOptional = false });
        newGraphMandatory.Symbols.Add(newMethodMandatory);

        // Versione con nuovo parametro opzionale
        var newMethodOptional = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "FetchData",
            QualifiedName = "Service.FetchData",
            FilePath = "Service.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        newMethodOptional.Parameters.Add(new ParameterSymbol { Name = "query", DeclaredTypeName = "String" });
        newMethodOptional.Parameters.Add(new ParameterSymbol { Name = "timeout", DeclaredTypeName = "Int32", IsOptional = true, DefaultValue = "30" });
        newGraphOptional.Symbols.Add(newMethodOptional);

        // Act
        var reportMandatory = ApiDiffAnalyzer.Compare(oldGraph, newGraphMandatory);
        var reportOptional = ApiDiffAnalyzer.Compare(oldGraph, newGraphOptional);

        // Assert
        reportMandatory.Should().NotBeNull();
        reportMandatory.BreakingChangesCount.Should().BeGreaterThan(0);
        reportMandatory.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);

        reportOptional.Should().NotBeNull();
        reportOptional.BreakingChangesCount.Should().Be(0);
    }

    #endregion

    #region Architectural Rules Multi-Layer Enforcement

    /// <summary>
    /// Verifica che l'ArchitecturalRulesEngine rilevi correttamente violazioni architetturali multi-tier,
    /// come la violazione del principio di inversione delle dipendenze (Infrastructure che chiama Presentation).
    /// </summary>
    [Fact]
    public void ArchitecturalRules_ShouldDetectMultiTierLayerViolations()
    {
        // Arrange
        var graph = new CodeGraph();

        var uiController = new Symbol { Id = Guid.NewGuid(), Name = "OrderController", QualifiedName = "UI.OrderController", FilePath = "UI/OrderController.cs", Kind = SymbolKind.Class };
        var repo = new Symbol { Id = Guid.NewGuid(), Name = "OrderRepository", QualifiedName = "Data.OrderRepository", FilePath = "Data/OrderRepository.cs", Kind = SymbolKind.Class };
        var helper = new Symbol { Id = Guid.NewGuid(), Name = "NotificationService", QualifiedName = "UI.NotificationService", FilePath = "UI/NotificationService.cs", Kind = SymbolKind.Class };

        graph.Symbols.AddRange(new[] { uiController, repo, helper });

        // Violazione: Data -> UI
        graph.AddCalls(repo, helper);

        var rule = new ArchitecturalRule
        {
            Name = "Data_Must_Not_Depend_On_UI",
            Description = "Data layer cannot call UI layer",
            Type = RuleType.ForbiddenDependency,
            Severity = RuleSeverity.Error,
            SourceSelector = new ComponentSelector { Pattern = "Data.*" },
            TargetSelector = new ComponentSelector { Pattern = "UI.*" }
        };

        // Act
        var report = ArchitecturalRulesEngine.Evaluate(graph, new[] { rule });

        // Assert
        report.Should().NotBeNull();
        report.Violations.Should().NotBeEmpty();
        report.Violations.Should().Contain(v => v.Rule.Name == "Data_Must_Not_Depend_On_UI");
    }

    #endregion

    #region Dead Code Isolated Recursive Cycles Detection

    /// <summary>
    /// Verifica che un cluster ciclico e ricorsivo di metodi isolati (A -> B -> A),
    /// ma non raggiungibile da alcun entry point dell'applicazione (Main, Form Load, ecc.),
    /// venga correttamente classificato nella sua interezza come Dead Code.
    /// </summary>
    [Fact]
    public void DeadCodeAnalyzer_ShouldClassifyIsolatedMutualRecursiveCycle_AsDeadCode()
    {
        // Arrange
        var graph = new CodeGraph();

        // Entry point attivo
        var mainEntry = new Symbol { Id = Guid.NewGuid(), Name = "Main", QualifiedName = "MainModule.Main", FilePath = "MainModule.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Public };
        var liveHelper = new Symbol { Id = Guid.NewGuid(), Name = "LiveHelper", QualifiedName = "MainModule.LiveHelper", FilePath = "MainModule.bas", Kind = SymbolKind.Method };

        // Ciclo isolato morto
        var deadFuncA = new Symbol { Id = Guid.NewGuid(), Name = "MutualA", QualifiedName = "Legacy.MutualA", FilePath = "Legacy.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Private };
        var deadFuncB = new Symbol { Id = Guid.NewGuid(), Name = "MutualB", QualifiedName = "Legacy.MutualB", FilePath = "Legacy.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Private };

        graph.Symbols.AddRange(new[] { mainEntry, liveHelper, deadFuncA, deadFuncB });

        // Live flow: Main -> LiveHelper
        graph.AddCalls(mainEntry, liveHelper);

        // Dead cycle: MutualA <-> MutualB
        graph.AddCalls(deadFuncA, deadFuncB);
        graph.AddCalls(deadFuncB, deadFuncA);

        // Act
        var report = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        report.Should().NotBeNull();
        report.Items.Select(i => i.Id).Should().Contain(deadFuncA.Id);
        report.Items.Select(i => i.Id).Should().Contain(deadFuncB.Id);
        report.Items.Select(i => i.Id).Should().NotContain(mainEntry.Id);
        report.Items.Select(i => i.Id).Should().NotContain(liveHelper.Id);
    }

    #endregion

    #region Cohesion High Disconnected Cluster LCOM4 Calculation

    /// <summary>
    /// Verifica che una classe con 3 gruppi di metodi totalmente disgiunti
    /// che accedono a campi separati produca un indice di coesione LCOM4 = 3 (Bassa coesione / Responsabilità multiple).
    /// </summary>
    [Fact]
    public void CohesionCalculator_ShouldCalculateHighLCOM4_ForDisjointMethodClusters()
    {
        // Arrange
        var graph = new CodeGraph();

        var containerClass = new Symbol { Id = Guid.NewGuid(), Name = "GodObject", QualifiedName = "Services.GodObject", FilePath = "Services.cs", Kind = SymbolKind.Class };
        graph.Symbols.Add(containerClass);

        // Cluster 1: Field1, Method1
        var f1 = new Symbol { Id = Guid.NewGuid(), Name = "_field1", QualifiedName = "Services.GodObject._field1", FilePath = "Services.cs", Kind = SymbolKind.Field };
        var m1 = new Symbol { Id = Guid.NewGuid(), Name = "Method1", QualifiedName = "Services.GodObject.Method1", FilePath = "Services.cs", Kind = SymbolKind.Method };

        // Cluster 2: Field2, Method2
        var f2 = new Symbol { Id = Guid.NewGuid(), Name = "_field2", QualifiedName = "Services.GodObject._field2", FilePath = "Services.cs", Kind = SymbolKind.Field };
        var m2 = new Symbol { Id = Guid.NewGuid(), Name = "Method2", QualifiedName = "Services.GodObject.Method2", FilePath = "Services.cs", Kind = SymbolKind.Method };

        // Cluster 3: Field3, Method3
        var f3 = new Symbol { Id = Guid.NewGuid(), Name = "_field3", QualifiedName = "Services.GodObject._field3", FilePath = "Services.cs", Kind = SymbolKind.Field };
        var m3 = new Symbol { Id = Guid.NewGuid(), Name = "Method3", QualifiedName = "Services.GodObject.Method3", FilePath = "Services.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { f1, m1, f2, m2, f3, m3 });
        graph.AddContains(containerClass, f1);
        graph.AddContains(containerClass, m1);
        graph.AddContains(containerClass, f2);
        graph.AddContains(containerClass, m2);
        graph.AddContains(containerClass, f3);
        graph.AddContains(containerClass, m3);

        graph.AddReads(m1, f1);
        graph.AddReads(m2, f2);
        graph.AddReads(m3, f3);

        // Act
        var result = CohesionCalculator.Analyze(graph);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().HaveCount(1);
        var item = result.Items[0];
        item.Lcom4.Should().Be(3, "La classe contiene 3 cluster disgiunti e indipendenti");
    }

    #endregion
}
