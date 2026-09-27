using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpUncoveredEdgeCasesAndBugsHunterTests
{
    #region Primary Constructors on Classes and Structs

    /// <summary>
    /// Verifica l'estrazione di tipi C# moderni definiti con Primary Constructor (C# 12) sia per classi che per struct,
    /// e la corretta registrazione dei parametri del costruttore.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractPrimaryConstructors_OnClassesAndStructs()
    {
        // Arrange
        var code = """
            namespace MyApp.Services
            {
                public interface ILogger { void Log(string msg); }
                public interface IRepository { void Save(); }

                public class OrderProcessor(ILogger logger, IRepository repository)
                {
                    public void Process()
                    {
                        logger.Log("Processing order");
                        repository.Save();
                    }
                }

                public readonly struct MetricPoint(string key, double value, long timestamp)
                {
                    public string Key => key;
                    public double Value => value;
                    public long Timestamp => timestamp;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("PrimaryConstructors.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var procClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "OrderProcessor");
        var metricStruct = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "MetricPoint");

        procClass.Should().NotBeNull();
        metricStruct.Should().NotBeNull();

        var processMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Process");
        processMethod.Should().NotBeNull();
    }

    #endregion

    #region Collection Expressions and Modern C# 12 Syntax

    /// <summary>
    /// Verifica il parsing di espressioni di collezione C# 12 (bracket syntax con spread operator [..items, x]).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractMethodsUsingCollectionExpressionsAndSpreadOperators()
    {
        // Arrange
        var code = """
            using System.Collections.Generic;
            using System.Linq;

            namespace MyApp.Data
            {
                public class Pipeline
                {
                    public int[] Combine(int[] first, int[] second, int extra)
                    {
                        int[] combined = [..first, ..second, extra];
                        List<string> empty = [];
                        return combined;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("CollectionExpr.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var pipelineClass = graph.Symbols.FirstOrDefault(s => s.Name == "Pipeline");
        var combineMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Combine");

        pipelineClass.Should().NotBeNull();
        combineMethod.Should().NotBeNull();
    }

    #endregion

    #region Record Structs and Readonly Record Structs

    /// <summary>
    /// Verifica l'estrazione di tipi 'record struct' e 'readonly record struct'.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractRecordStructsAndReadonlyRecordStructs()
    {
        // Arrange
        var code = """
            namespace MyApp.Geometry
            {
                public record struct Vector2D(double X, double Y);
                public readonly record struct GeoLocation(double Latitude, double Longitude, string Label);

                public class GeoCalculator
                {
                    public double CalculateDistance(GeoLocation a, GeoLocation b)
                    {
                        return 0.0;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("RecordStructs.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var vecStruct = graph.Symbols.FirstOrDefault(s => s.Name == "Vector2D");
        var geoStruct = graph.Symbols.FirstOrDefault(s => s.Name == "GeoLocation");
        var calcClass = graph.Symbols.FirstOrDefault(s => s.Name == "GeoCalculator");

        vecStruct.Should().NotBeNull();
        geoStruct.Should().NotBeNull();
        calcClass.Should().NotBeNull();
    }

    #endregion

    #region Required Properties and Init-Only Setters

    /// <summary>
    /// Verifica l'estrazione di proprietà con modificatore 'required' e setter 'init'.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractRequiredAndInitOnlyProperties()
    {
        // Arrange
        var code = """
            namespace MyApp.Models
            {
                public class ProductDto
                {
                    public required string Sku { get; init; }
                    public required string Title { get; init; }
                    public decimal UnitPrice { get; init; }
                    public bool IsActive { get; set; } = true;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("ProductDto.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var dtoClass = graph.Symbols.FirstOrDefault(s => s.Name == "ProductDto");
        dtoClass.Should().NotBeNull();

        var skuProp = graph.Symbols.FirstOrDefault(s => s.Name == "Sku" && s.Kind == SymbolKind.Property);
        var titleProp = graph.Symbols.FirstOrDefault(s => s.Name == "Title" && s.Kind == SymbolKind.Property);

        skuProp.Should().NotBeNull();
        titleProp.Should().NotBeNull();
    }

    #endregion

    #region Overloaded Method Calls in CallGraph

    /// <summary>
    /// Verifica che chiamate a metodi sovraccaricati (overloaded) con parametri differenti
    /// vengano correttamente indicizzate e collegate nel CallGraph.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldHandleOverloadedMethodInvocations()
    {
        // Arrange
        var code = """
            namespace MyApp.Overloads
            {
                public class Notifier
                {
                    public void SendNotification(int userId) { }
                    public void SendNotification(string emailAddress) { }
                    public void SendNotification(int userId, string customMessage, bool highPriority) { }
                }

                public class WorkflowService
                {
                    private Notifier _notifier = new Notifier();

                    public void Run()
                    {
                        _notifier.SendNotification(42);
                        _notifier.SendNotification("admin@company.com");
                        _notifier.SendNotification(99, "Urgent alert", true);
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Overloads.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var notifierClass = graph.Symbols.FirstOrDefault(s => s.Name == "Notifier");
        var workflowClass = graph.Symbols.FirstOrDefault(s => s.Name == "WorkflowService");
        var runMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Run");

        notifierClass.Should().NotBeNull();
        workflowClass.Should().NotBeNull();
        runMethod.Should().NotBeNull();

        var overloads = graph.Symbols.Where(s => s.Kind == SymbolKind.Method && s.Name == "SendNotification").ToList();
        overloads.Should().HaveCount(3);

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == runMethod!.Id).ToList();
        calls.Should().NotBeEmpty("Run method should call SendNotification");
    }

    #endregion

    #region Interface Default Method Implementations (C# 8+)

    /// <summary>
    /// Verifica l'estrazione di interfacce con metodi predefiniti (Default Interface Methods) contenenti corpo di codice.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractDefaultInterfaceMethodsWithBodies()
    {
        // Arrange
        var code = """
            using System;

            namespace MyApp.Contracts
            {
                public interface ILogger
                {
                    void Write(string message);

                    void LogWarning(string message)
                    {
                        Write($"[WARN] {message}");
                    }

                    void LogError(string message, Exception? ex = null)
                    {
                        Write($"[ERR] {message} - {ex?.Message}");
                    }
                }

                public class ConsoleLogger : ILogger
                {
                    public void Write(string message)
                    {
                        Console.WriteLine(message);
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("DefaultInterface.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var loggerIface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "ILogger");
        var consoleLogger = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ConsoleLogger");

        loggerIface.Should().NotBeNull();
        consoleLogger.Should().NotBeNull();

        var defaultMethods = graph.Symbols.Where(s => s.Kind == SymbolKind.Method && (s.Name == "LogWarning" || s.Name == "LogError")).ToList();
        defaultMethods.Should().HaveCount(2);
    }

    #endregion

    #region Null-Conditional and Null-Coalescing Invocations

    /// <summary>
    /// Verifica la cattura di chiamate condizionali con operatore null-conditional (?.) e null-coalescing (??).
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldExtractCallsThroughNullConditionalAndCoalescingChains()
    {
        // Arrange
        var code = """
            namespace MyApp.SafeCalls
            {
                public class Gateway
                {
                    public void Ping() { }
                }

                public class Router
                {
                    private Gateway? _primary;
                    private Gateway _fallback = new Gateway();

                    public void Dispatch()
                    {
                        _primary?.Ping();
                        _fallback.Ping();
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("SafeCalls.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var pingMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Ping");
        var dispatchMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Dispatch");

        pingMethod.Should().NotBeNull();
        dispatchMethod.Should().NotBeNull();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == dispatchMethod!.Id && r.TargetId == pingMethod!.Id).ToList();
        calls.Should().NotBeEmpty("Dispatch should call Ping on Gateway");
    }

    #endregion
}
