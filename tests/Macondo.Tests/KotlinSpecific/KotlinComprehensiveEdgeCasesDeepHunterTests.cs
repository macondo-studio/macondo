using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.KotlinSpecific;

/// <summary>
/// Specialized test suite for deep edge cases of the Kotlin compiler and parser (.kt):
/// generics with multiple bounds ('where'), inline functions with type parameter 'reified',
/// resolving multiple inheritance conflicts via 'super<T>', tailrec and local (nested) functions,
/// classi 'inner' con qualified this ('this@Outer'), string templates complessi e sovraccarico operatori (get, set, invoke).
/// </summary>
public class KotlinComprehensiveEdgeCasesDeepHunterTests
{
    #region Generics with Multiple Bounds ('where' clause)

    /// <summary>
    /// Test parsing and extraction of advanced generic constructs with multi-constraint 'where' clause.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldHandleGenericsWithMultipleBounds()
    {
        // Arrange
        var code = """
            package com.acme.generics

            import java.io.Serializable

            class DataSorter {
                fun <T> sortAndFilter(items: List<T>, threshold: T): List<T>
                    where T : Comparable<T>, T : Serializable {
                    return items.filter { it > threshold }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("DataSorter.kt", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var sorterCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "DataSorter");
        sorterCls.Should().NotBeNull();

        var sortMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "sortAndFilter");
        sortMethod.Should().NotBeNull();
    }

    #endregion

    #region Inline Functions and Reified Type Parameters

    /// <summary>
    /// Check that inline functions with reified type parameters ('inline fun <reified T> findByType()')
    /// are analyzed and extracted regularly.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldHandleInlineFunctionsAndReifiedTypes()
    {
        // Arrange
        var code = """
            package com.acme.di

            class ServiceContainer {
                val registry = mutableMapOf<String, Any>()

                fun register(name: String, instance: Any) {
                    registry[name] = instance
                }

                inline fun <reified T : Any> resolveService(): T? {
                    val key = T::class.java.name
                    return registry[key] as? T
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Container.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var containerCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ServiceContainer");
        containerCls.Should().NotBeNull();

        var resolveMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "resolveService");
        resolveMethod.Should().NotBeNull();
    }

    #endregion

    #region Multiple Interface Implementation and Super Disambiguation

    /// <summary>
    /// In case of default methods in multiple interfaces with the same signature, Kotlin requires disambiguation
    /// explicit via 'super<NomeInterfaccia>.method()'. Verify that the parser extracts and connects the calls.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldHandleMultipleInterfacesAndSuperDisambiguation()
    {
        // Arrange
        var code = """
            package com.acme.polymorphism

            interface Printable {
                fun render(): String = "[Printable]"
            }

            interface Displayable {
                fun render(): String = "[Displayable]"
            }

            class MultiView : Printable, Displayable {
                override fun render(): String {
                    val p = super<Printable>.render()
                    val d = super<Displayable>.render()
                    return "$p - $d"
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("MultiView.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var printableIface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "Printable");
        printableIface.Should().NotBeNull();

        var displayableIface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "Displayable");
        displayableIface.Should().NotBeNull();

        var multiViewCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "MultiView");
        multiViewCls.Should().NotBeNull();

        var renderMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "render" && s.QualifiedName.Contains("MultiView"));
        renderMethod.Should().NotBeNull();

        var printableInherits = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == multiViewCls!.Id &&
            r.TargetId == printableIface!.Id);
        printableInherits.Should().NotBeNull();

        var displayableInherits = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == multiViewCls!.Id &&
            r.TargetId == displayableIface!.Id);
        displayableInherits.Should().NotBeNull();
    }

    #endregion

    #region Tailrec and Local (Nested) Functions

    /// <summary>
    /// Tests the handling of tailrec (tailed recursion) functions and defined local functions
    /// within the body of another function.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldHandleTailrecAndLocalFunctions()
    {
        // Arrange
        var code = """
            package com.acme.recursion

            class MathService {
                tailrec fun gcd(a: Long, b: Long): Long {
                    return if (b == 0L) a else gcd(b, a % b)
                }

                fun calculateComposite(values: List<Long>): Long {
                    fun doubleValue(v: Long): Long = v * 2

                    var acc = 0L
                    for (x in values) {
                        acc += doubleValue(x)
                    }
                    return acc
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("MathService.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var mathCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "MathService");
        mathCls.Should().NotBeNull();

        var gcdMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "gcd");
        gcdMethod.Should().NotBeNull();

        var calcMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "calculateComposite");
        calcMethod.Should().NotBeNull();
    }

    #endregion

    #region Inner Classes and Qualified This Reference

    /// <summary>
    /// Test the handling of 'inner class' with reference to the external context via 'this@OuterClassName'.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldHandleInnerClassesWithQualifiedThis()
    {
        // Arrange
        var code = """
            package com.acme.hierarchy

            class OuterWindow(val windowTitle: String) {
                fun refreshWindow() { }

                inner class WindowButton(val label: String) {
                    fun click() {
                        val title = this@OuterWindow.windowTitle
                        this@OuterWindow.refreshWindow()
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Window.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var outerCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "OuterWindow");
        outerCls.Should().NotBeNull();

        var innerCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "WindowButton");
        innerCls.Should().NotBeNull();

        var refreshMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "refreshWindow");
        refreshMethod.Should().NotBeNull();

        var clickMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "click");
        clickMethod.Should().NotBeNull();

        var calls = graph.GetOutgoingRelationships(clickMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        calls.Should().Contain(r => r.TargetId == refreshMethod!.Id);
    }

    #endregion

    #region Multiline Raw Strings and Complex Interpolation

    /// <summary>
    /// Test parser resilience when dealing with triple quotes (\"\"\") raw multiline strings
    /// containing escape characters, internal quotes, regexes, and nested interpolations ${expr}.
    /// </summary>
    [Fact]
    public void KotlinParser_ShouldHandleComplexRawStringsAndNestedInterpolation()
    {
        // Arrange
        var code = """"
            package com.acme.templates

            class QueryBuilder {
                fun buildQuery(tableName: String, limit: Int): String {
                    val escapedPattern = """[a-zA-Z0-9_]+"""
                    val query = """
                        SELECT id, name, "extra_info"
                        FROM $tableName
                        WHERE active = 1 AND regex_match(code, '$escapedPattern')
                        ORDER BY id DESC
                        LIMIT ${limit.coerceAtLeast(1)}
                    """.trimIndent()
                    return query
                }
            }
            """";

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("QueryBuilder.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var qbCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "QueryBuilder");
        qbCls.Should().NotBeNull();

        var buildMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "buildQuery");
        buildMethod.Should().NotBeNull();
    }

    #endregion

    #region Operator Overloading (get, set, invoke, contains)

    /// <summary>
    /// In Kotlin 'operator fun' defines syntactic conventions (indexers [], invocation (), etc.).
    /// Check for successful extraction of these overloaded methods.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldExtractOperatorOverloadingConventions()
    {
        // Arrange
        var code = """
            package com.acme.collections

            class CustomDictionary {
                private val store = mutableMapOf<String, String>()

                operator fun get(key: String): String? = store[key]

                operator fun set(key: String, value: String) {
                    store[key] = value
                }

                operator fun contains(key: String): Boolean = store.containsKey(key)

                operator fun invoke(prefix: String): List<String> {
                    return store.keys.filter { it.startsWith(prefix) }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Dictionary.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var dictCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "CustomDictionary");
        dictCls.Should().NotBeNull();

        var getMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "get");
        getMethod.Should().NotBeNull();

        var setMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "set");
        setMethod.Should().NotBeNull();

        var containsMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "contains");
        containsMethod.Should().NotBeNull();

        var invokeMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "invoke");
        invokeMethod.Should().NotBeNull();
    }

    #endregion
}
