using FluentAssertions;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class ApiDiffAndArchitectureDeepTests
{
    #region ApiDiff & SemVer Deep Scenarios

    /// <summary>
    /// Verifica che la riduzione di visibilità di un metodo da Public a Private
    /// venga classificata come Breaking Change (Major).
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenPublicMethodBecomesPrivate()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var methodOld = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ProcessPayment",
            QualifiedName = "Billing.ProcessPayment",
            FilePath = "Billing.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        oldGraph.Symbols.Add(methodOld);

        var methodNew = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ProcessPayment",
            QualifiedName = "Billing.ProcessPayment",
            FilePath = "Billing.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Private
        };
        newGraph.Symbols.Add(methodNew);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().BeGreaterThan(0);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
    }

    /// <summary>
    /// Verifica che grafi identici o modifiche esclusivamente a metodi privati
    /// non generino alcuna segnalazione di breaking change.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldReportNoBreakingChanges_ForIdenticalGraphsOrInternalChanges()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var pubMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "StableApi",
            QualifiedName = "Service.StableApi",
            FilePath = "Service.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };

        var privHelperOld = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "InternalHelper",
            QualifiedName = "Service.InternalHelper",
            FilePath = "Service.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Private
        };

        var privHelperNew = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "InternalHelperV2",
            QualifiedName = "Service.InternalHelperV2",
            FilePath = "Service.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Private
        };

        oldGraph.Symbols.AddRange(new[] { pubMethod, privHelperOld });
        newGraph.Symbols.AddRange(new[] { pubMethod, privHelperNew });

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(0);
    }

    /// <summary>
    /// Verifica che la rimozione di un'intera interfaccia o classe pubblica
    /// provochi la classificazione MajorBreaking.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenPublicInterfaceIsRemoved()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var iface = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ILegacyProcessor",
            QualifiedName = "Core.ILegacyProcessor",
            FilePath = "Core.cs",
            Kind = SymbolKind.Interface,
            Accessibility = Accessibility.Public
        };
        oldGraph.Symbols.Add(iface);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(1);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
    }

    #endregion

    #region Deep Taint Propagation & Disconnected Paths

    /// <summary>
    /// Verifica la corretta propagazione della taint attraverso una catena profonda a 5 livelli
    /// da un campo UI sorgente non controllato fino a un'istruzione SQL sink.
    /// </summary>
    [Fact]
    public void TaintAnalyzer_ShouldPropagateTaintAcrossDeepFiveLevelCallChain()
    {
        // Arrange
        var graph = new CodeGraph();

        var uiField = new Symbol { Id = Guid.NewGuid(), Name = "txtSearchTerm", QualifiedName = "Form1.txtSearchTerm", FilePath = "Form1.frm", Kind = SymbolKind.Field };
        var uiHandler = new Symbol { Id = Guid.NewGuid(), Name = "btnSearch_Click", QualifiedName = "Form1.btnSearch_Click", FilePath = "Form1.frm", Kind = SymbolKind.Method };
        var step1 = new Symbol { Id = Guid.NewGuid(), Name = "PrepareQuery", QualifiedName = "Service.PrepareQuery", FilePath = "Service.bas", Kind = SymbolKind.Method };
        var step2 = new Symbol { Id = Guid.NewGuid(), Name = "ValidateAndFormat", QualifiedName = "Service.ValidateAndFormat", FilePath = "Service.bas", Kind = SymbolKind.Method };
        var step3 = new Symbol { Id = Guid.NewGuid(), Name = "BuildSqlString", QualifiedName = "Dao.BuildSqlString", FilePath = "Dao.bas", Kind = SymbolKind.Method };
        var sqlSink = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteSqlQuery", QualifiedName = "Db.ExecuteSqlQuery", FilePath = "Db.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { uiField, uiHandler, step1, step2, step3, sqlSink });

        graph.AddReads(uiHandler, uiField);
        graph.AddCalls(uiHandler, step1);
        graph.AddCalls(step1, step2);
        graph.AddCalls(step2, step3);
        graph.AddCalls(step3, sqlSink);

        // Act
        var dataset = TaintAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TaintPaths.Should().NotBeEmpty();

        var path = dataset.TaintPaths.FirstOrDefault(p => p.SinkKind == TaintSinkKind.SqlExecution);
        path.Should().NotBeNull();
        path!.SourceSymbol.Name.Should().Be("txtSearchTerm");
        path.SinkSymbol.Name.Should().Be("ExecuteSqlQuery");
        path.PropagationChain.Should().HaveCount(6);
    }

    /// <summary>
    /// Verifica che un campo di input isolato o senza percorsi verso alcun sink critico
    /// non generi falsi positivi nei TaintPath.
    /// </summary>
    [Fact]
    public void TaintAnalyzer_ShouldNotReportFalsePositives_WhenSourceCannotReachSink()
    {
        // Arrange
        var graph = new CodeGraph();

        var uiField = new Symbol { Id = Guid.NewGuid(), Name = "txtDisplayName", QualifiedName = "Form1.txtDisplayName", FilePath = "Form1.frm", Kind = SymbolKind.Field };
        var uiHandler = new Symbol { Id = Guid.NewGuid(), Name = "btnSave_Click", QualifiedName = "Form1.btnSave_Click", FilePath = "Form1.frm", Kind = SymbolKind.Method };
        var localLogger = new Symbol { Id = Guid.NewGuid(), Name = "LogMessage", QualifiedName = "Log.LogMessage", FilePath = "Log.bas", Kind = SymbolKind.Method };
        
        var disconnectedSqlSink = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteSqlQuery", QualifiedName = "Db.ExecuteSqlQuery", FilePath = "Db.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { uiField, uiHandler, localLogger, disconnectedSqlSink });

        graph.AddReads(uiHandler, uiField);
        graph.AddCalls(uiHandler, localLogger);

        // Act
        var dataset = TaintAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TaintPaths.Should().BeEmpty("Nessun percorso raggiunge un sink critico conosciuto");
    }

    #endregion

    #region Complex Architectural Rules

    /// <summary>
    /// Verifica la valutazione simultanea di regole architetturali multiple (violazioni su livelli diversi).
    /// </summary>
    [Fact]
    public void ArchitecturalRules_ShouldEvaluateMultipleLayeredRulesSimultaneously()
    {
        // Arrange
        var graph = new CodeGraph();

        var uiClass = new Symbol { Id = Guid.NewGuid(), Name = "OrderView", QualifiedName = "UI.OrderView", FilePath = "UI/OrderView.cs", Kind = SymbolKind.Class };
        var uiMethod = new Symbol { Id = Guid.NewGuid(), Name = "Render", QualifiedName = "UI.OrderView.Render", FilePath = "UI/OrderView.cs", Kind = SymbolKind.Method };

        var dbClass = new Symbol { Id = Guid.NewGuid(), Name = "DatabaseHelper", QualifiedName = "Database.DatabaseHelper", FilePath = "Database/Helper.cs", Kind = SymbolKind.Class };
        var dbMethod = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteDirectSql", QualifiedName = "Database.DatabaseHelper.ExecuteDirectSql", FilePath = "Database/Helper.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { uiClass, uiMethod, dbClass, dbMethod });
        graph.AddContains(uiClass, uiMethod);
        graph.AddContains(dbClass, dbMethod);

        // Violazione: UI chiama direttamente Database
        graph.AddCalls(uiMethod, dbMethod);

        var rule = new ArchitecturalRule
        {
            Name = "UI cannot call Database",
            Type = RuleType.ForbiddenDependency,
            Severity = RuleSeverity.Critical,
            SourceSelector = new ComponentSelector { Name = "UI Layer", Pattern = "*UI*" },
            TargetSelector = new ComponentSelector { Name = "DB Layer", Pattern = "*Database*" }
        };

        // Act
        var report = ArchitecturalRulesEngine.Evaluate(graph, new[] { rule });

        // Assert
        report.Should().NotBeNull();
        report.TotalViolations.Should().Be(1);
        report.Violations[0].Rule.Name.Should().Be("UI cannot call Database");
    }

    #endregion

    #region DSM Large Cycle Detection

    /// <summary>
    /// Verifica che DsmMatrixBuilder rilevi cicli estesi composti da 5 nodi (A -> B -> C -> D -> E -> A).
    /// </summary>
    [Fact]
    public void DsmMatrix_ShouldDetectFiveNodeCircularDependency()
    {
        // Arrange
        var graph = new CodeGraph();
        var nodes = new List<Symbol>();

        for (int i = 0; i < 5; i++)
        {
            var mod = new Symbol { Id = Guid.NewGuid(), Name = $"Mod_{(char)('A' + i)}", QualifiedName = $"Mod_{(char)('A' + i)}", FilePath = $"Mod_{(char)('A' + i)}.bas", Kind = SymbolKind.Module };
            nodes.Add(mod);
            graph.Symbols.Add(mod);
        }

        // A -> B -> C -> D -> E -> A
        for (int i = 0; i < 5; i++)
        {
            graph.AddCalls(nodes[i], nodes[(i + 1) % 5]);
        }

        // Act
        var dsm = DsmMatrixBuilder.Build(graph);

        // Assert
        dsm.Should().NotBeNull();
        dsm.StronglyConnectedComponents.Should().HaveCount(1);
        dsm.TotalCyclicDependencies.Should().Be(5);
        dsm.Items.Should().OnlyContain(item => item.HasCircularDependencies);
    }

    #endregion
}
