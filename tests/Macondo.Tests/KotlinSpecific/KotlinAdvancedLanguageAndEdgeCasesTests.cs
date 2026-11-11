using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.KotlinSpecific;

public class KotlinAdvancedLanguageAndEdgeCasesTests
{
    [Fact]
    public void KotlinParser_ShouldHandleNestedBlockComments()
    {
        // Arrange: In Kotlin block comments can be nested
        var code = """
            package com.acme.comments

            /*
               Commento esterno
               /* Commento interno annidato */
               Ancora nel commento esterno
            */
            class CommentTest {
                fun test() { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Comments.kt", code);

        // Assert
        result.Success.Should().BeTrue();
        var cls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "CommentTest");
        cls.Should().NotBeNull();
        var m = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "test");
        m.Should().NotBeNull();
    }

    [Fact]
    public void KotlinParser_ShouldHandleBacktickIdentifiers()
    {
        // Arrange: In Kotlin identifiers can contain spaces or keywords between backticks
        var code = """
            package com.acme.test

            class CalculationTests {
                fun `calculate total with discount and tax 2026`() {
                    val `when` = 1
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("BacktickTests.kt", code);

        // Assert
        result.Success.Should().BeTrue();
        var m = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "calculate total with discount and tax 2026");
        m.Should().NotBeNull();
    }

    [Fact]
    public void KotlinParser_ShouldHandleSingleExpressionFunctionsAndDelegatedProperties()
    {
        // Arrange
        var code = """
            package com.acme.advanced

            class HeavyResource {
                fun execute() { }
            }

            class LazyConsumer {
                val resource: HeavyResource by lazy {
                    HeavyResource()
                }

                fun compute(x: Int): Int = x * 2

                fun run() {
                    resource.execute()
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Advanced.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var consumer = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "LazyConsumer");
        consumer.Should().NotBeNull();

        var prop = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "resource");
        prop.Should().NotBeNull();

        var compute = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "compute");
        compute.Should().NotBeNull();

        var run = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "run");
        run.Should().NotBeNull();

        var exec = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "execute");
        exec.Should().NotBeNull();

        var calls = graph.GetOutgoingRelationships(run!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        calls.Should().Contain(r => r.TargetId == exec!.Id);
    }
}
