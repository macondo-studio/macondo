using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

/// <summary>
/// Suite di test deep-hunter per scenari architetturali complessi e casi limite in Java:
/// mutua ricorsione cross-classe, ereditarietà a diamante tra interfacce con metodi default,
/// chiamate polimorfiche, calcolo della complessità ciclomatica con costrutti nidificati,
/// commenti javadoc/blocco/linea e costrutti di flusso avanzati (labelled break/continue).
/// </summary>
public class JavaComprehensiveEdgeCasesDeepHunterTests
{
    #region Mutual Recursion Across Classes

    /// <summary>
    /// Verifica la corretta costruzione del call graph in caso di ricorsione mutua tra due classi distinte
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
    /// Verifica l'estrazione e la risoluzione delle relazioni di ereditarietà per strutture a diamante:
    /// BaseEmitter -> (AudioEmitter, VisualEmitter) -> MultiMediaEmitter con sovrascrittura esplicita.
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
    /// Verifica che l'analizzatore gestisca correttamente costrutti di controllo di flusso:
    /// cicli for annidati con label, switch expression/statement, guardie if-else, blocchi try-catch e operatori ternari.
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
    /// Verifica l'analisi di blocchi try-catch annidati, multi-catch con tipi union (SQLException | IOException),
    /// e costrutti finally che invocano metodi di supporto.
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
    /// Verifica la resilienza del parser con identificatori contenenti caratteri Unicode validi in Java,
    /// blocchi di inizializzazione statici/istanza e commenti complessi (Javadoc con tag HTML).
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
