using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

/// <summary>
/// Suite di test per costrutti Java moderni (Java 14-21+) e casi sintattici complessi, inclusi sealed types,
/// switch expressions, lambda, anonymous classes, record compatti e generic bounds avanzati.
/// </summary>
public class JavaModernPatternsAndAdvancedEdgeCasesTests
{
    #region Sealed Classes and Interfaces (Java 17+)

    /// <summary>
    /// Verifica l'estrazione corretta di gerarchie sigillate ('sealed class Shape permits Circle, Square')
    /// e la creazione delle relazioni INHERITS.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractSealedClassesAndPermittedSubclasses()
    {
        // Arrange
        var code = """
            package com.example.geometry;

            public sealed interface Shape permits Circle, Rectangle, Polygon {
                double area();
            }

            public final class Circle implements Shape {
                private final double radius;
                public Circle(double radius) { this.radius = radius; }
                @Override public double area() { return Math.PI * radius * radius; }
            }

            public sealed class Rectangle implements Shape permits Square {
                protected final double width, height;
                public Rectangle(double width, double height) { this.width = width; this.height = height; }
                @Override public double area() { return width * height; }
            }

            public final class Square extends Rectangle {
                public Square(double side) { super(side, side); }
            }

            public non-sealed class Polygon implements Shape {
                @Override public double area() { return 0.0; }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Geometry.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var shape = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "Shape");
        var circle = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Circle");
        var rectangle = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Rectangle");
        var square = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Square");
        var polygon = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Polygon");

        shape.Should().NotBeNull();
        circle.Should().NotBeNull();
        rectangle.Should().NotBeNull();
        square.Should().NotBeNull();
        polygon.Should().NotBeNull();

        // Verifica relazioni implements / inherits
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Implements && r.SourceId == circle!.Id && r.TargetId == shape!.Id);
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Implements && r.SourceId == rectangle!.Id && r.TargetId == shape!.Id);
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Inherits && r.SourceId == square!.Id && r.TargetId == rectangle!.Id);
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Implements && r.SourceId == polygon!.Id && r.TargetId == shape!.Id);
    }

    #endregion

    #region Pattern Matching and Switch Expressions (Java 14-21+)

    /// <summary>
    /// Verifica il parsing di switch expressions con frecce '->', 'yield' e pattern matching con clausole 'when'.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractMethodsWithModernSwitchExpressionsAndPatternMatching()
    {
        // Arrange
        var code = """
            package com.example.matcher;

            public class PatternEvaluator {
                public String formatObject(Object obj) {
                    return switch (obj) {
                        case Integer i -> String.format("int %d", i);
                        case Long l -> String.format("long %d", l);
                        case Double d -> {
                            double rounded = Math.round(d * 100.0) / 100.0;
                            yield String.format("double %f", rounded);
                        }
                        case String s when s.length() > 5 -> "Long string: " + s;
                        case String s -> "Short string: " + s;
                        case null -> "null object";
                        default -> obj.toString();
                    };
                }

                public boolean isPositiveNumber(Object obj) {
                    if (obj instanceof Integer i && i > 0) {
                        return true;
                    }
                    if (obj instanceof Double d && d > 0.0) {
                        return true;
                    }
                    return false;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("PatternEvaluator.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var cls = graph.Symbols.FirstOrDefault(s => s.Name == "PatternEvaluator");
        var formatMethod = graph.Symbols.FirstOrDefault(s => s.Name == "formatObject");
        var isPositiveMethod = graph.Symbols.FirstOrDefault(s => s.Name == "isPositiveNumber");

        cls.Should().NotBeNull();
        formatMethod.Should().NotBeNull();
        isPositiveMethod.Should().NotBeNull();
    }

    #endregion

    #region Interface Default and Static Methods

    /// <summary>
    /// Verifica l'estrazione di interfacce con metodi default, static e private (Java 8/9+),
    /// nonché la risoluzione delle chiamate interne tra metodi dell'interfaccia.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractDefaultAndStaticMethodsInInterfaces()
    {
        // Arrange
        var code = """
            package com.example.service;

            public interface AuditLogger {
                void writeRaw(String entry);

                default void info(String message) {
                    writeRaw(formatEntry("INFO", message));
                }

                default void error(String message) {
                    writeRaw(formatEntry("ERROR", message));
                }

                static String formatEntry(String level, String msg) {
                    return "[" + level + "] " + msg;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("AuditLogger.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var iface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "AuditLogger");
        var writeRaw = graph.Symbols.FirstOrDefault(s => s.Name == "writeRaw");
        var info = graph.Symbols.FirstOrDefault(s => s.Name == "info");
        var error = graph.Symbols.FirstOrDefault(s => s.Name == "error");
        var formatEntry = graph.Symbols.FirstOrDefault(s => s.Name == "formatEntry");

        iface.Should().NotBeNull();
        writeRaw.Should().NotBeNull();
        info.Should().NotBeNull();
        error.Should().NotBeNull();
        formatEntry.Should().NotBeNull();

        // Le chiamate default -> writeRaw e default -> formatEntry devono essere collegate
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.SourceId == info!.Id && r.TargetId == writeRaw!.Id);
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.SourceId == info!.Id && r.TargetId == formatEntry!.Id);
    }

    #endregion

    #region Records with Compact Constructors and Custom Methods

    /// <summary>
    /// Verifica l'estrazione di record con costruttori canonici, compatti, metodi accessori e costruttori secondari sovraccaricati.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractRecordWithCompactAndOverloadedConstructors()
    {
        // Arrange
        var code = """
            package com.example.domain;

            public record DateRange(java.time.LocalDate start, java.time.LocalDate end) {
                // Compact constructor
                public DateRange {
                    if (start != null && end != null && start.isAfter(end)) {
                        throw new IllegalArgumentException("Start cannot be after end");
                    }
                }

                // Secondary constructor
                public DateRange(java.time.LocalDate singleDate) {
                    this(singleDate, singleDate);
                }

                public boolean contains(java.time.LocalDate date) {
                    return !date.isBefore(start) && !date.isAfter(end);
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("DateRange.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var recordSymbol = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Record && s.Name == "DateRange");
        var containsMethod = graph.Symbols.FirstOrDefault(s => s.Name == "contains");

        recordSymbol.Should().NotBeNull();
        containsMethod.Should().NotBeNull();
    }

    #endregion

    #region Complex Generics and Multi-Bounds

    /// <summary>
    /// Verifica il parsing di classi generiche con bounds multipli (&) e wildcard bounds complessi.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractClassesAndMethodsWithMultiBoundGenerics()
    {
        // Arrange
        var code = """
            package com.example.util;

            import java.io.Serializable;
            import java.util.List;

            public class Repository<T extends Serializable & Comparable<T>> {
                public <R extends Number & Comparable<R>> R computeMetric(List<? extends T> items, List<? super R> destination) {
                    return null;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Repository.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var repoClass = graph.Symbols.FirstOrDefault(s => s.Name == "Repository");
        var computeMetric = graph.Symbols.FirstOrDefault(s => s.Name == "computeMetric");

        repoClass.Should().NotBeNull();
        computeMetric.Should().NotBeNull();
        computeMetric!.Parameters.Should().HaveCount(2);
    }

    #endregion

    #region Try-With-Resources and Multi-Catch

    /// <summary>
    /// Verifica l'estrazione di metodi con blocchi try-with-resources complessi e multi-catch (pipe syntax).
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractMethodsWithTryWithResourcesAndMultiCatch()
    {
        // Arrange
        var code = """
            package com.example.io;

            import java.io.*;
            import java.sql.*;

            public class DataIngestion {
                public void transfer(File source, File destination) {
                    try (FileInputStream in = new FileInputStream(source);
                         FileOutputStream out = new FileOutputStream(destination)) {
                        in.transferTo(out);
                    } catch (IOException | RuntimeException e) {
                        handleError(e);
                    }
                }

                private void handleError(Exception e) { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("DataIngestion.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var transfer = graph.Symbols.FirstOrDefault(s => s.Name == "transfer");
        var handleError = graph.Symbols.FirstOrDefault(s => s.Name == "handleError");

        transfer.Should().NotBeNull();
        handleError.Should().NotBeNull();

        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.SourceId == transfer!.Id && r.TargetId == handleError!.Id);
    }

    #endregion
}
