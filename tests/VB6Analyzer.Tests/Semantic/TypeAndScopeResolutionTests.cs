using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Semantic;

public class TypeAndScopeResolutionTests
{
    [Fact]
    public void TypeResolver_ShouldResolveClassTypesAcrossModules()
    {
        // Arrange
        var graph = new CodeGraph();
        var customerCls = """
            Public Sub Save()
            End Sub
            """;
        var orderCls = """
            Private m_Customer As Customer

            Public Sub Init(ByRef cust As Customer)
                Set m_Customer = cust
            End Sub
            """;

        var res1 = ModuleExtractor.ExtractModule(graph, "Customer.cls", customerCls);
        var res2 = ModuleExtractor.ExtractModule(graph, "Order.cls", orderCls);

        // Act
        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { res1, res2 });

        // Assert
        var customerSymbol = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "Customer");
        var resolvedType = typeResolver.ResolveType("Customer");
        resolvedType.UserTypeSymbol.Should().BeSameAs(customerSymbol);

        var usesTypeRels = graph.Relationships.Where(r => r.Kind == RelationshipKind.UsesType).ToList();
        usesTypeRels.Should().NotBeEmpty();
        usesTypeRels.Should().Contain(r => r.TargetId == customerSymbol.Id);
    }

    [Fact]
    public void SymbolResolver_ShouldResolveLocalVariableBeforeModuleField()
    {
        // Arrange
        var graph = new CodeGraph();
        var code = """
            Private count As Integer

            Public Sub Process()
                Dim count As String
                count = "test"
            End Sub
            """;

        var res = ModuleExtractor.ExtractModule(graph, "ScopedModule.bas", code);
        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);

        // Assert
        var moduleSym = graph.Symbols.First(s => s.Kind == SymbolKind.Module);
        var methodSym = graph.Symbols.First(s => s.Kind == SymbolKind.Method);

        var projectScope = new ProjectScope(graph, typeResolver);
        var moduleScope = new ModuleScope(moduleSym, graph, projectScope);
        var localScope = new LocalScope(methodSym, moduleScope, graph);

        var resolved = localScope.Resolve("count");
        resolved.Should().NotBeNull();
        resolved.TargetTypeName.Should().Be("String"); // local string shadows module integer
    }

    [Fact]
    public void SymbolResolver_ShouldResolveWithBlockImplicitTarget()
    {
        // Arrange
        var graph = new CodeGraph();
        var helperCls = """
            Public Sub ExecuteQuery(ByVal sql As String)
            End Sub
            """;
        var clientCls = """
            Private db As Helper

            Public Sub Run()
                With db
                    .ExecuteQuery "SELECT 1"
                End With
            End Sub
            """;

        var res1 = ModuleExtractor.ExtractModule(graph, "Helper.cls", helperCls);
        var res2 = ModuleExtractor.ExtractModule(graph, "Client.cls", clientCls);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { res1, res2 });

        // Assert
        var executeSym = graph.Symbols.First(s => s.Name == "ExecuteQuery");
        var runSym = graph.Symbols.First(s => s.Name == "Run");

        var callRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == runSym.Id && r.TargetId == executeSym.Id);
        callRel.Should().NotBeNull();
    }
}
