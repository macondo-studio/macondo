using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpModernLanguageAndEdgeCasesTests
{
    #region File-Scoped Namespaces (C# 10+)

    /// <summary>
    /// Check extraction of classes and interfaces declared with file-scoped namespaces ('namespace MyApp.Services;').
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractSymbols_WithFileScopedNamespace()
    {
        // Arrange
        var code = """
            namespace MyApp.FileScoped;

            public interface ITokenProvider
            {
                string GetToken();
            }

            public class JwtTokenProvider : ITokenProvider
            {
                public string GetToken() => "jwt-sample";
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("FileScoped.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var iface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "ITokenProvider");
        var cls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "JwtTokenProvider");

        iface.Should().NotBeNull();
        cls.Should().NotBeNull();
        iface!.QualifiedName.Should().Be("MyApp.FileScoped.ITokenProvider");
        cls!.QualifiedName.Should().Be("MyApp.FileScoped.JwtTokenProvider");
    }

    #endregion

    #region Pattern Matching and Switch Expressions

    /// <summary>
    /// Test parsing of switch expressions with relational patterns ('> 0 and <= 100') and pattern matching on types.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractMethodsUsingSwitchExpressionsAndRelationalPatterns()
    {
        // Arrange
        var code = """
            namespace MyApp.Patterns
            {
                public class Classifier
                {
                    public string ClassifyScore(int score) => score switch
                    {
                        < 0 => "Invalid",
                        >= 0 and < 60 => "Fail",
                        >= 60 and < 80 => "Pass",
                        >= 80 and <= 100 => "Excellent",
                        _ => "Out of Range"
                    };

                    public string DescribeShape(object shape) => shape switch
                    {
                        string s when s.Length > 0 => $"Text: {s}",
                        int n => $"Number: {n}",
                        null => "Null",
                        _ => "Other"
                    };
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Patterns.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var cls = graph.Symbols.FirstOrDefault(s => s.Name == "Classifier");
        var mthScore = graph.Symbols.FirstOrDefault(s => s.Name == "ClassifyScore");
        var mthShape = graph.Symbols.FirstOrDefault(s => s.Name == "DescribeShape");

        cls.Should().NotBeNull();
        mthScore.Should().NotBeNull();
        mthShape.Should().NotBeNull();
    }

    #endregion

    #region Extension Methods

    /// <summary>
    /// Tests the extraction of extension methods defined in static classes.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractExtensionMethods_InStaticClasses()
    {
        // Arrange
        var code = """
            namespace MyApp.Extensions
            {
                public static class StringExtensions
                {
                    public static bool IsNullOrEmpty(this string? value)
                    {
                        return string.IsNullOrEmpty(value);
                    }

                    public static string Truncate(this string value, int maxLength)
                    {
                        if (value.Length <= maxLength) return value;
                        return value.Substring(0, maxLength);
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("StringExtensions.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var extClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "StringExtensions");
        extClass.Should().NotBeNull();
        extClass!.IsStatic.Should().BeTrue();

        var mthTruncate = graph.Symbols.FirstOrDefault(s => s.Name == "Truncate");
        mthTruncate.Should().NotBeNull();
        mthTruncate!.IsStatic.Should().BeTrue();
        mthTruncate.Parameters.Should().HaveCount(2);
        mthTruncate.Parameters[0].Name.Should().Be("value");
        mthTruncate.Parameters[0].DeclaredTypeName.Should().Be("string");
    }

    #endregion

    #region Generic Constraints and Static Abstract Members (C# 11+)

    /// <summary>
    /// Test extraction of multiple generic constraints ('where T : class, new()', 'where T : unmanaged')
    /// and abstract static members in interfaces.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractGenericConstraints_AndStaticAbstractMembers()
    {
        // Arrange
        var code = """
            namespace MyApp.GenericsAdvanced
            {
                public interface IFactory<TSelf> where TSelf : IFactory<TSelf>
                {
                    static abstract TSelf CreateDefault();
                }

                public class Container<TItem> where TItem : class, new()
                {
                    public TItem CreateItem() => new TItem();
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("GenericsAdv.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var iface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name.StartsWith("IFactory"));
        var containerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name.StartsWith("Container"));

        iface.Should().NotBeNull();
        containerClass.Should().NotBeNull();
    }

    #endregion

    #region Raw String Literals and Index/Range Expressions

    /// <summary>
    /// Tests parsing of multiline raw string literals with multiple double quotes and slicing with indices from end of array (^1).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractRawStringLiteralsAndIndexRangeExpressions()
    {
        // Arrange
        var code = """"
            namespace MyApp.ModernSyntax
            {
                public class JsonHelper
                {
                    public string GetSampleJson()
                    {
                        return """
                            {
                                "id": 100,
                                "name": "Antigravity",
                                "roles": ["Admin", "Developer"]
                            }
                            """;
                    }

                    public int[] SliceArray(int[] source)
                    {
                        return source[1..^1];
                    }
                }
            }
            """";

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("ModernSyntax.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var helperClass = graph.Symbols.FirstOrDefault(s => s.Name == "JsonHelper");
        var jsonMethod = graph.Symbols.FirstOrDefault(s => s.Name == "GetSampleJson");
        var sliceMethod = graph.Symbols.FirstOrDefault(s => s.Name == "SliceArray");

        helperClass.Should().NotBeNull();
        jsonMethod.Should().NotBeNull();
        sliceMethod.Should().NotBeNull();
    }

    #endregion
}
