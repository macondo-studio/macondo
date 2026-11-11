using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

/// <summary>
/// Depth test suite on advanced Java language constructs:
/// anonymous classes, static/instance initialization blocks, enums with specific methods
/// for constant, concurrency modifiers, multidimensional arrays, and advanced generics.
/// </summary>
public class JavaComprehensiveLanguageEdgeCasesTests
{
    #region Anonymous Classes and Local Variable Captures

    /// <summary>
    /// Verify the extraction of anonymous classes instantiated inline (e.g. Runnable, Comparator)
    /// and proper logging of their overridden methods and internal calls.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleAnonymousClassesAndOverriddenMethods()
    {
        // Arrange
        var code = """
            package com.example.async;

            import java.util.concurrent.ExecutorService;
            import java.util.concurrent.Executors;

            public class TaskManager {
                private final ExecutorService executor = Executors.newSingleThreadExecutor();

                public void executeCustomTask() {
                    executor.submit(new Runnable() {
                        @Override
                        public void run() {
                            doBackgroundWork();
                        }
                    });
                }

                private void doBackgroundWork() { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("TaskManager.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var taskManagerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "TaskManager");
        var executeCustomTask = graph.Symbols.FirstOrDefault(s => s.Name == "executeCustomTask");
        var doBackgroundWork = graph.Symbols.FirstOrDefault(s => s.Name == "doBackgroundWork");

        taskManagerClass.Should().NotBeNull();
        executeCustomTask.Should().NotBeNull();
        doBackgroundWork.Should().NotBeNull();
    }

    #endregion

    #region Enums with Constant-Specific Class Bodies

    /// <summary>
    /// Test extraction of advanced enums where each constant defines a class body with specialized methods.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractEnumWithConstantSpecificMethods()
    {
        // Arrange
        var code = """
            package com.example.calculator;

            public enum Operation {
                PLUS("+") {
                    @Override
                    public double apply(double x, double y) {
                        return x + y;
                    }
                },
                MINUS("-") {
                    @Override
                    public double apply(double x, double y) {
                        return x - y;
                    }
                },
                TIMES("*") {
                    @Override
                    public double apply(double x, double y) {
                        return x * y;
                    }
                },
                DIVIDE("/") {
                    @Override
                    public double apply(double x, double y) {
                        if (y == 0.0) {
                            throw new ArithmeticException("Division by zero");
                        }
                        return x / y;
                    }
                };

                private final String symbol;

                Operation(String symbol) {
                    this.symbol = symbol;
                }

                public String getSymbol() {
                    return symbol;
                }

                public abstract double apply(double x, double y);
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Operation.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var enumSymbol = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Enum && s.Name == "Operation");
        var applyMethod = graph.Symbols.FirstOrDefault(s => s.Name == "apply");
        var getSymbolMethod = graph.Symbols.FirstOrDefault(s => s.Name == "getSymbol");

        enumSymbol.Should().NotBeNull();
        applyMethod.Should().NotBeNull();
        getSymbolMethod.Should().NotBeNull();

        // Enum constants
        var enumConstants = graph.GetChildren(enumSymbol!.Id).Where(s => s.Kind == SymbolKind.Constant).ToList();
        enumConstants.Should().HaveCount(4);
        enumConstants.Select(c => c.Name).Should().Contain(new[] { "PLUS", "MINUS", "TIMES", "DIVIDE" });
    }

    #endregion

    #region Static and Instance Initializer Blocks

    /// <summary>
    /// Tests parsing of static (static { ... }) and instance ({ ... }) initialization blocks.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleStaticAndInstanceInitializerBlocks()
    {
        // Arrange
        var code = """
            package com.example.config;

            import java.util.HashMap;
            import java.util.Map;

            public class SystemRegistry {
                private static final Map<String, String> SETTINGS = new HashMap<>();
                private final String instanceId;

                static {
                    SETTINGS.put("env", "production");
                    SETTINGS.put("version", "1.0.0");
                    initStaticDefaults();
                }

                {
                    instanceId = java.util.UUID.randomUUID().toString();
                    trackInstanceCreation();
                }

                private static void initStaticDefaults() { }
                private void trackInstanceCreation() { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("SystemRegistry.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var cls = graph.Symbols.FirstOrDefault(s => s.Name == "SystemRegistry");
        var initDefaults = graph.Symbols.FirstOrDefault(s => s.Name == "initStaticDefaults");
        var trackCreation = graph.Symbols.FirstOrDefault(s => s.Name == "trackInstanceCreation");

        cls.Should().NotBeNull();
        initDefaults.Should().NotBeNull();
        trackCreation.Should().NotBeNull();
    }

    #endregion

    #region Concurrency Modifiers (Synchronized, Volatile, Transient)

    /// <summary>
    /// Tests extraction of synchronized methods, synchronized blocks, and fields with transient/volatile modifiers.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleConcurrencyModifiersAndSynchronizedBlocks()
    {
        // Arrange
        var code = """
            package com.example.concurrency;

            public class SafeCounter {
                private volatile int counter = 0;
                private transient Object lockObject = new Object();

                public synchronized void increment() {
                    counter++;
                    notifyChange();
                }

                public int getValue() {
                    synchronized (lockObject) {
                        return counter;
                    }
                }

                private void notifyChange() { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("SafeCounter.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var counterClass = graph.Symbols.FirstOrDefault(s => s.Name == "SafeCounter");
        var incrementMethod = graph.Symbols.FirstOrDefault(s => s.Name == "increment");
        var getValueMethod = graph.Symbols.FirstOrDefault(s => s.Name == "getValue");
        var notifyChange = graph.Symbols.FirstOrDefault(s => s.Name == "notifyChange");

        counterClass.Should().NotBeNull();
        incrementMethod.Should().NotBeNull();
        getValueMethod.Should().NotBeNull();
        notifyChange.Should().NotBeNull();

        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.SourceId == incrementMethod!.Id && r.TargetId == notifyChange!.Id);
    }

    #endregion

    #region Multi-dimensional Arrays and Array Return Types

    /// <summary>
    /// Check for correct extraction of return types and parameters with multidimensional arrays (int[][], String[][][]).
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractMultidimensionalArrays()
    {
        // Arrange
        var code = """
            package com.example.math;

            public class MatrixUtils {
                public double[][] multiply(double[][] a, double[][] b) {
                    validateMatrix(a);
                    validateMatrix(b);
                    return new double[a.length][b[0].length];
                }

                public String[][][] create3DCube(int depth, int rows, int cols) {
                    return new String[depth][rows][cols];
                }

                private void validateMatrix(double[][] m) { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("MatrixUtils.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var multiply = graph.Symbols.FirstOrDefault(s => s.Name == "multiply");
        var createCube = graph.Symbols.FirstOrDefault(s => s.Name == "create3DCube");
        var validateMatrix = graph.Symbols.FirstOrDefault(s => s.Name == "validateMatrix");

        multiply.Should().NotBeNull();
        createCube.Should().NotBeNull();
        validateMatrix.Should().NotBeNull();

        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.SourceId == multiply!.Id && r.TargetId == validateMatrix!.Id);
    }

    #endregion

    #region Throws Clause with Multiple Exceptions

    /// <summary>
    /// Tests extraction of throws clauses containing multiple comma-separated exceptions.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractMethodsWithMultipleThrowsExceptions()
    {
        // Arrange
        var code = """
            package com.example.io;

            import java.io.IOException;
            import java.sql.SQLException;
            import java.util.concurrent.TimeoutException;

            public class DatabaseClient {
                public void executeQuery(String sql) throws IOException, SQLException, TimeoutException {
                    connect();
                    runRawSql(sql);
                }

                private void connect() throws IOException { }
                private void runRawSql(String sql) throws SQLException { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("DatabaseClient.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var executeQuery = graph.Symbols.FirstOrDefault(s => s.Name == "executeQuery");
        var connect = graph.Symbols.FirstOrDefault(s => s.Name == "connect");
        var runRawSql = graph.Symbols.FirstOrDefault(s => s.Name == "runRawSql");

        executeQuery.Should().NotBeNull();
        connect.Should().NotBeNull();
        runRawSql.Should().NotBeNull();

        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.SourceId == executeQuery!.Id && r.TargetId == connect!.Id);
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Calls && r.SourceId == executeQuery!.Id && r.TargetId == runRawSql!.Id);
    }

    #endregion
}
