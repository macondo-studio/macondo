using FluentAssertions;
using Macondo.App.Controls;
using Macondo.App.Models;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.App;

public class GraphNodeVisibilityTests
{
    private static (GraphControl control, VisualCodeGraph graph, GraphNode container, GraphNode method, GraphNode prop, GraphNode otherContainer) CreateTestGraph()
    {
        var graph = new VisualCodeGraph();

        var containerId = Guid.NewGuid();
        var container = new GraphNode
        {
            Id = containerId,
            Name = "Module1",
            QualifiedName = "Project1.Module1",
            Kind = SymbolKind.Module,
            IsExpanded = false
        };

        var methodId = Guid.NewGuid();
        var method = new GraphNode
        {
            Id = methodId,
            ParentId = containerId,
            Name = "CalculateTotal",
            QualifiedName = "Project1.Module1.CalculateTotal",
            Kind = SymbolKind.Method
        };

        var propId = Guid.NewGuid();
        var prop = new GraphNode
        {
            Id = propId,
            ParentId = containerId,
            Name = "UserName",
            QualifiedName = "Project1.Module1.UserName",
            Kind = SymbolKind.Property
        };

        var otherContainerId = Guid.NewGuid();
        var otherContainer = new GraphNode
        {
            Id = otherContainerId,
            Name = "Module2",
            QualifiedName = "Project1.Module2",
            Kind = SymbolKind.Module,
            IsExpanded = false
        };

        var otherMethodId = Guid.NewGuid();
        var otherMethod = new GraphNode
        {
            Id = otherMethodId,
            ParentId = otherContainerId,
            Name = "OtherMethod",
            QualifiedName = "Project1.Module2.OtherMethod",
            Kind = SymbolKind.Method
        };

        graph.Nodes.Add(container);
        graph.Nodes.Add(method);
        graph.Nodes.Add(prop);
        graph.Nodes.Add(otherContainer);
        graph.Nodes.Add(otherMethod);

        var control = new GraphControl
        {
            Graph = graph
        };

        return (control, graph, container, method, prop, otherContainer);
    }

    /// <summary>
    /// Check that with the "Containers Only" filter active and no nodes selected, they are displayed
    /// only the container modules/classes and all internal methods and properties hidden.
    /// </summary>
    [Fact]
    public void ContainersOnlyFilter_WithoutSelection_ShouldOnlyShowContainers()
    {
        var (control, graph, container, method, prop, otherContainer) = CreateTestGraph();

        // Preset 2: Containers Only
        control.SetFilters(methods: false, properties: false, fields: false, constants: false, typesAndEnums: false);

        var (visibleCount, totalCount) = control.GetNodeCounts();

        totalCount.Should().Be(5);
        visibleCount.Should().Be(2, "Solo i due moduli container devono essere visibili inizialmente");
    }

    /// <summary>
    /// Verify that by clicking/selecting a container node (module or class) under the "Containers Only" filter,
    /// the container automatically expands, making all its child methods and properties visible.
    /// </summary>
    [Fact]
    public void ContainersOnlyFilter_WhenContainerSelected_ShouldShowAllItsMethodsAndProperties()
    {
        var (control, graph, container, method, prop, otherContainer) = CreateTestGraph();

        // Preset 2: Containers Only
        control.SetFilters(methods: false, properties: false, fields: false, constants: false, typesAndEnums: false);

        // Select the container (simulates clicking on the module node)
        control.SelectNode(container);

        container.IsExpanded.Should().BeTrue("Il container selezionato deve essere automaticamente espanso");

        var (visibleCount, totalCount) = control.GetNodeCounts();

        // 2 containers + 2 members of the selected container (method, prop) = 4 visible nodes.
        // The members of otherContainer remain hidden.
        visibleCount.Should().Be(4, "Tutti i metodi e le proprietà del modulo cliccato devono essere mostrati");
    }

    /// <summary>
    /// Verify that selecting an internal method of a module causes the parent container to expand
    /// keeping both the selected method and its sibling members visible.
    /// </summary>
    [Fact]
    public void ContainersOnlyFilter_WhenChildMethodSelected_ShouldKeepContainerAndSiblingMembersVisible()
    {
        var (control, graph, container, method, prop, otherContainer) = CreateTestGraph();

        // Preset 2: Containers Only
        control.SetFilters(methods: false, properties: false, fields: false, constants: false, typesAndEnums: false);

        // Select the internal method directly
        control.SelectNode(method);

        container.IsExpanded.Should().BeTrue("Il container padre del metodo selezionato deve essere espanso");

        var (visibleCount, totalCount) = control.GetNodeCounts();

        // 2 containers + method + sibling properties = 4 visible
        visibleCount.Should().Be(4, "Il metodo selezionato e i suoi membri fratelli nel container devono rimanere visibili");
    }
}
