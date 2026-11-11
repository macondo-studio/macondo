using FluentAssertions;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

public class JavaAdvancedLanguageAndEdgeCasesTests
{
    /// <summary>
    /// Test Java's handling of 15+ multiline Text Blocks (""" ... """)
    /// containing quotes, nested SQL/JSON code, and escape characters.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleMultilineTextBlocks()
    {
        // Arrange
        var code = """"
            package com.acme.queries;

            public class SqlRepository {
                public static final String QUERY = """
                    SELECT u.id, u.username, u.email
                    FROM users u
                    WHERE u.status = 'ACTIVE'
                    ORDER BY u.created_at DESC;
                    """;

                public void executeQuery() {
                    System.out.println(QUERY);
                }
            }
            """";

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("SqlRepository.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var queryConst = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "QUERY");
        queryConst.Should().NotBeNull();
        queryConst!.DeclaredTypeName.Should().Be("String");

        var method = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "executeQuery");
        method.Should().NotBeNull();
    }

    /// <summary>
    /// Check the handling of rich annotations on classes, fields and parameters (@Entity, @Table, @Column, @NotNull).
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleRichAnnotations()
    {
        // Arrange
        var code = """
            package com.acme.entities;

            @Entity
            @Table(name = "customers", schema = "crm")
            public class CustomerEntity {
                @Id
                @GeneratedValue(strategy = GenerationType.IDENTITY)
                private Long id;

                @Column(name = "full_name", nullable = false, length = 100)
                private String fullName;

                @PrePersist
                public void onPrePersist() {
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("CustomerEntity.java", code);

        // Assert
        result.Success.Should().BeTrue();

        var classSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "CustomerEntity");
        classSym.Should().NotBeNull();

        var idField = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Field && s.Name == "id");
        idField.Should().NotBeNull();
        idField!.DeclaredTypeName.Should().Be("Long");

        var method = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "onPrePersist");
        method.Should().NotBeNull();
    }

    /// <summary>
    /// Check the management of complex generics at class and method level (e.g. <T extends Comparable<T>>).
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleComplexGenerics()
    {
        // Arrange
        var code = """
            package com.acme.generics;

            import java.util.List;
            import java.util.Map;

            public class Repository<T, ID extends Number> {
                public <R extends Comparable<R>> List<R> findSorted(Map<String, List<T>> filters) {
                    return null;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Repository.java", code);

        // Assert
        result.Success.Should().BeTrue();

        var classSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Repository");
        classSym.Should().NotBeNull();

        var method = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "findSorted");
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be("List");
        method.Parameters.Should().HaveCount(1);
        method.Parameters[0].DeclaredTypeName.Should().Be("Map");
    }

    /// <summary>
    /// Check the declaration of multiple variables or fields in the same line (e.g. 'int a = 1, b, c = 3;').
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleMultiVariableDeclarations()
    {
        // Arrange
        var code = """
            package com.acme.math;

            public class MathVector {
                private double x = 0.0, y = 0.0, z = 1.0;
                public static final int DIM_X = 1, DIM_Y = 2, DIM_Z = 3;
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("MathVector.java", code);

        // Assert
        result.Success.Should().BeTrue();

        var xField = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Field && s.Name == "x");
        var yField = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Field && s.Name == "y");
        var zField = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Field && s.Name == "z");

        xField.Should().NotBeNull();
        yField.Should().NotBeNull();
        zField.Should().NotBeNull();

        var dimX = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "DIM_X");
        var dimY = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "DIM_Y");
        var dimZ = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "DIM_Z");

        dimX.Should().NotBeNull();
        dimY.Should().NotBeNull();
        dimZ.Should().NotBeNull();
    }

    /// <summary>
    /// Test the extractor's resiliency on incomplete, fragmented, or syntactically incorrect Java code.
    /// The extractor should not raise blocking exceptions and extract valid symbols in best-effort mode.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldBeResilientToSyntaxErrorsAndPartialCode()
    {
        // Arrange
        var brokenCode = """
            package com.acme.broken;

            public class PartialClass {
                private int validField = 10;

                public void validMethod() {
                    int x = 5;
                    // broken statement without closure
                    broken (((( syntax error here !!!

                public void anotherValidMethod() {
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("PartialClass.java", brokenCode);

        // Assert
        // The parser must not throw exceptions
        var classSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "PartialClass");
        classSym.Should().NotBeNull();

        var validField = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Field && s.Name == "validField");
        validField.Should().NotBeNull();
    }
}
