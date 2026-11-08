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
    /// Verifica che con il filtro "Solo Contenitori" attivo e nessun nodo selezionato, vengano visualizzati
    /// unicamente i moduli/classi contenitori e nascosti tutti i metodi e le proprietà interne.
    /// </summary>
    [Fact]
    public void ContainersOnlyFilter_WithoutSelection_ShouldOnlyShowContainers()
    {
        var (control, graph, container, method, prop, otherContainer) = CreateTestGraph();

        // Preset 2: Solo Contenitori
        control.SetFilters(methods: false, properties: false, fields: false, constants: false, typesAndEnums: false);

        var (visibleCount, totalCount) = control.GetNodeCounts();

        totalCount.Should().Be(5);
        visibleCount.Should().Be(2, "Solo i due moduli container devono essere visibili inizialmente");
    }

    /// <summary>
    /// Verifica che cliccando/selezionando un nodo container (modulo o classe) sotto il filtro "Solo Contenitori",
    /// il container si espanda automaticamente rendendo visibili tutti i suoi metodi e proprietà figli.
    /// </summary>
    [Fact]
    public void ContainersOnlyFilter_WhenContainerSelected_ShouldShowAllItsMethodsAndProperties()
    {
        var (control, graph, container, method, prop, otherContainer) = CreateTestGraph();

        // Preset 2: Solo Contenitori
        control.SetFilters(methods: false, properties: false, fields: false, constants: false, typesAndEnums: false);

        // Seleziona il container (simula il click sul nodo del modulo)
        control.SelectNode(container);

        container.IsExpanded.Should().BeTrue("Il container selezionato deve essere automaticamente espanso");

        var (visibleCount, totalCount) = control.GetNodeCounts();

        // 2 container + 2 membri del container selezionato (method, prop) = 4 nodi visibili.
        // I membri di otherContainer rimangono nascosti.
        visibleCount.Should().Be(4, "Tutti i metodi e le proprietà del modulo cliccato devono essere mostrati");
    }

    /// <summary>
    /// Verifica che selezionando un metodo interno di un modulo, il container genitore si espanda
    /// mantenendo visibili sia il metodo selezionato sia i membri fratelli (sibling members).
    /// </summary>
    [Fact]
    public void ContainersOnlyFilter_WhenChildMethodSelected_ShouldKeepContainerAndSiblingMembersVisible()
    {
        var (control, graph, container, method, prop, otherContainer) = CreateTestGraph();

        // Preset 2: Solo Contenitori
        control.SetFilters(methods: false, properties: false, fields: false, constants: false, typesAndEnums: false);

        // Seleziona direttamente il metodo interno
        control.SelectNode(method);

        container.IsExpanded.Should().BeTrue("Il container padre del metodo selezionato deve essere espanso");

        var (visibleCount, totalCount) = control.GetNodeCounts();

        // 2 container + metodo + proprietà sibling = 4 visibili
        visibleCount.Should().Be(4, "Il metodo selezionato e i suoi membri fratelli nel container devono rimanere visibili");
    }
}
