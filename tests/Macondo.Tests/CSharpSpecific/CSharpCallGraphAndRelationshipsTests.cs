using FluentAssertions;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpCallGraphAndRelationshipsTests
{
    /// <summary>
    /// Verifica che CallGraphBuilder colleghi correttamente le relazioni INHERITS per le classi derivate
    /// e IMPLEMENTS per le interfacce implementate da tipi C#.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireInheritanceAndInterfaceImplementation_ForCSharpTypes()
    {
        // Arrange
        var code = """
            namespace MyApp.Domain
            {
                public interface IAuditable
                {
                    void Audit();
                }

                public class BaseEntity
                {
                    public int Id { get; set; }
                }

                public class Order : BaseEntity, IAuditable
                {
                    public void Audit() { }
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Entities.cs", code);

        // Assert
        var baseEntitySym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "BaseEntity");
        var ifaceSym = graph.Symbols.First(s => s.Kind == SymbolKind.Interface && s.Name == "IAuditable");
        var orderSym = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "Order");

        var inheritsRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Inherits && r.SourceId == orderSym.Id && r.TargetId == baseEntitySym.Id);
        inheritsRel.Should().NotBeNull("Order : BaseEntity must generate an INHERITS relationship");

        var implementsRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Implements && r.SourceId == orderSym.Id && r.TargetId == ifaceSym.Id);
        implementsRel.Should().NotBeNull("Order : IAuditable must generate an IMPLEMENTS relationship");
    }

    /// <summary>
    /// Verifica che CallGraphBuilder rilevi sia invocazioni dirette a metodi interni (senza receiver)
    /// sia invocazioni tramite membri di istanza ('receiver.Method(...)') generando relazioni CALLS.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireCallsRelationships_ForDirectAndReceiverMethodInvocations()
    {
        // Arrange
        var code = """
            namespace MyApp.Services
            {
                public class Logger
                {
                    public void WriteLog(string msg) { }
                }

                public class PaymentService
                {
                    private Logger logger = new Logger();

                    public void ProcessPayment()
                    {
                        Validate();
                        logger.WriteLog("Payment processed");
                    }

                    private void Validate() { }
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeCSharpSource("PaymentService.cs", code);

        // Assert
        var processMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "ProcessPayment");
        var validateMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "Validate");
        var writeLogMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "WriteLog");

        var callsValidate = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == processMethod.Id && r.TargetId == validateMethod.Id);
        callsValidate.Should().NotBeNull("ProcessPayment should call internal Validate method");

        var callsWriteLog = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == processMethod.Id && r.TargetId == writeLogMethod.Id);
        callsWriteLog.Should().NotBeNull("ProcessPayment should call logger.WriteLog method");
    }

    /// <summary>
    /// Verifica che l'espressione di creazione oggetto 'new Type()' sia all'interno dei metodi che
    /// negli inizializzatori di campo generi relazioni INSTANTIATES verso il tipo target nel CodeGraph.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireInstantiatesRelationships_FromCSharpNewExpressions()
    {
        // Arrange
        var code = """
            namespace MyApp.Factory
            {
                public class Repository { }

                public class Worker
                {
                    private Repository _repo = new Repository();

                    public void Run()
                    {
                        var localRepo = new Repository();
                    }
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Worker.cs", code);

        // Assert
        var repoClass = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "Repository");
        var repoField = graph.Symbols.First(s => s.Name == "_repo");
        var runMethod = graph.Symbols.First(s => s.Kind == SymbolKind.Method && s.Name == "Run");

        var fieldInstantiates = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Instantiates && r.SourceId == repoField.Id && r.TargetId == repoClass.Id);
        fieldInstantiates.Should().NotBeNull("Field initializer 'new Repository()' should create INSTANTIATES relationship");

        var methodInstantiates = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Instantiates && r.SourceId == runMethod.Id && r.TargetId == repoClass.Id);
        methodInstantiates.Should().NotBeNull("Method body 'new Repository()' should create INSTANTIATES relationship");
    }

    /// <summary>
    /// Verifica che le query di Call Graph e Impact Analysis funzionino coerentemente su grafi derivati da C#,
    /// tracciando chiamanti diretti (livello 1) e indiretti (livello 2+).
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldSupportCSharpCallChainsAndImpactAnalysis()
    {
        // Arrange
        var code = """
            namespace MyApp.App
            {
                public class Dal
                {
                    public void Save() { }
                }

                public class Biz
                {
                    private Dal _dal = new Dal();
                    public void UpdateOrder()
                    {
                        _dal.Save();
                    }
                }

                public class Controller
                {
                    private Biz _biz = new Biz();
                    public void Post()
                    {
                        _biz.UpdateOrder();
                    }
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeCSharpSource("AppStack.cs", code);

        var dalSave = graph.Symbols.First(s => s.Name == "Save");
        var queries = new CallGraphQueries(graph);
        var callers = queries.GetDirectCallers(dalSave).ToList();

        // Assert
        callers.Should().ContainSingle(c => c.Name == "UpdateOrder");

        var impactAnalyzer = new ImpactAnalyzer(graph, queries);
        var impact = impactAnalyzer.AnalyzeSymbolImpact(dalSave);

        impact.Should().NotBeNull();
        impact.DirectCallers.Should().Contain(c => c.Name == "UpdateOrder");
        impact.IndirectCallers.Should().Contain(c => c.Symbol.Name == "Post");
    }
}
