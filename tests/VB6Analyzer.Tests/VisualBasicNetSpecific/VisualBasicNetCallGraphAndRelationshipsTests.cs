using FluentAssertions;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.VisualBasicNetSpecific;

public class VisualBasicNetCallGraphAndRelationshipsTests
{
    /// <summary>
    /// Verifica che CallGraphBuilder colleghi correttamente le relazioni INHERITS per le classi derivate
    /// (tramite clausola 'Inherits') e IMPLEMENTS per le interfacce (tramite clausola 'Implements').
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireInheritsAndImplements_ForVisualBasicNetTypes()
    {
        // Arrange
        var code = """
            Namespace MyDomain
                Public Interface IAuditable
                    Sub Audit()
                End Interface

                Public Class BaseEntity
                    Public Property Id As Integer
                End Class

                Public Class Order
                    Inherits BaseEntity
                    Implements IAuditable

                    Public Sub Audit() Implements IAuditable.Audit
                    End Sub
                End Class
            End Namespace
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("Entities.vb", code);

        // Assert
        var baseEntitySym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "BaseEntity");
        var ifaceSym = graph.Symbols.First(s => s.Kind == SymbolKind.Interface && s.Name == "IAuditable");
        var orderSym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "Order");

        var inheritsRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Inherits && r.SourceId == orderSym.Id && r.TargetId == baseEntitySym.Id);
        inheritsRel.Should().NotBeNull("Order Inherits BaseEntity must generate an INHERITS relationship");

        var implementsRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Implements && r.SourceId == orderSym.Id && r.TargetId == ifaceSym.Id);
        implementsRel.Should().NotBeNull("Order Implements IAuditable must generate an IMPLEMENTS relationship");
    }

    /// <summary>
    /// Verifica che CallGraphBuilder rilevi sia invocazioni dirette (interne alla classe/modulo)
    /// sia invocazioni tramite ricevitore d'istanza ('receiver.Method(...)'), 'Me' o 'MyBase' generando archi CALLS.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireDirectAndReceiverCalls_ForVisualBasicNetMethods()
    {
        // Arrange
        var code = """
            Public Class Logger
                Public Sub WriteLog(msg As String)
                End Sub
            End Class

            Public Class PaymentService
                Private _logger As Logger

                Public Sub Validate()
                End Sub

                Public Sub ProcessPayment()
                    Validate()
                    Me.Validate()
                    _logger.WriteLog("Payment done")
                End Sub
            End Class
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("Payment.vb", code);

        // Assert
        var processMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "ProcessPayment");
        var validateMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "Validate");
        var writeLogMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "WriteLog");

        var callsInternal = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == processMethod.Id && r.TargetId == validateMethod.Id).ToList();
        callsInternal.Should().NotBeEmpty("ProcessPayment calls Validate internally");

        var callsReceiver = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == processMethod.Id && r.TargetId == writeLogMethod.Id);
        callsReceiver.Should().NotBeNull("ProcessPayment calls _logger.WriteLog");
    }

    /// <summary>
    /// Verifica che CallGraphBuilder rilevi le istanziazioni 'New' (sia 'Dim x As New Foo()' sia 'x = New Foo()')
    /// generando le relazioni INSTANTIATES verso il tipo bersaglio.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireInstantiations_ForNewExpressions()
    {
        // Arrange
        var code = """
            Public Class OrderRepository
            End Class

            Public Class OrderCoordinator
                Public Sub Setup()
                    Dim repo As New OrderRepository()
                    Dim anotherRepo = New OrderRepository()
                End Sub
            End Class
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("Coordinator.vb", code);

        // Assert
        var repoClass = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "OrderRepository");
        var setupMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "Setup");

        var instantiations = graph.Relationships.Where(r => r.Kind == RelationshipKind.Instantiates && r.SourceId == setupMethod.Id && r.TargetId == repoClass.Id).ToList();
        instantiations.Should().NotBeEmpty("Setup must instantiate OrderRepository");
    }

    /// <summary>
    /// Verifica che le invocazioni da classi a routine definite all'interno di un modulo standard VB.NET
    /// ('Module') vengano correttamente collegate tramite archi CALLS.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireModuleCalls_FromClassToModuleFunctions()
    {
        // Arrange
        var code = """
            Public Module StringUtils
                Public Function Capitalize(val As String) As String
                    Return val.ToUpper()
                End Function
            End Module

            Public Class ReportGenerator
                Public Sub Generate()
                    Dim res = StringUtils.Capitalize("annual report")
                End Sub
            End Class
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("Reports.vb", code);

        // Assert
        var capitalizeMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "Capitalize");
        var generateMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "Generate");

        var callRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == generateMethod.Id && r.TargetId == capitalizeMethod.Id);
        callRel.Should().NotBeNull("Generate must call StringUtils.Capitalize");
    }

    /// <summary>
    /// Verifica la risoluzione cross-file su più file sorgente VB.NET analizzati nello stesso workspace.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireCrossFileCalls_BetweenMultipleVisualBasicNetFiles()
    {
        // Arrange
        var fileA = """
            Namespace App.Core
                Public Class DataProvider
                    Public Function GetData() As String
                        Return "payload"
                    End Function
                End Class
            End Namespace
            """;

        var fileB = """
            Namespace App.Client
                Public Class Consumer
                    Public Sub Run()
                        Dim provider As New App.Core.DataProvider()
                        Dim data = provider.GetData()
                    End Sub
                End Class
            End Namespace
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSources(
            ("DataProvider.vb", fileA),
            ("Consumer.vb", fileB));

        // Assert
        results.Should().HaveCount(2);

        var dataProviderClass = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "DataProvider");
        var getDataMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "GetData");
        var runMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "Run");

        // Verifica arco INSTANTIATES cross-file
        var instRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Instantiates && r.SourceId == runMethod.Id && r.TargetId == dataProviderClass.Id);
        instRel.Should().NotBeNull("Consumer.Run must instantiate DataProvider cross-file");

        // Verifica arco CALLS cross-file
        var callRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == runMethod.Id && r.TargetId == getDataMethod.Id);
        callRel.Should().NotBeNull("Consumer.Run must call DataProvider.GetData cross-file");
    }
}
