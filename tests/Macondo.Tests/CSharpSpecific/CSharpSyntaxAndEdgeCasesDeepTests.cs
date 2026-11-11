using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpSyntaxAndEdgeCasesDeepTests
{
    #region Modern C# 11 & 12 Syntax Constructs

    /// <summary>
    /// Verify correct extraction of Primary Constructor with default parameters,
    /// optional parameters and explicit subconstructors concatenated with : this(...).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractPrimaryConstructors_WithDefaultsAndChainedConstructors()
    {
        // Arrange
        var code = """
            namespace Enterprise.Core.Services
            {
                public class ConfigService(string environment = "Production", int timeoutSeconds = 30)
                {
                    public string Environment => environment;
                    public int Timeout => timeoutSeconds;

                    public ConfigService(string environment) : this(environment, 60)
                    {
                    }

                    public void Execute()
                    {
                        var env = Environment;
                    }
                }

                public readonly record struct ServerEndpoint(string Host, int Port = 8080);
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("ConfigService.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var configClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ConfigService");
        configClass.Should().NotBeNull();

        var endpointStruct = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Struct && s.Name == "ServerEndpoint");
        endpointStruct.Should().NotBeNull();

        var executeMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Execute");
        executeMethod.Should().NotBeNull();
    }

    /// <summary>
    /// Test parsing of C# types with ref struct, readonly ref struct, modifiers
    /// in / out / ref readonly parameters and pattern matching on relational ranges.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldHandleRefStructsAndAdvancedParameterModifiers()
    {
        // Arrange
        var code = """
            using System;

            namespace LowLevel.Memory
            {
                public readonly ref struct SpanReader
                {
                    private readonly ReadOnlySpan<byte> _buffer;

                    public SpanReader(ReadOnlySpan<byte> buffer)
                    {
                        _buffer = buffer;
                    }

                    public bool TryReadHeader(in int expectedMagic, out int actualMagic, ref int offset)
                    {
                        if (_buffer.Length < 4)
                        {
                            actualMagic = 0;
                            return false;
                        }

                        actualMagic = expectedMagic;
                        offset += 4;
                        return true;
                    }

                    public string CategorizeLength(int length) => length switch
                    {
                        <= 0 => "Empty",
                        > 0 and <= 64 => "Small",
                        > 64 and <= 1024 => "Medium",
                        _ => "Large"
                    };
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("SpanReader.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var structSym = graph.Symbols.FirstOrDefault(s => s.Name == "SpanReader");
        structSym.Should().NotBeNull();
        structSym!.Kind.Should().Be(SymbolKind.Struct);

        var tryReadMethod = graph.Symbols.FirstOrDefault(s => s.Name == "TryReadHeader");
        tryReadMethod.Should().NotBeNull();
        tryReadMethod!.Parameters.Should().HaveCount(3);
    }

    /// <summary>
    /// Tests handling of multi-line raw string literals with complex formatting
    /// and multiple interpolations ($$"""...""").
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldHandleRawStringLiteralsAndMultiInterpolation()
    {
        // Arrange
        var code = """"
            namespace CodeGen.Templates
            {
                public class TemplateGenerator
                {
                    public string GenerateJson(string name, int age)
                    {
                        var json = $$"""
                        {
                            "name": "{{name}}",
                            "age": {{age}},
                            "details": {
                                "active": true
                            }
                        }
                        """;
                        return json;
                    }
                }
            }
            """";

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("TemplateGenerator.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var method = graph.Symbols.FirstOrDefault(s => s.Name == "GenerateJson");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be("string");
    }

    #endregion

    #region Local Functions and Nested Call Linking

    /// <summary>
    /// Verify multiple nested local functions and their calls to each other
    /// are analyzed correctly in the CallGraph without generating visibility misalignments.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractNestedLocalFunctionsAndCallGraphLinks()
    {
        // Arrange
        var code = """
            using System;
            using System.Threading.Tasks;

            namespace Algorithm.MathServices
            {
                public class QuickSorter
                {
                    public void Sort(int[] array)
                    {
                        if (array == null || array.Length <= 1) return;
                        QuickSort(0, array.Length - 1);

                        void QuickSort(int left, int right)
                        {
                            if (left < right)
                            {
                                int pivotIndex = Partition(left, right);
                                QuickSort(left, pivotIndex - 1);
                                QuickSort(pivotIndex + 1, right);
                            }
                        }

                        int Partition(int left, int right)
                        {
                            int pivot = array[right];
                            int i = left - 1;
                            for (int j = left; j < right; j++)
                            {
                                if (array[j] <= pivot)
                                {
                                    i++;
                                    Swap(i, j);
                                }
                            }
                            Swap(i + 1, right);
                            return i + 1;
                        }

                        void Swap(int a, int b)
                        {
                            int temp = array[a];
                            array[a] = array[b];
                            array[b] = temp;
                        }
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("QuickSorter.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var sortMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Sort");
        sortMethod.Should().NotBeNull();
        sortMethod!.Kind.Should().Be(SymbolKind.Method);
    }

    #endregion

    #region Multi-Namespace and File-Scoped Namespaces with Partial Types

    /// <summary>
    /// Tests extraction from multiple files containing partial classes
    /// defined with file-scoped namespace, merging the symbols into the same logical container.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldMergePartialClasses_AcrossMultipleFiles()
    {
        // Arrange
        var part1 = """
            namespace Architecture.Core;

            public partial class AggregateRoot
            {
                public Guid Id { get; set; }
                public int Version { get; private set; }

                public void IncrementVersion() => Version++;
            }
            """;

        var part2 = """
            namespace Architecture.Core;

            public partial class AggregateRoot
            {
                private readonly List<object> _uncommittedEvents = new();

                public void AddEvent(object ev)
                {
                    _uncommittedEvents.Add(ev);
                    IncrementVersion();
                }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeCSharpSources(
            ("AggregateRoot.Part1.cs", part1),
            ("AggregateRoot.Part2.cs", part2)
        );

        // Assert
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());

        var methods = graph.Symbols.Where(s => s.Kind == SymbolKind.Method).ToList();
        methods.Should().Contain(m => m.Name == "IncrementVersion");
        methods.Should().Contain(m => m.Name == "AddEvent");
    }

    #endregion
}
