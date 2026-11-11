using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpAdvancedSyntaxAndCallGraphExtendedTests
{
    /// <summary>
    /// Check that C# interfaces inherit from multiple base interfaces
    /// generate the corresponding INHERITS relations towards all parent interfaces.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractMultipleInterfaceInheritance()
    {
        // Arrange
        var code = """
            using System;

            namespace MyApp.Abstractions
            {
                public interface IReadable
                {
                    string Read();
                }

                public interface IWritable
                {
                    void Write(string text);
                }

                public interface IReadWriteStream : IReadable, IWritable, IDisposable
                {
                    void Flush();
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Streams.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var readable = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "IReadable");
        var writable = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "IWritable");
        var streamIface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "IReadWriteStream");

        readable.Should().NotBeNull();
        writable.Should().NotBeNull();
        streamIface.Should().NotBeNull();

        var inheritsRels = graph.Relationships.Where(r => r.Kind == RelationshipKind.Inherits && r.SourceId == streamIface!.Id).ToList();
        inheritsRels.Should().Contain(r => r.TargetId == readable!.Id);
        inheritsRels.Should().Contain(r => r.TargetId == writable!.Id);
    }

    /// <summary>
    /// Test extraction and tracking of explicit interface implementations ('void IDisposable.Dispose()').
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractExplicitInterfaceImplementations()
    {
        // Arrange
        var code = """
            using System;

            namespace MyApp.Services
            {
                public interface ICustomService
                {
                    void Run();
                }

                public class ServiceWorker : ICustomService, IDisposable
                {
                    void ICustomService.Run()
                    {
                        InternalStep();
                    }

                    void IDisposable.Dispose()
                    {
                        Cleanup();
                    }

                    private void InternalStep() { }
                    private void Cleanup() { }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Worker.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var workerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ServiceWorker");
        var customService = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "ICustomService");

        workerClass.Should().NotBeNull();
        customService.Should().NotBeNull();

        var implRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Implements && r.SourceId == workerClass!.Id && r.TargetId == customService!.Id);
        implRel.Should().NotBeNull();
    }

    /// <summary>
    /// Test extraction of static classes with extension methods (extension methods with 'this' parameter).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractExtensionMethodsAndStaticClasses()
    {
        // Arrange
        var code = """
            namespace MyApp.Extensions
            {
                public static class StringExtensions
                {
                    public static bool IsBlank(this string? value)
                    {
                        return string.IsNullOrWhiteSpace(value);
                    }

                    public static string Truncate(this string value, int maxLength)
                    {
                        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                            return value;
                        return value.Substring(0, maxLength);
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("StringExt.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var extClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "StringExtensions");
        extClass.Should().NotBeNull();
        extClass!.IsStatic.Should().BeTrue();

        var isBlankMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "IsBlank");
        isBlankMethod.Should().NotBeNull();
        isBlankMethod!.IsStatic.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the extraction of structs of type readonly record struct and related properties.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractReadonlyRecordStructs()
    {
        // Arrange
        var code = """
            namespace MyApp.Geometry
            {
                public readonly record struct Point2D(double X, double Y);

                public struct BoundingBox
                {
                    public Point2D TopLeft { get; set; }
                    public Point2D BottomRight { get; set; }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Geometry.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var pointStruct = graph.Symbols.FirstOrDefault(s => s.Name == "Point2D");
        pointStruct.Should().NotBeNull();

        var boxStruct = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Struct && s.Name == "BoundingBox");
        boxStruct.Should().NotBeNull();
    }

    /// <summary>
    /// Verify that static method calls between distinct classes are tracked correctly in the CallGraph.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldWireStaticMethodInvocationsAcrossClasses()
    {
        // Arrange
        var code = """
            namespace MyApp.Core
            {
                public static class MathUtility
                {
                    public static int Add(int a, int b) => a + b;
                }

                public class Calculator
                {
                    public int Compute()
                    {
                        return MathUtility.Add(10, 20);
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Calc.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var addMethod = graph.Symbols.FirstOrDefault(s => s.QualifiedName == "MyApp.Core.MathUtility.Add");
        var computeMethod = graph.Symbols.FirstOrDefault(s => s.QualifiedName == "MyApp.Core.Calculator.Compute");

        addMethod.Should().NotBeNull();
        computeMethod.Should().NotBeNull();

        var callRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == computeMethod!.Id && r.TargetId == addMethod!.Id);
        callRel.Should().NotBeNull("Calculator.Compute should have a CALLS relationship to MathUtility.Add");
    }
}
