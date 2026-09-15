using FluentAssertions;
using Macondo.Semantic.Analytics.ScreenFlow;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class ScreenFlowTests
{
    /// <summary>
    /// Verifica l'estrazione delle schermate (Form e UserControl) e delle relative transizioni di navigazione dal grafo.
    /// </summary>
    [Fact]
    public void ScreenFlow_ShouldExtractScreensAndTransitions_FromGraph()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var screenFlow = ScreenFlowBuilder.Build(graph);

        // Assert
        screenFlow.Should().NotBeNull();
        screenFlow.Screens.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifica la corretta differenziazione tra transizioni modali (OpenModal via chiamate) e non modali (OpenModeless via istanziazioni).
    /// </summary>
    [Fact]
    public void ScreenFlow_ShouldDifferentiateModalAndModelessTransitions()
    {
        // Arrange: Synthetic graph with FormA opening FormB (Modal via Calls) and FormC (Modeless via Instantiates)
        var graph = new CodeGraph();
        var formA = new Symbol { Id = Guid.NewGuid(), Name = "FormA", QualifiedName = "FormA", FilePath = "FormA.frm", Kind = SymbolKind.Form };
        var formB = new Symbol { Id = Guid.NewGuid(), Name = "FormB", QualifiedName = "FormB", FilePath = "FormB.frm", Kind = SymbolKind.Form };
        var formC = new Symbol { Id = Guid.NewGuid(), Name = "FormC", QualifiedName = "FormC", FilePath = "FormC.frm", Kind = SymbolKind.Form };

        var btnOpenModal = new Symbol { Id = Guid.NewGuid(), Name = "btnOpenModal_Click", QualifiedName = "FormA.btnOpenModal_Click", FilePath = "FormA.frm", Kind = SymbolKind.Method };
        var btnOpenModeless = new Symbol { Id = Guid.NewGuid(), Name = "btnOpenModeless_Click", QualifiedName = "FormA.btnOpenModeless_Click", FilePath = "FormA.frm", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { formA, formB, formC, btnOpenModal, btnOpenModeless });
        graph.AddContains(formA, btnOpenModal);
        graph.AddContains(formA, btnOpenModeless);

        // Calls -> Modal
        graph.AddCalls(btnOpenModal, formB, 10);
        // Instantiates -> Modeless
        graph.AddInstantiates(btnOpenModeless, formC, 20);

        // Act
        var screenFlow = ScreenFlowBuilder.Build(graph);

        // Assert
        screenFlow.Screens.Should().HaveCount(3);
        screenFlow.Transitions.Should().HaveCount(2);

        var modalTrans = screenFlow.Transitions.First(t => t.TargetScreenId == formB.Id);
        modalTrans.TransitionKind.Should().Be(ScreenTransitionKind.OpenModal);

        var modelessTrans = screenFlow.Transitions.First(t => t.TargetScreenId == formC.Id);
        modelessTrans.TransitionKind.Should().Be(ScreenTransitionKind.OpenModeless);
    }

    /// <summary>
    /// Verifica l'identificazione della schermata di avvio (Startup Screen) e l'assegnazione
    /// di coordinate gerarchiche a livelli (Layered Coordinates) da sinistra verso destra.
    /// </summary>
    [Fact]
    public void ScreenFlow_ShouldAssignStartupScreen_AndLayeredCoordinates()
    {
        // Arrange
        var graph = new CodeGraph();
        var mainForm = new Symbol { Id = Guid.NewGuid(), Name = "frmMain", QualifiedName = "frmMain", FilePath = "frmMain.frm", Kind = SymbolKind.Form };
        var detailsForm = new Symbol { Id = Guid.NewGuid(), Name = "frmDetails", QualifiedName = "frmDetails", FilePath = "frmDetails.frm", Kind = SymbolKind.Form };

        graph.Symbols.AddRange(new[] { mainForm, detailsForm });
        graph.AddCalls(mainForm, detailsForm, 10);

        // Act
        var screenFlow = ScreenFlowBuilder.Build(graph);

        // Assert
        var mainScreen = screenFlow.FindScreen(mainForm.Id);
        var detailsScreen = screenFlow.FindScreen(detailsForm.Id);

        mainScreen.Should().NotBeNull();
        mainScreen!.IsStartupScreen.Should().BeTrue();

        detailsScreen.Should().NotBeNull();
        detailsScreen!.X.Should().BeGreaterThan(mainScreen.X, "Details form at level 1 should be positioned to the right of frmMain at level 0");
    }
}
