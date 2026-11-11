using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpEdgeCasesAndResilienceTests
{
    /// <summary>
    /// Tests extraction of types declared in the Global Namespace (without explicit namespace clause),
    /// ensuring that the QualifiedName matches the simple name of the type.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractTypesInGlobalNamespace()
    {
        // Arrange
        var code = """
            public class TopLevelProgram
            {
                public static void Main(string[] args)
                {
                    DoSomething();
                }

                private static void DoSomething() { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Program.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var topLevelClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "TopLevelProgram");
        topLevelClass.Should().NotBeNull();
        topLevelClass!.QualifiedName.Should().Be("TopLevelProgram");

        var mainMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Main");
        mainMethod.Should().NotBeNull();
        mainMethod!.QualifiedName.Should().Be("TopLevelProgram.Main");
    }

    /// <summary>
    /// Verifies the correct management of multiple 'namespace' blocks within the same source file.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldSupportMultipleNamespacesInSingleFile()
    {
        // Arrange
        var code = """
            namespace FirstNamespace
            {
                public class Alpha
                {
                    public void MethodA() { }
                }
            }

            namespace SecondNamespace
            {
                public class Beta
                {
                    public void MethodB() { }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("MultiNs.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var alphaClass = graph.Symbols.First(s => s.Name == "Alpha");
        alphaClass.QualifiedName.Should().Be("FirstNamespace.Alpha");

        var betaClass = graph.Symbols.First(s => s.Name == "Beta");
        betaClass.QualifiedName.Should().Be("SecondNamespace.Beta");
    }

    /// <summary>
    /// Check for C# files that are empty, contain only spaces, or contain only comments and using directives
    /// are processed without errors, returning an empty list of symbols.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldHandleEmptyAndCommentOnlyFilesGracefully()
    {
        // Arrange
        var emptyCode = "";
        var commentsOnly = """
            // This is a header comment
            /* Multi-line comment block
               with no classes */
            using System;
            using System.Collections.Generic;
            """;

        // Act
        var (graph1, result1) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Empty.cs", emptyCode);
        var (graph2, result2) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Comments.cs", commentsOnly);

        // Assert
        result1.Success.Should().BeTrue();
        result1.TopLevelSymbols.Should().BeEmpty();

        result2.Success.Should().BeTrue();
        result2.TopLevelSymbols.Should().BeEmpty();
    }

    /// <summary>
    /// Checks the extraction of multiple overloads of the same method within the same class.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractOverloadedMethods()
    {
        // Arrange
        var code = """
            namespace MyApp.Overloads
            {
                public class Dispatcher
                {
                    public void Send(string message) { }
                    public void Send(string message, int priority) { }
                    public void Send(byte[] data) { }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Dispatcher.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var sendMethods = graph.Symbols.Where(s => s.Kind == SymbolKind.Method && s.Name == "Send").ToList();
        sendMethods.Should().HaveCount(3);
    }
}
