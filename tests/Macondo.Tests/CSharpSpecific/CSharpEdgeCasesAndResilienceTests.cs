using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpEdgeCasesAndResilienceTests
{
    /// <summary>
    /// Verifica l'estrazione di tipi dichiarati nel Global Namespace (senza clausola namespace esplicita),
    /// garantendo che il QualifiedName coincida con il nome semplice del tipo.
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
    /// Verifica la corretta gestione di molteplici blocchi 'namespace' all'interno del medesimo file sorgente.
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
    /// Verifica che file C# vuoti, composti da soli spazi o contenenti esclusivamente commenti e direttive using
    /// vengano elaborati senza errori restituendo una lista vuota di simboli.
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
    /// Verifica l'estrazione di molteplici overload del medesimo metodo all'interno della stessa classe.
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
