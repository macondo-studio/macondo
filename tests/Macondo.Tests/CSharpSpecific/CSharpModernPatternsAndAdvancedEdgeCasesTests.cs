using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpModernPatternsAndAdvancedEdgeCasesTests
{
    /// <summary>
    /// Check the extraction of classes and methods that use list patterns and combinatorial patterns ('and', 'or', 'not').
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractMethodsUsingListAndCombinatorPatterns()
    {
        var code = """
            namespace MyApp.Patterns;

            public class SequenceMatcher
            {
                public bool MatchHeader(byte[] bytes) => bytes is [0xFF, 0xD8, ..];

                public bool IsValidStatus(int code) => code is (>= 200 and <= 299) or (>= 300 and <= 399);

                public bool IsNotNullNorEmpty(string? s) => s is not null and not "";
            }
            """;

        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("SequenceMatcher.cs", code);

        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var cls = graph.Symbols.FirstOrDefault(s => s.Name == "SequenceMatcher");
        cls.Should().NotBeNull();
        cls!.Kind.Should().Be(SymbolKind.Class);

        var methods = graph.GetChildren(cls.Id).ToList();
        methods.Should().HaveCount(3);
    }

    /// <summary>
    /// Test parsing of multi-line raw string literals with interpolation and special characters.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldParseRawStringLiteralsAndNestedInterpolations()
    {
        var code = """"
            namespace MyApp.Templating;

            public class TemplateEngine
            {
                public string GenerateJson(string id, string val) =>
                    $$"""
                    {
                        "id": "{{id}}",
                        "value": "{{val}}",
                        "nested": {
                            "valid": true
                        }
                    }
                    """;
            }
            """";

        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("TemplateEngine.cs", code);

        result.Success.Should().BeTrue();
        var cls = graph.Symbols.FirstOrDefault(s => s.Name == "TemplateEngine");
        cls.Should().NotBeNull();
    }

    /// <summary>
    /// Tests the extraction of generic types with recursive constraints (CRTP - Curiously Recurring Template Pattern).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractRecursiveGenericsAndConstraints()
    {
        var code = """
            namespace MyApp.Generics;

            public interface IComparableEntity<TSelf> where TSelf : IComparableEntity<TSelf>
            {
                int CompareTo(TSelf other);
            }

            public class BaseEntity<T> : IComparableEntity<BaseEntity<T>> where T : class, new()
            {
                public int CompareTo(BaseEntity<T> other) => 0;
            }
            """;

        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Generics.cs", code);

        result.Success.Should().BeTrue();
        var iface = graph.Symbols.FirstOrDefault(s => s.Name.StartsWith("IComparableEntity"));
        var cls = graph.Symbols.FirstOrDefault(s => s.Name.StartsWith("BaseEntity"));

        iface.Should().NotBeNull();
        cls.Should().NotBeNull();
    }

    /// <summary>
    /// Test class extraction with primary constructors (C# 12+).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractClassWithPrimaryConstructor()
    {
        var code = """
            namespace MyApp.Services;

            public class UserService(string connectionString, int timeoutSeconds)
            {
                public string ConnectionString => connectionString;
                public int TimeoutSeconds => timeoutSeconds;

                public void Connect() { }
            }
            """;

        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("UserService.cs", code);

        result.Success.Should().BeTrue();
        var cls = graph.Symbols.FirstOrDefault(s => s.Name == "UserService");
        cls.Should().NotBeNull();
        cls!.Kind.Should().Be(SymbolKind.Class);
    }
}
