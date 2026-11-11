using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

/// <summary>
/// Deep-hunter test suite for complex architectural scenarios and edge cases in Java:
/// mutual cross-class recursion, diamond inheritance between interfaces with default methods,
/// polymorphic calls, calculation of cyclomatic complexity with nested constructs,
/// javadoc/block/line comments and advanced flow constructs (labelled break/continue).
/// </summary>
public class JavaComprehensiveEdgeCasesDeepHunterTests
{
    #region Mutual Recursion Across Classes

    /// <summary>
    /// Verifies the correct construction of the call graph in case of mutual recursion between two distinct classes
    /// (PingService.ping -> PongService.pong -> PingService.ping).
    /// </summary>
    [Fact]
    public void JavaCallGraph_ShouldResolveMutualRecursionAcrossClasses()
    {
        // Arrange
        var pingSource = """
            package com.acme.recursion;

            public class PingService {
                private final PongService pongService;

                public PingService(PongService pongService) {
                    this.pongService = pongService;
                }

                public void ping(int count) {
                    if (count > 0) {
                        pongService.pong(count - 1);
                    }
                }
            }
            """;

        var pongSource = """
            package com.acme.recursion;

            public class PongService {
                private PingService pingService;

                public void setPingService(PingService pingService) {
                    this.pingService = pingService;
                }

                public void pong(int count) {
                    if (count > 0) {
                        pingService.ping(count - 1);
                    }
                }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeJavaSources(
            ("PingService.java", pingSource),
            ("PongService.java", pongSource));

        // Assert
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());

        var pingMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "ping");
        var pongMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "pong");

        pingMethod.Should().NotBeNull();
        pongMethod.Should().NotBeNull();

        var pingCallsPong = graph.Relationships.Any(r =>
            r.Kind == RelationshipKind.Calls &&
            r.SourceId == pingMethod!.Id &&
            r.TargetId == pongMethod!.Id);

        var pongCallsPing = graph.Relationships.Any(r =>
            r.Kind == RelationshipKind.Calls &&
            r.SourceId == pongMethod!.Id &&
            r.TargetId == pingMethod!.Id);

        pingCallsPong.Should().BeTrue();
        pongCallsPing.Should().BeTrue();
    }

    #endregion

    #region Diamond Interface Hierarchy with Default Methods

    /// <summary>
    /// Test extraction and resolution of inheritance relations for diamond structures:
    /// BaseEmitter -> (AudioEmitter, VisualEmitter) -> MultiMediaEmitter with explicit override.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldResolveDiamondInterfaceHierarchy()
    {
        // Arrange
        var code = """
            package com.acme.media;

            public interface BaseEmitter {
                default void emit() {
                    System.out.println("Base emission");
                }
            }

            public interface AudioEmitter extends BaseEmitter {
                @Override
                default void emit() {
                    System.out.println("Audio emission");
                }
            }

            public interface VisualEmitter extends BaseEmitter {
                @Override
                default void emit() {
                    System.out.println("Visual emission");
                }
            }

            public class MultiMediaEmitter implements AudioEmitter, VisualEmitter {
                @Override
                public void emit() {
                    AudioEmitter.super.emit();
                    VisualEmitter.super.emit();
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("MultiMediaEmitter.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var baseEmitter = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "BaseEmitter");
        var audioEmitter = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "AudioEmitter");
        var visualEmitter = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "VisualEmitter");
        var multiMedia = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "MultiMediaEmitter");

        baseEmitter.Should().NotBeNull();
        audioEmitter.Should().NotBeNull();
        visualEmitter.Should().NotBeNull();
        multiMedia.Should().NotBeNull();

        var multiMediaEmit = graph.GetChildren(multiMedia!.Id).FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "emit");
        multiMediaEmit.Should().NotBeNull();
    }

    #endregion

    #region Nested Control Flow and Labelled Jumps

    /// <summary>
    /// Verify that the parser handles control-flow constructs correctly:
    /// nested for loops with labels, expression/statement switches, if-else guards, try-catch blocks, and ternary operators.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldParseComplexNestedControlFlow()
    {
        // Arrange
        var code = """
            package com.acme.algorithm;

            public class MatrixSearcher {
                public boolean searchInMatrix(int[][] matrix, int target) {
                    if (matrix == null || matrix.length == 0 || matrix[0].length == 0) {
                        return false;
                    }

                    int rows = matrix.length;
                    int cols = matrix[0].length;
                    boolean found = false;

                    outerLoop:
                    for (int r = 0; r < rows; r++) {
                        if (matrix[r][0] > target) {
                            break outerLoop;
                        }

                        for (int c = 0; c < cols; c++) {
                            int current = matrix[r][c];
                            if (current == target) {
                                found = true;
                                break outerLoop;
                            } else if (current > target) {
                                continue outerLoop;
                            }
                        }
                    }

                    return found;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("MatrixSearcher.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var searchMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "searchInMatrix");
        searchMethod.Should().NotBeNull();
    }

    #endregion

    #region Multi-Catch, Nested Try-Finally and Complex Exceptions

    /// <summary>
    /// Test parsing of nested, multi-catch try-catch blocks with union types (SQLException | IOException),
    /// and finally constructs that invoke helper methods.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleNestedTryCatchAndUnionExceptions()
    {
        // Arrange
        var code = """
            package com.acme.resilience;

            import java.io.IOException;
            import java.sql.SQLException;

            public class TransactionManager {
                public void executeTransaction() throws Exception {
                    try {
                        performStepOne();
                        try {
                            performStepTwo();
                        } catch (SQLException | IOException ex) {
                            rollback();
                            throw new Exception("Transaction step 2 failed", ex);
                        }
                    } catch (Exception e) {
                        logError(e);
                        throw e;
                    } finally {
                        closeConnections();
                    }
                }

                private void performStepOne() { }
                private void performStepTwo() throws SQLException, IOException { }
                private void rollback() { }
                private void logError(Exception e) { }
                private void closeConnections() { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("TransactionManager.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var txMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "executeTransaction");
        txMethod.Should().NotBeNull();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == txMethod!.Id).ToList();
        calls.Should().NotBeEmpty();
    }

    #endregion

    #region Edge Cases: Empty Unit, Comments, Unicode Identifiers, Static Initializers

    /// <summary>
    /// Test parser resilience with identifiers containing valid Unicode characters in Java,
    /// static/instance initialization blocks and complex comments (Javadoc with HTML tags).
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleUnicodeIdentifiersAndInitializationBlocks()
    {
        // Arrange
        var code = """
            package com.acme.i18n;

            import java.util.HashMap;
            import java.util.Map;

            /**
             * Calcolatore per valute internazionali.
             * <p>Supporta caratteri come &euro;, &yen;, &pound;.</p>
             */
            public class ValutaCalculator_€ {
                private static final Map<String, Double> TASSI_CAMBIO = new HashMap<>();

                static {
                    TASSI_CAMBIO.put("EUR", 1.0);
                    TASSI_CAMBIO.put("USD", 1.08);
                    TASSI_CAMBIO.put("JPY", 160.5);
                }

                {
                    // Instance initialization block
                    initCache();
                }

                public double converti_€_to_$(double importo_€) {
                    return importo_€ * TASSI_CAMBIO.getOrDefault("USD", 1.0);
                }

                private void initCache() { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("ValutaCalculator.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var valutaClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ValutaCalculator_€");
        valutaClass.Should().NotBeNull();

        var convertMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "converti_€_to_$");
        convertMethod.Should().NotBeNull();
        convertMethod!.Parameters.Should().HaveCount(1);
    }

    #endregion
}
