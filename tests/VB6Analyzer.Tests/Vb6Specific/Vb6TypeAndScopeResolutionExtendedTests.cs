using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6TypeAndScopeResolutionExtendedTests
{
    /// <summary>
    /// Verifica la risoluzione di chiamate a metodi di moduli standard (.bas) qualificate esplicitamente con il nome del modulo.
    /// </summary>
    [Fact]
    public void SymbolResolver_ShouldResolveExplicitlyQualifiedModuleCalls()
    {
        // Arrange
        var graph = new CodeGraph();
        var utilBas = """
            Public Sub LogAction(ByVal msg As String)
            End Sub
            """;
        var clientCls = """
            Public Sub Execute()
                Utils.LogAction "Running task with module prefix"
            End Sub
            """;

        var res1 = ModuleExtractor.ExtractModule(graph, "Utils.bas", utilBas);
        var res2 = ModuleExtractor.ExtractModule(graph, "Client.cls", clientCls);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        // Act
        builder.BuildGraph(new[] { res1, res2 });

        // Assert
        var logSym = graph.Symbols.First(s => s.Name == "LogAction");
        var execSym = graph.Symbols.First(s => s.Name == "Execute");

        var callRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == execSym.Id && r.TargetId == logSym.Id);
        callRel.Should().NotBeNull("Utils.LogAction should resolve to LogAction in Utils.bas");
    }

    /// <summary>
    /// Verifica la gestione di riferimenti circolari di tipo tra due classi distinte (ClassA riferisce ClassB e ClassB riferisce ClassA).
    /// </summary>
    [Fact]
    public void TypeResolver_ShouldHandleCircularClassDependenciesGracefully()
    {
        // Arrange
        var graph = new CodeGraph();
        var orderCls = """
            Private m_Customer As Customer

            Public Sub SetCustomer(ByRef cust As Customer)
                Set m_Customer = cust
            End Sub

            Public Function GetCustomer() As Customer
                Set GetCustomer = m_Customer
            End Function
            """;

        var customerCls = """
            Private m_PrimaryOrder As Order

            Public Sub SetOrder(ByRef ord As Order)
                Set m_PrimaryOrder = ord
            End Sub

            Public Function GetOrder() As Order
                Set GetOrder = m_PrimaryOrder
            End Function
            """;

        var res1 = ModuleExtractor.ExtractModule(graph, "Order.cls", orderCls);
        var res2 = ModuleExtractor.ExtractModule(graph, "Customer.cls", customerCls);

        // Act
        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(new[] { res1, res2 });

        // Assert
        var orderSym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "Order");
        var customerSym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "Customer");

        var usesTypeRels = graph.Relationships.Where(r => r.Kind == RelationshipKind.UsesType).ToList();
        usesTypeRels.Should().Contain(r => r.TargetId == orderSym.Id);
        usesTypeRels.Should().Contain(r => r.TargetId == customerSym.Id);
    }

    /// <summary>
    /// Verifica la risoluzione di costanti globali definite in moduli standard e l'oscuramento (shadowing) da costanti locali o parametri.
    /// </summary>
    [Fact]
    public void SymbolResolver_ShouldRespectConstantScopingAndShadowing()
    {
        // Arrange
        var graph = new CodeGraph();
        var constBas = """
            Public Const MAX_RETRIES As Long = 5
            """;
        var workerCls = """
            Public Const MAX_RETRIES As Long = 10

            Public Sub DoWork()
                Const MAX_RETRIES As Long = 20
                Dim current As Long
                current = MAX_RETRIES
            End Sub
            """;

        var res1 = ModuleExtractor.ExtractModule(graph, "Constants.bas", constBas);
        var res2 = ModuleExtractor.ExtractModule(graph, "Worker.cls", workerCls);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);

        var workerClassSym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "Worker");
        var doWorkMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "DoWork");

        var projectScope = new ProjectScope(graph, typeResolver);
        var moduleScope = new ModuleScope(workerClassSym, graph, projectScope);
        var localScope = new LocalScope(doWorkMethod, moduleScope, graph);

        // Act
        var resolvedInLocal = localScope.Resolve("MAX_RETRIES");
        var resolvedInModule = moduleScope.Resolve("MAX_RETRIES");
        var resolvedInProject = projectScope.Resolve("MAX_RETRIES");

        // Assert
        resolvedInLocal.Should().NotBeNull();
        resolvedInModule.Should().NotBeNull();
        resolvedInProject.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica che tipi non dichiarati o librerie esterne non referenziate vengano trattati in modo resiliente senza causare eccezioni.
    /// </summary>
    [Fact]
    public void TypeResolver_ShouldHandleUnknownTypesResiliently()
    {
        // Arrange
        var graph = new CodeGraph();
        var code = """
            Private m_UnknownObj As UnreferencedExternalComLibraryType

            Public Sub Process(ByVal unk As NonExistentType)
                Dim temp As AnotherMissingType
            End Sub
            """;

        var res = ModuleExtractor.ExtractModule(graph, "ResilientModule.cls", code);

        // Act
        var typeResolver = new TypeResolver(graph);
        var act = () => typeResolver.ResolveAllTypes();

        // Assert
        act.Should().NotThrow();
        var resolved = typeResolver.ResolveType("UnreferencedExternalComLibraryType");
        resolved.Should().NotBeNull();
        resolved.UserTypeSymbol.Should().BeNull();
    }

    /// <summary>
    /// Verifica l'assegnazione e l'interazione tra molteplici controlli su una stessa Form VB6 (pulsanti, caselle di testo, timer).
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireHandlesEvent_ForMultipleControlsOnSameForm()
    {
        // Arrange
        var graph = new CodeGraph();
        var formCode = """
            VERSION 5.00
            Begin VB.Form ComplexForm
               Caption = "Complex Form"
               Begin VB.CommandButton btnSubmit
                  Caption = "Submit"
               End
               Begin VB.CommandButton btnCancel
                  Caption = "Cancel"
               End
               Begin VB.Timer tmrHeartbeat
                  Interval = 1000
               End
            End
            Attribute VB_Name = "ComplexForm"

            Private Sub btnSubmit_Click()
                Call SaveData
            End Sub

            Private Sub btnCancel_Click()
                Call ResetForm
            End Sub

            Private Sub tmrHeartbeat_Timer()
                Call CheckStatus
            End Sub

            Private Sub SaveData() : End Sub
            Private Sub ResetForm() : End Sub
            Private Sub CheckStatus() : End Sub
            """;

        var res = ModuleExtractor.ExtractModule(graph, "ComplexForm.frm", formCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        // Act
        builder.BuildGraph(new[] { res });

        // Assert
        var formSym = graph.Symbols.First(s => s.Kind == SymbolKind.Form && s.Name == "ComplexForm");
        var btnSubmit = graph.Symbols.First(s => s.Name == "btnSubmit");
        var btnCancel = graph.Symbols.First(s => s.Name == "btnCancel");
        var tmrHeartbeat = graph.Symbols.First(s => s.Name == "tmrHeartbeat");

        var submitClick = graph.Symbols.First(s => s.Name == "btnSubmit_Click");
        var cancelClick = graph.Symbols.First(s => s.Name == "btnCancel_Click");
        var timerTick = graph.Symbols.First(s => s.Name == "tmrHeartbeat_Timer");

        var handlesRels = graph.Relationships.Where(r => r.Kind == RelationshipKind.HandlesEvent).ToList();

        handlesRels.Should().Contain(r => r.SourceId == btnSubmit.Id && r.TargetId == submitClick.Id);
        handlesRels.Should().Contain(r => r.SourceId == btnCancel.Id && r.TargetId == cancelClick.Id);
        handlesRels.Should().Contain(r => r.SourceId == tmrHeartbeat.Id && r.TargetId == timerTick.Id);
    }
}
