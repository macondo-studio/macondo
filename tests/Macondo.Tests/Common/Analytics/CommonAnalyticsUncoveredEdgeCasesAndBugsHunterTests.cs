using FluentAssertions;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Analytics.ControlFlow;
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CommonAnalyticsUncoveredEdgeCasesAndBugsHunterTests
{
    #region Control Flow Multi-Exit and Deep Branching Scenarios

    /// <summary>
    /// Verifica che ControlFlowBuilder gestisca routine con molteplici punti di uscita anticipati (Exit Sub / return multipli).
    /// </summary>
    [Fact]
    public void ControlFlowBuilder_ShouldHandleMultipleExitPointsAndEarlyReturns()
    {
        // Arrange
        var graph = new CodeGraph();
        var complexRoutine = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ValidateAndProcessOrder",
            QualifiedName = "OrderService.ValidateAndProcessOrder",
            FilePath = "OrderService.cs",
            Kind = SymbolKind.Method
        };
        complexRoutine.Parameters.Add(new ParameterSymbol { Name = "orderId", DeclaredTypeName = "Long" });
        complexRoutine.Parameters.Add(new ParameterSymbol { Name = "amount", DeclaredTypeName = "Double" });
        complexRoutine.Parameters.Add(new ParameterSymbol { Name = "isVip", DeclaredTypeName = "Boolean" });

        var step1 = new Symbol { Id = Guid.NewGuid(), Name = "CheckInventory", QualifiedName = "OrderService.CheckInventory", FilePath = "OrderService.cs", Kind = SymbolKind.Method };
        var step2 = new Symbol { Id = Guid.NewGuid(), Name = "ApplyDiscount", QualifiedName = "OrderService.ApplyDiscount", FilePath = "OrderService.cs", Kind = SymbolKind.Method };
        var step3 = new Symbol { Id = Guid.NewGuid(), Name = "PersistTransaction", QualifiedName = "OrderService.PersistTransaction", FilePath = "OrderService.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { complexRoutine, step1, step2, step3 });
        graph.AddCalls(complexRoutine, step1, 10);
        graph.AddCalls(complexRoutine, step2, 20);
        graph.AddCalls(complexRoutine, step3, 30);

        // Act
        var cfg = ControlFlowBuilder.BuildForSymbol(complexRoutine, graph);

        // Assert
        cfg.Should().NotBeNull();
        cfg.Blocks.Should().Contain(b => b.BlockKind == ControlFlowBlockKind.Entry);
        cfg.Blocks.Should().Contain(b => b.BlockKind == ControlFlowBlockKind.Exit);
        cfg.CyclomaticComplexity.Should().BeGreaterThan(1);
        cfg.Edges.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifica che una routine completamente vuota o priva di istruzioni produca un CFG coerente con complessità pari a 1.
    /// </summary>
    [Fact]
    public void ControlFlowBuilder_ShouldProduceValidCFG_ForEmptyOrStubRoutine()
    {
        // Arrange
        var graph = new CodeGraph();
        var emptySub = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "NoOpStub",
            QualifiedName = "ModuleA.NoOpStub",
            FilePath = "ModuleA.bas",
            Kind = SymbolKind.Method
        };
        graph.Symbols.Add(emptySub);

        // Act
        var cfg = ControlFlowBuilder.BuildForSymbol(emptySub, graph);

        // Assert
        cfg.Should().NotBeNull();
        cfg.CyclomaticComplexity.Should().Be(1);
        cfg.Blocks.Should().Contain(b => b.BlockKind == ControlFlowBlockKind.Entry);
        cfg.Blocks.Should().Contain(b => b.BlockKind == ControlFlowBlockKind.Exit);
    }

    #endregion

    #region Data Flow Multi-Hop Taint Propagation and Multiple Sinks

    /// <summary>
    /// Verifica la propagazione del flusso di contaminazione (Taint Analysis) attraverso una catena profonda
    /// a 5 livelli (UI Input -> Controller -> Service -> Repository -> Database Sink).
    /// </summary>
    [Fact]
    public void DataFlow_ShouldPropagateTaintThroughDeepMultiHopChain()
    {
        // Arrange
        var graph = new CodeGraph();

        var txtQuery = new Symbol { Id = Guid.NewGuid(), Name = "txtFilterInput", QualifiedName = "SearchForm.txtFilterInput", FilePath = "SearchForm.frm", Kind = SymbolKind.Field };
        var uiHandler = new Symbol { Id = Guid.NewGuid(), Name = "btnSearch_Click", QualifiedName = "SearchForm.btnSearch_Click", FilePath = "SearchForm.frm", Kind = SymbolKind.Method };
        var controller = new Symbol { Id = Guid.NewGuid(), Name = "SearchController", QualifiedName = "SearchController.Execute", FilePath = "SearchController.cs", Kind = SymbolKind.Method };
        var service = new Symbol { Id = Guid.NewGuid(), Name = "SearchService", QualifiedName = "SearchService.QueryCatalog", FilePath = "SearchService.cs", Kind = SymbolKind.Method };
        var repository = new Symbol { Id = Guid.NewGuid(), Name = "CatalogRepo", QualifiedName = "CatalogRepo.FindRaw", FilePath = "CatalogRepo.cs", Kind = SymbolKind.Method };
        var dbSink = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteSqlQuery", QualifiedName = "DbHelper.ExecuteSqlQuery", FilePath = "DbHelper.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { txtQuery, uiHandler, controller, service, repository, dbSink });

        graph.AddReads(uiHandler, txtQuery);
        graph.AddCalls(uiHandler, controller);
        graph.AddCalls(controller, service);
        graph.AddCalls(service, repository);
        graph.AddCalls(repository, dbSink);

        // Act
        var dataset = TaintAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TaintPaths.Should().NotBeEmpty();

        var path = dataset.TaintPaths.FirstOrDefault(p => p.SinkKind == TaintSinkKind.SqlExecution);
        path.Should().NotBeNull();
        path!.SourceSymbol.Name.Should().Be("txtFilterInput");
        path.SinkSymbol.Name.Should().Be("ExecuteSqlQuery");
        path.PropagationChain.Should().HaveCount(6);
    }

    #endregion

    #region Dead Code Mutually Recursive Islands

    /// <summary>
    /// Verifica che un'isola di codice composta da routine mutuamente ricorsive (A chiama B e B chiama A),
    /// ma priva di qualsiasi chiamata da Entry Point o da codice pubblico attivo, venga classificata come codice morto.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldIdentifyMutuallyRecursiveDeadIslands()
    {
        // Arrange
        var graph = new CodeGraph();

        var mod = new Symbol { Id = Guid.NewGuid(), Name = "DeadClusterMod", QualifiedName = "DeadClusterMod", FilePath = "DeadClusterMod.bas", Kind = SymbolKind.Module };
        var liveMain = new Symbol { Id = Guid.NewGuid(), Name = "Main", QualifiedName = "DeadClusterMod.Main", FilePath = "DeadClusterMod.bas", Kind = SymbolKind.Method };
        var liveWorker = new Symbol { Id = Guid.NewGuid(), Name = "ActiveWorker", QualifiedName = "DeadClusterMod.ActiveWorker", FilePath = "DeadClusterMod.bas", Kind = SymbolKind.Method };

        var deadRoutineA = new Symbol { Id = Guid.NewGuid(), Name = "DeadPing", QualifiedName = "DeadClusterMod.DeadPing", FilePath = "DeadClusterMod.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Private, LineStart = 50, LineEnd = 65 };
        var deadRoutineB = new Symbol { Id = Guid.NewGuid(), Name = "DeadPong", QualifiedName = "DeadClusterMod.DeadPong", FilePath = "DeadClusterMod.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Private, LineStart = 70, LineEnd = 85 };

        graph.Symbols.AddRange(new[] { mod, liveMain, liveWorker, deadRoutineA, deadRoutineB });
        graph.AddContains(mod, liveMain);
        graph.AddContains(mod, liveWorker);
        graph.AddContains(mod, deadRoutineA);
        graph.AddContains(mod, deadRoutineB);

        // Live flow: Main -> ActiveWorker
        graph.AddCalls(liveMain, liveWorker);

        // Mutually recursive dead cycle: DeadPing <-> DeadPong
        graph.AddCalls(deadRoutineA, deadRoutineB);
        graph.AddCalls(deadRoutineB, deadRoutineA);

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        dataset.Items.Should().NotContain(i => i.Id == liveMain.Id);
        dataset.Items.Should().NotContain(i => i.Id == liveWorker.Id);

        var deadItemA = dataset.Items.FirstOrDefault(i => i.Id == deadRoutineA.Id);
        var deadItemB = dataset.Items.FirstOrDefault(i => i.Id == deadRoutineB.Id);

        deadItemA.Should().NotBeNull("DeadPing has no incoming calls from entry points");
        deadItemB.Should().NotBeNull("DeadPong has no incoming calls from entry points");
    }

    #endregion

    #region ApiDiff Parameter Changes and Overload Bumps

    /// <summary>
    /// Verifica che la rimozione di un parametro o l'aggiunta di un parametro obbligatorio in una API pubblica
    /// venga rilevata e classificata con impatto SemVer adeguato.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenPublicMethodSignatureChanges()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var oldMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ExportReport",
            QualifiedName = "ReportGenerator.ExportReport",
            FilePath = "ReportGenerator.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        oldMethod.Parameters.Add(new ParameterSymbol { Name = "reportId", DeclaredTypeName = "Long" });
        oldGraph.Symbols.Add(oldMethod);

        // Nel nuovo grafo il metodo è stato rimosso o modificato in modo incompatibile
        var newMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ExportReportV2",
            QualifiedName = "ReportGenerator.ExportReportV2",
            FilePath = "ReportGenerator.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        newMethod.Parameters.Add(new ParameterSymbol { Name = "reportId", DeclaredTypeName = "Long" });
        newMethod.Parameters.Add(new ParameterSymbol { Name = "format", DeclaredTypeName = "String" });
        newGraph.Symbols.Add(newMethod);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().BeGreaterThan(0);
        report.AdditionsCount.Should().BeGreaterThan(0);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
    }

    #endregion

    #region CRUD Matrix Multi-Entity Transactions

    /// <summary>
    /// Verifica la generazione della matrice CRUD per servizi complessi che manipolano contemporaneamente più entità distinte.
    /// </summary>
    [Fact]
    public void CrudMatrix_ShouldTrackOperationsAcrossMultipleEntitiesInSingleService()
    {
        // Arrange
        var graph = new CodeGraph();

        var invoiceEntity = new Symbol { Id = Guid.NewGuid(), Name = "Invoice", QualifiedName = "Invoice", FilePath = "Invoice.cs", Kind = SymbolKind.Class };
        var lineItemEntity = new Symbol { Id = Guid.NewGuid(), Name = "LineItem", QualifiedName = "LineItem", FilePath = "LineItem.cs", Kind = SymbolKind.Class };
        var accountEntity = new Symbol { Id = Guid.NewGuid(), Name = "Account", QualifiedName = "Account", FilePath = "Account.cs", Kind = SymbolKind.Class };

        var createInvoice = new Symbol { Id = Guid.NewGuid(), Name = "CreateInvoice", QualifiedName = "BillingService.CreateInvoice", FilePath = "BillingService.cs", Kind = SymbolKind.Method };
        var addLineItem = new Symbol { Id = Guid.NewGuid(), Name = "AddLineItem", QualifiedName = "BillingService.AddLineItem", FilePath = "BillingService.cs", Kind = SymbolKind.Method };
        var updateAccount = new Symbol { Id = Guid.NewGuid(), Name = "UpdateAccountBalance", QualifiedName = "BillingService.UpdateAccountBalance", FilePath = "BillingService.cs", Kind = SymbolKind.Method };
        var deleteInvoice = new Symbol { Id = Guid.NewGuid(), Name = "DeleteInvoice", QualifiedName = "BillingService.DeleteInvoice", FilePath = "BillingService.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { invoiceEntity, lineItemEntity, accountEntity, createInvoice, addLineItem, updateAccount, deleteInvoice });

        // Act
        var dataset = CrudMatrixAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Entities.Should().NotBeEmpty();
        dataset.EntitySummaries.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    #endregion
}
