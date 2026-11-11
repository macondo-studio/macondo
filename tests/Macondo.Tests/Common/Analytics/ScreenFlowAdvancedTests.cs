using FluentAssertions;
using Macondo.Semantic.Analytics.ScreenFlow;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class ScreenFlowAdvancedTests
{
    /// <summary>
    /// Verify that ScreenFlowBuilder detects navigation transitions between UI (Form) screens
    /// either through direct calls or sub-window instantiations.
    /// </summary>
    [Fact]
    public void ScreenFlowBuilder_ShouldIdentifyMultipleFormsAndTransitions()
    {
        // Arrange
        var graph = new CodeGraph();

        var mainForm = new Symbol { Id = Guid.NewGuid(), Name = "frmMain", QualifiedName = "frmMain", FilePath = "frmMain.frm", Kind = SymbolKind.Form };
        var btnOpenOrders = new Symbol { Id = Guid.NewGuid(), Name = "cmdOrders_Click", QualifiedName = "frmMain.cmdOrders_Click", FilePath = "frmMain.frm", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { mainForm, btnOpenOrders });
        graph.AddContains(mainForm, btnOpenOrders);

        var ordersForm = new Symbol { Id = Guid.NewGuid(), Name = "frmOrders", QualifiedName = "frmOrders", FilePath = "frmOrders.frm", Kind = SymbolKind.Form };
        var btnOpenModal = new Symbol { Id = Guid.NewGuid(), Name = "cmdPay_Click", QualifiedName = "frmOrders.cmdPay_Click", FilePath = "frmOrders.frm", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { ordersForm, btnOpenModal });
        graph.AddContains(ordersForm, btnOpenModal);

        var modalForm = new Symbol { Id = Guid.NewGuid(), Name = "frmPaymentModal", QualifiedName = "frmPaymentModal", FilePath = "frmPaymentModal.frm", Kind = SymbolKind.Form };
        graph.Symbols.Add(modalForm);

        // Transitions: mainForm -> ordersForm and ordersForm -> modalForm
        graph.AddInstantiates(btnOpenOrders, ordersForm);
        graph.AddInstantiates(btnOpenModal, modalForm);

        // Act
        var flowGraph = ScreenFlowBuilder.Build(graph);

        // Assert
        flowGraph.Should().NotBeNull();
        flowGraph.Screens.Should().HaveCount(3);
        flowGraph.Transitions.Should().HaveCount(2);

        var trans1 = flowGraph.Transitions.FirstOrDefault(t => t.SourceScreenId == mainForm.Id && t.TargetScreenId == ordersForm.Id);
        var trans2 = flowGraph.Transitions.FirstOrDefault(t => t.SourceScreenId == ordersForm.Id && t.TargetScreenId == modalForm.Id);

        trans1.Should().NotBeNull();
        trans2.Should().NotBeNull();
    }

    /// <summary>
    /// Check that cyclic navigation paths between screens (FormA <-> FormB)
    /// do not generate infinite loops or errors in the calculation of the layered layout.
    /// </summary>
    [Fact]
    public void ScreenFlowBuilder_ShouldHandleCyclicNavigation()
    {
        // Arrange: frmA apre frmB e frmB apre frmA
        var graph = new CodeGraph();

        var formA = new Symbol { Id = Guid.NewGuid(), Name = "frmA", QualifiedName = "frmA", FilePath = "frmA.frm", Kind = SymbolKind.Form };
        var methodA = new Symbol { Id = Guid.NewGuid(), Name = "OpenB", QualifiedName = "frmA.OpenB", FilePath = "frmA.frm", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { formA, methodA });
        graph.AddContains(formA, methodA);

        var formB = new Symbol { Id = Guid.NewGuid(), Name = "frmB", QualifiedName = "frmB", FilePath = "frmB.frm", Kind = SymbolKind.Form };
        var methodB = new Symbol { Id = Guid.NewGuid(), Name = "OpenA", QualifiedName = "frmB.OpenA", FilePath = "frmB.frm", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { formB, methodB });
        graph.AddContains(formB, methodB);

        graph.AddInstantiates(methodA, formB);
        graph.AddInstantiates(methodB, formA);

        // Act
        var flowGraph = ScreenFlowBuilder.Build(graph);

        // Assert
        flowGraph.Screens.Should().HaveCount(2);
        flowGraph.Transitions.Should().HaveCount(2);

        foreach (var screen in flowGraph.Screens)
        {
            screen.X.Should().NotBe(double.NaN);
            screen.Y.Should().NotBe(double.NaN);
            double.IsInfinity(screen.X).Should().BeFalse();
            double.IsInfinity(screen.Y).Should().BeFalse();
        }
    }

    /// <summary>
    /// Checks the inclusion of user controls (UserControl) within a Form.
    /// </summary>
    [Fact]
    public void ScreenFlowBuilder_ShouldDetectUserControlEmbedding()
    {
        // Arrange
        var graph = new CodeGraph();

        var form = new Symbol { Id = Guid.NewGuid(), Name = "frmMain", QualifiedName = "frmMain", FilePath = "frmMain.frm", Kind = SymbolKind.Form };
        var uctl = new Symbol { Id = Guid.NewGuid(), Name = "ctlToolbar", QualifiedName = "ctlToolbar", FilePath = "ctlToolbar.ctl", Kind = SymbolKind.UserControl };

        graph.Symbols.AddRange(new[] { form, uctl });
        graph.AddUsesType(form, uctl);

        // Act
        var flowGraph = ScreenFlowBuilder.Build(graph);

        // Assert
        flowGraph.Screens.Should().HaveCount(2);
        flowGraph.Screens.Should().Contain(s => s.Kind == SymbolKind.UserControl && s.Name == "ctlToolbar");
    }
}
