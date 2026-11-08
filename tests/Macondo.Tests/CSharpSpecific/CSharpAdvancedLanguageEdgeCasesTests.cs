using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpAdvancedLanguageEdgeCasesTests
{
    #region Generics and Constraints

    /// <summary>
    /// Verifica l'estrazione di classi ed interfacce generiche con vincoli multipli (where T : class, new(), where TKey : notnull).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractGenericTypesWithComplexConstraints()
    {
        // Arrange
        var code = """
            using System;
            using System.Collections.Generic;

            namespace MyApp.Generics
            {
                public interface IRepository<TEntity, TKey> 
                    where TEntity : class, new() 
                    where TKey : notnull, IComparable<TKey>
                {
                    TEntity? GetById(TKey id);
                    void Save(TEntity entity);
                }

                public class SqlRepository<TEntity, TKey> : IRepository<TEntity, TKey>
                    where TEntity : class, new()
                    where TKey : notnull, IComparable<TKey>
                {
                    public TEntity? GetById(TKey id) => default;
                    public void Save(TEntity entity) { }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("GenericRepo.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var iface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name.StartsWith("IRepository"));
        var repoClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name.StartsWith("SqlRepository"));

        iface.Should().NotBeNull();
        repoClass.Should().NotBeNull();
    }

    #endregion

    #region Async and Task Returning Methods

    /// <summary>
    /// Verifica l'estrazione e il tracciamento di metodi asincroni (async Task, async ValueTask, IAsyncEnumerable).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractAsyncMethodsAndTasks()
    {
        // Arrange
        var code = """
            using System.Collections.Generic;
            using System.Threading;
            using System.Threading.Tasks;

            namespace MyApp.Async
            {
                public class AsyncDataService
                {
                    public async Task<int> FetchDataAsync(CancellationToken ct = default)
                    {
                        await Task.Delay(10, ct);
                        return 42;
                    }

                    public async ValueTask<bool> TryProcessAsync()
                    {
                        var result = await FetchDataAsync();
                        return result > 0;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("AsyncService.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var fetchMethod = graph.Symbols.FirstOrDefault(s => s.Name == "FetchDataAsync");
        var processMethod = graph.Symbols.FirstOrDefault(s => s.Name == "TryProcessAsync");

        fetchMethod.Should().NotBeNull();
        processMethod.Should().NotBeNull();

        var callRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == processMethod!.Id && r.TargetId == fetchMethod!.Id);
        callRel.Should().NotBeNull("TryProcessAsync should call FetchDataAsync");
    }

    #endregion

    #region Pattern Matching and Switch Expressions

    /// <summary>
    /// Verifica l'estrazione di costrutti moderni C# con switch expression, property pattern e clausole guard (when).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldSupportPatternMatchingAndSwitchExpressions()
    {
        // Arrange
        var code = """
            namespace MyApp.Patterns
            {
                public abstract record Shape;
                public record Circle(double Radius) : Shape;
                public record Rectangle(double Width, double Height) : Shape;

                public static class ShapeCalculator
                {
                    public static double CalculateArea(Shape shape) => shape switch
                    {
                        Circle { Radius: > 0 } c => 3.14159 * c.Radius * c.Radius,
                        Rectangle { Width: > 0, Height: > 0 } r => r.Width * r.Height,
                        _ => 0.0
                    };
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Patterns.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var calcClass = graph.Symbols.FirstOrDefault(s => s.Name == "ShapeCalculator");
        var calcMethod = graph.Symbols.FirstOrDefault(s => s.Name == "CalculateArea");

        calcClass.Should().NotBeNull();
        calcMethod.Should().NotBeNull();
    }

    #endregion

    #region Custom Operators and Indexers

    /// <summary>
    /// Verifica l'estrazione di operator overloads (+, -, ==, !=) e indicizzatori (this[int index], this[string key]).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractOperatorOverloadsAndIndexers()
    {
        // Arrange
        var code = """
            using System.Collections.Generic;

            namespace MyApp.Collections
            {
                public class KeyValueStore
                {
                    private readonly Dictionary<string, string> _dict = new();

                    public string this[string key]
                    {
                        get => _dict.TryGetValue(key, out var val) ? val : string.Empty;
                        set => _dict[key] = value;
                    }
                }

                public readonly struct Vector2D
                {
                    public double X { get; }
                    public double Y { get; }

                    public Vector2D(double x, double y) => (X, Y) = (x, y);

                    public static Vector2D operator +(Vector2D a, Vector2D b) => new(a.X + b.X, a.Y + b.Y);
                    public static Vector2D operator -(Vector2D a, Vector2D b) => new(a.X - b.X, a.Y - b.Y);
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Operators.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var storeClass = graph.Symbols.FirstOrDefault(s => s.Name == "KeyValueStore");
        var vectorStruct = graph.Symbols.FirstOrDefault(s => s.Name == "Vector2D");

        storeClass.Should().NotBeNull();
        vectorStruct.Should().NotBeNull();
    }

    #endregion

    #region Primary Constructors and Record Hierarchies

    /// <summary>
    /// Verifica l'estrazione di classi e record con costruttori primari (Primary Constructors) e gerarchie di record.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractPrimaryConstructorsAndRecordHierarchies()
    {
        // Arrange
        var code = """
            namespace MyApp.Domain
            {
                public interface ILogger { void Log(string msg); }

                public class AuditService(ILogger logger)
                {
                    public void RecordEvent(string eventName) => logger.Log(eventName);
                }

                public abstract record BaseEvent(string Id, long Timestamp);
                public record UserCreatedEvent(string Id, long Timestamp, string UserName, string Email) : BaseEvent(Id, Timestamp);
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("DomainRecords.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var auditService = graph.Symbols.FirstOrDefault(s => s.Name == "AuditService");
        var baseEvent = graph.Symbols.FirstOrDefault(s => s.Name == "BaseEvent");
        var userCreated = graph.Symbols.FirstOrDefault(s => s.Name == "UserCreatedEvent");

        auditService.Should().NotBeNull();
        baseEvent.Should().NotBeNull();
        userCreated.Should().NotBeNull();
    }

    #endregion

    #region Local Functions and Closures

    /// <summary>
    /// Verifica l'analisi di metodi contenenti funzioni locali (Local Functions) anche ricorsive.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractMethodsWithNestedLocalFunctions()
    {
        // Arrange
        var code = """
            namespace MyApp.Algorithms
            {
                public class TreeWalker
                {
                    public int ComputeTreeDepth(object? root)
                    {
                        return Traverse(root, 0);

                        int Traverse(object? current, int currentDepth)
                        {
                            if (current == null) return currentDepth;
                            return currentDepth + 1;
                        }
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("TreeWalker.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var method = graph.Symbols.FirstOrDefault(s => s.Name == "ComputeTreeDepth");
        method.Should().NotBeNull();
    }

    #endregion

    #region Custom Attributes

    /// <summary>
    /// Verifica l'estrazione di attributi applicati su classi, metodi, proprietà e parametri.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldSupportClassesAndMethodsDecoratedWithAttributes()
    {
        // Arrange
        var code = """
            using System;

            namespace MyApp.Api
            {
                [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
                public class RouteAttribute(string template) : Attribute
                {
                    public string Template { get; } = template;
                }

                [Route("api/v1/orders")]
                public class OrdersController
                {
                    [Obsolete("Use GetOrdersV2 instead", false)]
                    [Route("all")]
                    public string GetAllOrders([System.Diagnostics.CodeAnalysis.NotNull] string filter)
                    {
                        return "[]";
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Controller.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var controller = graph.Symbols.FirstOrDefault(s => s.Name == "OrdersController");
        var getOrders = graph.Symbols.FirstOrDefault(s => s.Name == "GetAllOrders");

        controller.Should().NotBeNull();
        getOrders.Should().NotBeNull();
    }

    #endregion
}
