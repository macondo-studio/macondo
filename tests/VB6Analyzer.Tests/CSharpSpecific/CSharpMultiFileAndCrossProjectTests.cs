using FluentAssertions;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpMultiFileAndCrossProjectTests
{
    /// <summary>
    /// Verifica che CallGraphBuilder risolva correttamente le relazioni di chiamata CALLS tra routine
    /// definite in file sorgente C# (.cs) distinti all'interno dello stesso grafo semantico.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldResolveCallsAcrossMultipleCSharpFiles()
    {
        // Arrange
        var file1 = """
            namespace MyApp.Core
            {
                public class OrderService
                {
                    private PaymentProcessor _processor = new PaymentProcessor();

                    public void PlaceOrder()
                    {
                        _processor.Process();
                    }
                }
            }
            """;

        var file2 = """
            namespace MyApp.Core
            {
                public class PaymentProcessor
                {
                    public void Process()
                    {
                        Audit();
                    }

                    private void Audit() { }
                }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeCSharpSources(
            ("OrderService.cs", file1),
            ("PaymentProcessor.cs", file2)
        );

        // Assert
        results.Should().HaveCount(2);

        var placeOrderMethod = graph.Symbols.First(s => s.Name == "PlaceOrder");
        var processMethod = graph.Symbols.First(s => s.Name == "Process");

        var crossFileCall = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == placeOrderMethod.Id && r.TargetId == processMethod.Id);
        crossFileCall.Should().NotBeNull("Method in OrderService.cs calling Process in PaymentProcessor.cs should be resolved in the unified Call Graph");
    }

    /// <summary>
    /// Verifica che le gerarchie di tipi (classi base astratte e interfacce) definite su file sorgente
    /// separati vengano collegate correttamente con relazioni INHERITS e IMPLEMENTS.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldResolveInheritanceAcrossMultipleFiles()
    {
        // Arrange
        var interfaceFile = """
            namespace MyApp.Contracts
            {
                public interface IRepository<T>
                {
                    T GetById(int id);
                }
            }
            """;

        var baseClassFile = """
            namespace MyApp.Data
            {
                public abstract class BaseRepository
                {
                    public void LogAccess() { }
                }
            }
            """;

        var implFile = """
            using MyApp.Contracts;
            using MyApp.Data;

            namespace MyApp.Implementations
            {
                public class SqlOrderRepository : BaseRepository, IRepository<string>
                {
                    public string GetById(int id)
                    {
                        LogAccess();
                        return "OrderData";
                    }
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeCSharpSources(
            ("IRepository.cs", interfaceFile),
            ("BaseRepository.cs", baseClassFile),
            ("SqlOrderRepository.cs", implFile)
        );

        // Assert
        var baseRepoClass = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "BaseRepository");
        var iface = graph.Symbols.First(s => s.Kind == SymbolKind.Interface && s.Name == "IRepository");
        var sqlRepoClass = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "SqlOrderRepository");

        var inheritsRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Inherits && r.SourceId == sqlRepoClass.Id && r.TargetId == baseRepoClass.Id);
        inheritsRel.Should().NotBeNull("SqlOrderRepository should INHERIT BaseRepository across files");

        var implementsRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Implements && r.SourceId == sqlRepoClass.Id && r.TargetId == iface.Id);
        implementsRel.Should().NotBeNull("SqlOrderRepository should IMPLEMENT IRepository across files");

        var getByIdMethod = graph.Symbols.First(s => s.Name == "GetById");
        var logAccessMethod = graph.Symbols.First(s => s.Name == "LogAccess");
        var callsLog = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == getByIdMethod.Id && r.TargetId == logAccessMethod.Id);
        callsLog.Should().NotBeNull("GetById calling base class method LogAccess across files should generate CALLS relationship");
    }

    /// <summary>
    /// Verifica che l'algoritmo di Impact Analysis calcoli l'effetto a catena (Blast Radius) propagandosi
    /// attraverso livelli applicativi distinti distribuiti su file separati (Controller -> Service -> Repository).
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldCalculateBlastRadiusAcrossMultipleCSharpFiles()
    {
        // Arrange: 3 files forming an architectural layer (Controller -> Service -> Repository)
        var repoFile = """
            namespace MyApp.Data
            {
                public class CustomerRepo
                {
                    public void DeleteCustomer(int id) { }
                }
            }
            """;

        var serviceFile = """
            using MyApp.Data;

            namespace MyApp.Services
            {
                public class CustomerService
                {
                    private CustomerRepo _repo = new CustomerRepo();

                    public void Remove(int id)
                    {
                        _repo.DeleteCustomer(id);
                    }
                }
            }
            """;

        var controllerFile = """
            using MyApp.Services;

            namespace MyApp.Controllers
            {
                public class CustomerController
                {
                    private CustomerService _service = new CustomerService();

                    public void DeleteAction(int id)
                    {
                        _service.Remove(id);
                    }
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeCSharpSources(
            ("CustomerRepo.cs", repoFile),
            ("CustomerService.cs", serviceFile),
            ("CustomerController.cs", controllerFile)
        );

        var deleteCustomerMethod = graph.Symbols.First(s => s.Name == "DeleteCustomer");
        var impactAnalyzer = new ImpactAnalyzer(graph);
        var impact = impactAnalyzer.AnalyzeSymbolImpact(deleteCustomerMethod);

        // Assert
        impact.DirectCallers.Should().ContainSingle(c => c.Name == "Remove");
        impact.IndirectCallers.Should().ContainSingle(c => c.Symbol.Name == "DeleteAction");
        impact.AffectedClasses.Select(c => c.Name).Should().Contain(new[] { "CustomerRepo", "CustomerService", "CustomerController" });
    }
}
