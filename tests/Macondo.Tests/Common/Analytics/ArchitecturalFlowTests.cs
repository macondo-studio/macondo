using FluentAssertions;
using Macondo.Semantic.Analytics.ArchitecturalFlow;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class ArchitecturalFlowTests
{
    /// <summary>
    /// Verifica l'esecuzione dell'analisi dei flussi architetturali sull'intero set di fixture di progetto,
    /// confermando la costruzione coerente di nodi, layer, link e volumi di flusso.
    /// </summary>
    [Fact]
    public void ArchitecturalFlow_ShouldExecuteOnFixtures_AndBuildValidFlowDataset()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var dataset = ArchitecturalFlowAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Nodes.Should().NotBeEmpty();
        dataset.Links.Should().NotBeEmpty();
        dataset.TotalFlowVolume.Should().BeGreaterThan(0);

        foreach (var node in dataset.Nodes)
        {
            node.SweepAngle.Should().BeGreaterThan(0.0);
            node.Loc.Should().BeGreaterThan(0);
        }
    }

    /// <summary>
    /// Verifica che l'analizzatore deduca correttamente i layer architetturali (UserInterface, BusinessLogic,
    /// DataAccess, CommonUtilities) in base alla tipologia di simbolo e alle convenzioni di denominazione.
    /// </summary>
    [Fact]
    public void ArchitecturalFlow_ShouldInferLayers_BasedOnSymbolKindAndNameConventions()
    {
        // Arrange
        var graph = new CodeGraph();

        var form = new Symbol { Id = Guid.NewGuid(), Name = "frmCustomer", QualifiedName = "frmCustomer", FilePath = "frmCustomer.frm", Kind = SymbolKind.Form };
        var service = new Symbol { Id = Guid.NewGuid(), Name = "OrderManager", QualifiedName = "OrderManager", FilePath = "OrderManager.cls", Kind = SymbolKind.Class };
        var repo = new Symbol { Id = Guid.NewGuid(), Name = "CustomerDao", QualifiedName = "CustomerDao", FilePath = "CustomerDao.cls", Kind = SymbolKind.Class };
        var util = new Symbol { Id = Guid.NewGuid(), Name = "StringUtils", QualifiedName = "StringUtils", FilePath = "StringUtils.bas", Kind = SymbolKind.Module };

        graph.Symbols.AddRange(new[] { form, service, repo, util });

        // Act
        var dataset = ArchitecturalFlowAnalyzer.Analyze(graph);

        // Assert
        dataset.FindNodeById(form.Id)?.Layer.Should().Be(ArchitecturalLayerKind.UserInterface);
        dataset.FindNodeById(service.Id)?.Layer.Should().Be(ArchitecturalLayerKind.BusinessLogic);
        dataset.FindNodeById(repo.Id)?.Layer.Should().Be(ArchitecturalLayerKind.DataAccess);
        dataset.FindNodeById(util.Id)?.Layer.Should().Be(ArchitecturalLayerKind.CommonUtilities);
    }

    /// <summary>
    /// Verifica che le chiamate che violano la gerarchia a livelli (es. DataAccess che richiama direttamente la UI
    /// o CommonUtilities che dipende da BusinessLogic) vengano contrassegnate come violazioni architetturali (IsLayerViolation = true).
    /// </summary>
    [Fact]
    public void ArchitecturalFlow_ShouldDetectLayerViolations_SuchAsDataAccessCallingUserInterface()
    {
        // Arrange: DataAccess (CustomerDao) invoca UserInterface (frmCustomer)
        var graph = new CodeGraph();

        var form = new Symbol { Id = Guid.NewGuid(), Name = "frmCustomer", QualifiedName = "frmCustomer", FilePath = "frmCustomer.frm", Kind = SymbolKind.Form };
        var formMethod = new Symbol { Id = Guid.NewGuid(), Name = "ShowAlert", QualifiedName = "frmCustomer.ShowAlert", FilePath = "frmCustomer.frm", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { form, formMethod });
        graph.AddContains(form, formMethod);

        var dao = new Symbol { Id = Guid.NewGuid(), Name = "CustomerDao", QualifiedName = "CustomerDao", FilePath = "CustomerDao.cls", Kind = SymbolKind.Class };
        var daoMethod = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteQuery", QualifiedName = "CustomerDao.ExecuteQuery", FilePath = "CustomerDao.cls", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { dao, daoMethod });
        graph.AddContains(dao, daoMethod);

        // Violazione: daoMethod chiama formMethod
        graph.AddCalls(daoMethod, formMethod);

        // Act
        var dataset = ArchitecturalFlowAnalyzer.Analyze(graph);

        // Assert
        dataset.LayerViolationsCount.Should().Be(1);
        var violationLink = dataset.Links.First();
        violationLink.IsLayerViolation.Should().BeTrue();
        violationLink.SourceName.Should().Be("CustomerDao");
        violationLink.TargetName.Should().Be("frmCustomer");
    }

    /// <summary>
    /// Verifica che i nodi del Chord Diagram abbiano archi angolari coerenti che coprono l'intervallo [0 .. 2π].
    /// </summary>
    [Fact]
    public void ArchitecturalFlow_ShouldComputeInflowOutflowAndChordAngles()
    {
        // Arrange
        var graph = new CodeGraph();

        var uiMod = new Symbol { Id = Guid.NewGuid(), Name = "frmMain", QualifiedName = "frmMain", FilePath = "frmMain.frm", Kind = SymbolKind.Form };
        var uiMethod = new Symbol { Id = Guid.NewGuid(), Name = "OnLoad", QualifiedName = "frmMain.OnLoad", FilePath = "frmMain.frm", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { uiMod, uiMethod });
        graph.AddContains(uiMod, uiMethod);

        var bizMod = new Symbol { Id = Guid.NewGuid(), Name = "BusinessService", QualifiedName = "BusinessService", FilePath = "BusinessService.cls", Kind = SymbolKind.Class };
        var bizMethod = new Symbol { Id = Guid.NewGuid(), Name = "Process", QualifiedName = "BusinessService.Process", FilePath = "BusinessService.cls", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { bizMod, bizMethod });
        graph.AddContains(bizMod, bizMethod);

        graph.AddCalls(uiMethod, bizMethod);

        // Act
        var dataset = ArchitecturalFlowAnalyzer.Analyze(graph);

        // Assert
        var uiNode = dataset.FindNodeById(uiMod.Id);
        var bizNode = dataset.FindNodeById(bizMod.Id);

        uiNode.Should().NotBeNull();
        bizNode.Should().NotBeNull();

        uiNode!.OutflowVolume.Should().Be(1);
        bizNode!.InflowVolume.Should().Be(1);

        uiNode.StartAngle.Should().BeGreaterOrEqualTo(0.0);
        bizNode.EndAngle.Should().BeLessThanOrEqualTo(2 * Math.PI + 0.5);
    }
}
