using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

/// <summary>
/// Suite di test esaustiva per costrutti avanzati del linguaggio Java:
/// generic bounds complessi, metodi varargs, classi locali e interne,
/// try-with-resources multi-risorsa, interfacce funzionali con metodi privati/default,
/// annotazioni personalizzate avanzate e pattern matching su record.
/// </summary>
public class JavaExhaustiveLanguageAndEdgeCasesTests
{
    #region Complex Generics and Wildcards

    /// <summary>
    /// Verifica la corretta estrazione e risoluzione di classi e metodi generici con vincoli multipli
    /// ad intersezione (T extends Comparable & Serializable), bounded wildcards e parametri ricorsivi.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleComplexGenericIntersectionAndWildcards()
    {
        // Arrange
        var code = """
            package com.acme.generics;

            import java.io.Serializable;
            import java.util.List;
            import java.util.Map;

            public class RepositoryManager<T extends Comparable<? super T> & Serializable> {
                private final Map<String, List<? extends T>> entityCache;

                public RepositoryManager(Map<String, List<? extends T>> initialCache) {
                    this.entityCache = initialCache;
                }

                public <R extends Serializable> R transform(T source, Class<R> targetClass) {
                    return null;
                }

                public void processAll(List<? super T> consumerList, List<? extends T> producerList) {
                    consumerList.addAll(producerList);
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("RepositoryManager.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var repoClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "RepositoryManager");
        repoClass.Should().NotBeNull();
        repoClass!.Accessibility.Should().Be(Accessibility.Public);

        var transformMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "transform");
        transformMethod.Should().NotBeNull();
        transformMethod!.Parameters.Should().HaveCount(2);

        var processAllMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "processAll");
        processAllMethod.Should().NotBeNull();
        processAllMethod!.Parameters.Should().HaveCount(2);
    }

    #endregion

    #region Varargs and Overloaded Methods

    /// <summary>
    /// Verifica l'estrazione e il riconoscimento di firme di metodi in overload
    /// con parametri varargs (String...), array primitivi (byte[]) e tipi generici.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleVarargsAndMethodOverloading()
    {
        // Arrange
        var code = """
            package com.acme.logging;

            public class LoggerService {
                public void log(String message) {
                    log(message, new Object[0]);
                }

                public void log(String format, Object... args) {
                    System.out.printf(format, args);
                }

                public void log(int level, String format, Object... args) {
                    if (level > 0) {
                        log(format, args);
                    }
                }

                public void writeBytes(byte[] buffer, int offset, int length) { }
                public void writeBytes(byte... data) { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("LoggerService.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var logMethods = graph.Symbols.Where(s => s.Kind == SymbolKind.Method && s.Name == "log").ToList();
        logMethods.Should().HaveCount(3);

        var writeBytesMethods = graph.Symbols.Where(s => s.Kind == SymbolKind.Method && s.Name == "writeBytes").ToList();
        writeBytesMethods.Should().HaveCount(2);
    }

    #endregion

    #region Inner, Nested and Local Classes

    /// <summary>
    /// Verifica l'estrazione di classi statiche annidate, classi interne con riferimento all'istanza outer
    /// (Outer.this) e classi locali definite internamente a metodi.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractNestedInnerAndLocalClasses()
    {
        // Arrange
        var code = """
            package com.acme.structures;

            public class Container {
                private String containerName;

                public static class StaticNested {
                    public void doStaticWork() { }
                }

                public class InnerMember {
                    public void accessOuter() {
                        System.out.println(Container.this.containerName);
                    }
                }

                public void processLocal() {
                    class LocalHelper {
                        public void execute() { }
                    }
                    LocalHelper helper = new LocalHelper();
                    helper.execute();
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Container.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var containerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Container");
        var staticNested = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "StaticNested");
        var innerMember = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "InnerMember");

        containerClass.Should().NotBeNull();
        staticNested.Should().NotBeNull();
        innerMember.Should().NotBeNull();
    }

    #endregion

    #region Try-With-Resources with Multiple AutoCloseable and Multi-Catch

    /// <summary>
    /// Verifica la gestione di blocchi try-with-resources complessi con istanziazione di più risorse
    /// AutoCloseable, clausole multi-catch (IOException | SQLException) e blocco finally.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleMultiResourceTryWithResourcesAndMultiCatch()
    {
        // Arrange
        var code = """
            package com.acme.io;

            import java.io.FileInputStream;
            import java.io.FileOutputStream;
            import java.io.IOException;
            import java.sql.SQLException;

            public class DataTransferService {
                public void transferData(String src, String dest) {
                    try (FileInputStream in = new FileInputStream(src);
                         FileOutputStream out = new FileOutputStream(dest)) {
                        byte[] buf = new byte[4096];
                        int n;
                        while ((n = in.read(buf)) >= 0) {
                            out.write(buf, 0, n);
                        }
                    } catch (IOException | SecurityException e) {
                        handleError(e);
                    } finally {
                        cleanup();
                    }
                }

                private void handleError(Exception e) { }
                private void cleanup() { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("DataTransferService.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var transferMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "transferData");
        var handleErrorMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "handleError");
        var cleanupMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "cleanup");

        transferMethod.Should().NotBeNull();
        handleErrorMethod.Should().NotBeNull();
        cleanupMethod.Should().NotBeNull();
    }

    #endregion

    #region Functional Interfaces with Default, Static and Private Methods

    /// <summary>
    /// Verifica l'estrazione di interfacce Java 8/9+ con metodi astratti, default, static e private.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractModernInterfaceFeatures()
    {
        // Arrange
        var code = """
            package com.acme.functional;

            @FunctionalInterface
            public interface StringTransformer {
                String transform(String input);

                default String transformAndTrim(String input) {
                    String result = transform(input);
                    return validateAndSanitize(result);
                }

                static StringTransformer identity() {
                    return s -> s;
                }

                private String validateAndSanitize(String s) {
                    return s != null ? s.trim() : "";
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("StringTransformer.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var interfaceSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "StringTransformer");
        interfaceSym.Should().NotBeNull();

        var transformMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "transform");
        var trimMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "transformAndTrim");
        var identityMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "identity");

        transformMethod.Should().NotBeNull();
        trimMethod.Should().NotBeNull();
        identityMethod.Should().NotBeNull();
    }

    #endregion

    #region Advanced Custom Annotation Declaration

    /// <summary>
    /// Verifica l'estrazione di annotazioni Java personalizzate (@interface) con elementi,
    /// valori di default, tipi enum, array e meta-annotazioni (@Retention, @Target).
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractCustomAnnotationDefinitions()
    {
        // Arrange
        var code = """
            package com.acme.annotations;

            import java.lang.annotation.ElementType;
            import java.lang.annotation.Retention;
            import java.lang.annotation.RetentionPolicy;
            import java.lang.annotation.Target;

            @Retention(RetentionPolicy.RUNTIME)
            @Target({ElementType.TYPE, ElementType.METHOD})
            public @interface Auditable {
                String action() default "DEFAULT_ACTION";
                int priority() default 1;
                String[] roles() default {"ADMIN", "USER"};
                Class<?> targetEntity() default Void.class;
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Auditable.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var annotationSym = graph.Symbols.FirstOrDefault(s => s.Name == "Auditable");
        annotationSym.Should().NotBeNull();
        annotationSym!.Accessibility.Should().Be(Accessibility.Public);
    }

    #endregion

    #region Record Patterns, Validation and Compact Constructors

    /// <summary>
    /// Verifica la gestione di record Java con compact constructor per validazione invarianti,
    /// costruttori canonici espliciti, metodi di istanza aggiuntivi e implementazione di interfacce.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleRecordWithCompactConstructorAndMethods()
    {
        // Arrange
        var code = """
            package com.acme.domain;

            import java.io.Serializable;

            public record Money(double amount, String currency) implements Serializable {
                public Money {
                    if (amount < 0) {
                        throw new IllegalArgumentException("Amount cannot be negative");
                    }
                    if (currency == null || currency.isBlank()) {
                        throw new IllegalArgumentException("Currency cannot be empty");
                    }
                }

                public Money add(Money other) {
                    if (!this.currency.equals(other.currency())) {
                        throw new IllegalArgumentException("Currencies must match");
                    }
                    return new Money(this.amount + other.amount(), this.currency);
                }

                public static Money zero(String currency) {
                    return new Money(0.0, currency);
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Money.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var recordSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Record && s.Name == "Money");
        recordSym.Should().NotBeNull();

        var addMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "add");
        var zeroMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "zero");

        addMethod.Should().NotBeNull();
        zeroMethod.Should().NotBeNull();
    }

    #endregion

    #region Concurrency Modifiers and Synchronized Blocks

    /// <summary>
    /// Verifica l'estrazione di campi con modificatori di concorrenza (volatile, transient)
    /// e metodi/blocchi con sincronizzazione esplicita.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldExtractConcurrencyModifiersAndSynchronizedConstructs()
    {
        // Arrange
        var code = """
            package com.acme.concurrent;

            public class ThreadSafeCounter {
                private volatile int count = 0;
                private transient Object lockObject = new Object();

                public synchronized void increment() {
                    count++;
                }

                public int getCount() {
                    synchronized (lockObject) {
                        return count;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("ThreadSafeCounter.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var counterClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ThreadSafeCounter");
        counterClass.Should().NotBeNull();

        var countField = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Field && s.Name == "count");
        var lockField = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Field && s.Name == "lockObject");

        countField.Should().NotBeNull();
        lockField.Should().NotBeNull();

        var incrementMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "increment");
        var getCountMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "getCount");

        incrementMethod.Should().NotBeNull();
        getCountMethod.Should().NotBeNull();
    }

    #endregion
}
