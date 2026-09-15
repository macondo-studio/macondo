using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6TypeAndScopeResolutionTests
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

    [Fact]
    public void SymbolResolver_ShouldSupportCaseInsensitiveLookups()
    {
        // Arrange
        var graph = new CodeGraph();
        var code = """
            Public Sub DoWork()
                Dim myCounter As Long
                MYCOUNTER = 100
            End Sub
            """;

        var res = ModuleExtractor.ExtractModule(graph, "Module1.bas", code);
        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);

        var moduleSym = graph.Symbols.First(s => s.Kind == SymbolKind.Module);
        var methodSym = graph.Symbols.First(s => s.Kind == SymbolKind.Method);

        var projectScope = new ProjectScope(graph, typeResolver);
        var moduleScope = new ModuleScope(moduleSym, graph, projectScope);
        var localScope = new LocalScope(methodSym, moduleScope, graph);

        // Act
        var resUpper = localScope.Resolve("MYCOUNTER");
        var resMixed = localScope.Resolve("MyCounter");
        var resLower = localScope.Resolve("mycounter");

        // Assert
        resUpper.Should().NotBeNull();
        resUpper.TargetSymbol.Should().NotBeNull();
        resUpper.TargetSymbol!.Name.Should().Be("myCounter");

        resMixed.TargetSymbol.Should().Be(resUpper.TargetSymbol);
        resLower.TargetSymbol.Should().Be(resUpper.TargetSymbol);
    }

    [Fact]
    public void SymbolResolver_ShouldResolveGlobalFunctionsFromStandardModules()
    {
        // Arrange
        var graph = new CodeGraph();
        var loggerBas = """
            Public Sub LogMessage(ByVal msg As String)
            End Sub
            """;
        var clientCls = """
            Public Sub Execute()
                LogMessage "Executing task..."
            End Sub
            """;

        var res1 = ModuleExtractor.ExtractModule(graph, "Logger.bas", loggerBas);
        var res2 = ModuleExtractor.ExtractModule(graph, "Worker.cls", clientCls);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { res1, res2 });

        // Assert
        var logSym = graph.Symbols.First(s => s.Name == "LogMessage");
        var execSym = graph.Symbols.First(s => s.Name == "Execute");

        var callRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == execSym.Id && r.TargetId == logSym.Id);
        callRel.Should().NotBeNull("Public routines in standard .bas modules should be globally callable without module qualifier");
    }

    [Fact]
    public void SymbolResolver_ShouldResolveChainedMemberAccess()
    {
        // Arrange
        var graph = new CodeGraph();
        var customerCls = """
            Public Sub Save()
            End Sub
            """;
        var orderCls = """
            Private m_Customer As Customer

            Public Property Get Customer() As Customer
                Set Customer = m_Customer
            End Property
            """;
        var controllerCls = """
            Private m_Order As Order

            Public Sub Process()
                m_Order.Customer.Save
            End Sub
            """;

        var res1 = ModuleExtractor.ExtractModule(graph, "Customer.cls", customerCls);
        var res2 = ModuleExtractor.ExtractModule(graph, "Order.cls", orderCls);
        var res3 = ModuleExtractor.ExtractModule(graph, "Controller.cls", controllerCls);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { res1, res2, res3 });

        // Assert
        var saveMethod = graph.Symbols.First(s => s.QualifiedName == "Customer.Save");
        var processMethod = graph.Symbols.First(s => s.QualifiedName == "Controller.Process");

        var callToSave = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == processMethod.Id && r.TargetId == saveMethod.Id);
        callToSave.Should().NotBeNull("Chained call m_Order.Customer.Save should resolve to Customer.Save");
    }
}
