using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

/// <summary>
/// Suite di test approfondita per pattern architetturali enterprise, costrutti di concorrenza,
/// annotazioni complesse/ripetibili, enum con comportamenti polimorfici, record avanzati e text block in Java.
/// </summary>
public class JavaEnterpriseAndConcurrentPatternsTests
{
    #region Builder Pattern and Fluent APIs

    /// <summary>
    /// Verifica l'estrazione e l'analisi del classico Builder Pattern con classe statica interna annidata,
    /// metodi fluent per il concatenamento (chaining) che restituiscono la stessa istanza del Builder e metodo build().
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleBuilderPatternWithFluentMethodChaining()
    {
        // Arrange
        var code = """
            package com.acme.config;

            import java.time.Duration;

            public class ClientConfiguration {
                private final String endpoint;
                private final int maxRetries;
                private final Duration timeout;
                private final boolean enableTls;

                private ClientConfiguration(Builder builder) {
                    this.endpoint = builder.endpoint;
                    this.maxRetries = builder.maxRetries;
                    this.timeout = builder.timeout;
                    this.enableTls = builder.enableTls;
                }

                public String getEndpoint() { return endpoint; }
                public int getMaxRetries() { return maxRetries; }
                public Duration getTimeout() { return timeout; }
                public boolean isEnableTls() { return enableTls; }

                public static Builder builder() {
                    return new Builder();
                }

                public static final class Builder {
                    private String endpoint = "https://api.acme.com";
                    private int maxRetries = 3;
                    private Duration timeout = Duration.ofSeconds(30);
                    private boolean enableTls = true;

                    private Builder() { }

                    public Builder endpoint(String endpoint) {
                        this.endpoint = endpoint;
                        return this;
                    }

                    public Builder maxRetries(int maxRetries) {
                        this.maxRetries = maxRetries;
                        return this;
                    }

                    public Builder timeout(Duration timeout) {
                        this.timeout = timeout;
                        return this;
                    }

                    public Builder enableTls(boolean enableTls) {
                        this.enableTls = enableTls;
                        return this;
                    }

                    public ClientConfiguration build() {
                        if (endpoint == null || endpoint.isBlank()) {
                            throw new IllegalStateException("Endpoint cannot be null or blank");
                        }
                        return new ClientConfiguration(this);
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("ClientConfiguration.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var outerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ClientConfiguration");
        outerClass.Should().NotBeNull();

        var builderClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Builder");
        builderClass.Should().NotBeNull();
        builderClass!.Accessibility.Should().Be(Accessibility.Public);

        var buildMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "build");
        buildMethod.Should().NotBeNull();
    }

    #endregion

    #region Singleton Variations (Double-Checked Locking & Bill Pugh Holder)

    /// <summary>
    /// Verifica il parsing e l'estrazione di pattern Singleton avanzati:
    /// Double-Checked Locking con campo volatile e Bill Pugh Static Inner Holder.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleSingletonVariationsAndVolatileFields()
    {
        // Arrange
        var code = """
            package com.acme.core;

            public class ServiceRegistry {
                private static volatile ServiceRegistry instance;
                private static final Object LOCK = new Object();

                private ServiceRegistry() {
                    // Protected against reflection instantiation
                    if (instance != null) {
                        throw new IllegalStateException("Already initialized");
                    }
                }

                public static ServiceRegistry getInstance() {
                    if (instance == null) {
                        synchronized (LOCK) {
                            if (instance == null) {
                                instance = new ServiceRegistry();
                            }
                        }
                    }
                    return instance;
                }

                // Bill Pugh Singleton idiom variation for sub-component
                private static class Holder {
                    private static final ServiceRegistry LAZY_FALLBACK = new ServiceRegistry();
                }

                public static ServiceRegistry getLazyFallback() {
                    return Holder.LAZY_FALLBACK;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("ServiceRegistry.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var registryClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ServiceRegistry");
        registryClass.Should().NotBeNull();

        var getInstanceMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "getInstance");
        getInstanceMethod.Should().NotBeNull();
    }

    #endregion

    #region Enums with Abstract Methods and Interfaces

    /// <summary>
    /// Verifica la modellazione di enum con metodi astratti implementati da ciascuna costante,
    /// implementazione di interfacce e campi finali con costruttore parametrico.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleEnumWithAbstractMethodsAndInterfaces()
    {
        // Arrange
        var code = """
            package com.acme.math;

            import java.util.function.DoubleBinaryOperator;

            public interface OperationEvaluator {
                double evaluate(double x, double y);
            }

            public enum ArithmeticOperation implements OperationEvaluator {
                PLUS("+") {
                    @Override
                    public double evaluate(double x, double y) {
                        return x + y;
                    }
                },
                MINUS("-") {
                    @Override
                    public double evaluate(double x, double y) {
                        return x - y;
                    }
                },
                MULTIPLY("*") {
                    @Override
                    public double evaluate(double x, double y) {
                        return x * y;
                    }
                },
                DIVIDE("/") {
                    @Override
                    public double evaluate(double x, double y) {
                        if (y == 0.0) {
                            throw new ArithmeticException("Division by zero");
                        }
                        return x / y;
                    }
                };

                private final String symbol;

                ArithmeticOperation(String symbol) {
                    this.symbol = symbol;
                }

                public String getSymbol() {
                    return symbol;
                }

                public abstract double evaluate(double x, double y);
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("ArithmeticOperation.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var interfaceSymbol = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "OperationEvaluator");
        interfaceSymbol.Should().NotBeNull();

        var enumSymbol = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Enum && s.Name == "ArithmeticOperation");
        enumSymbol.Should().NotBeNull();

        var enumConstants = graph.GetChildren(enumSymbol!.Id).ToList();
        enumConstants.Should().HaveCount(4);
        enumConstants.Select(c => c.Name).Should().Contain(new[] { "PLUS", "MINUS", "MULTIPLY", "DIVIDE" });
    }

    #endregion

    #region Custom and Repeatable Annotations

    /// <summary>
    /// Verifica la corretta estrazione di definizioni di annotazioni con @interface,
    /// annotazioni ripetibili (@Repeatable), valori di default e tipizzazione degli elementi.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleRepeatableAndComplexCustomAnnotations()
    {
        // Arrange
        var code = """
            package com.acme.security;

            import java.lang.annotation.*;

            @Retention(RetentionPolicy.RUNTIME)
            @Target(ElementType.METHOD)
            public @interface RoleContainers {
                RequireRole[] value();
            }

            @Retention(RetentionPolicy.RUNTIME)
            @Target({ElementType.TYPE, ElementType.METHOD})
            @Repeatable(RoleContainers.class)
            public @interface RequireRole {
                String role();
                int minimumLevel() default 1;
                boolean strict() default false;
            }

            @RequireRole(role = "ADMIN", minimumLevel = 5)
            @RequireRole(role = "SUPERVISOR", minimumLevel = 3)
            public class SecureOrderController {
                @RequireRole(role = "AUDITOR")
                public void auditLogs() {
                    System.out.println("Auditing logs...");
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("SecurityAnnotations.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var controllerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "SecureOrderController");
        controllerClass.Should().NotBeNull();

        var auditMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "auditLogs");
        auditMethod.Should().NotBeNull();
    }

    #endregion

    #region Concurrency Constructs, Locks, and CompletableFuture

    /// <summary>
    /// Verifica l'analisi sintattica e la risoluzione delle dipendenze per classi di concorrenza avanzata
    /// con ReentrantLock, CountDownLatch, blocchi synchronized e catene di CompletableFuture.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleConcurrencyConstructsAndLocks()
    {
        // Arrange
        var code = """
            package com.acme.async;

            import java.util.concurrent.*;
            import java.util.concurrent.locks.ReentrantLock;
            import java.util.concurrent.locks.Condition;

            public class AsyncPipelineCoordinator {
                private final ReentrantLock lock = new ReentrantLock();
                private final Condition notEmpty = lock.newCondition();
                private final ExecutorService executor = Executors.newFixedThreadPool(4);

                public CompletableFuture<String> processDataAsync(String payload) {
                    return CompletableFuture.supplyAsync(() -> transform(payload), executor)
                        .thenApply(this::enrich)
                        .exceptionally(ex -> fallback(ex));
                }

                private String transform(String in) {
                    lock.lock();
                    try {
                        return "Transformed: " + in;
                    } finally {
                        lock.unlock();
                    }
                }

                private String enrich(String data) {
                    return data + " [ENRICHED]";
                }

                private String fallback(Throwable ex) {
                    return "ERROR: " + ex.getMessage();
                }

                public void shutdown() {
                    executor.shutdown();
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("AsyncPipelineCoordinator.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var coordinator = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "AsyncPipelineCoordinator");
        coordinator.Should().NotBeNull();

        var processMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "processDataAsync");
        processMethod.Should().NotBeNull();

        var transformMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "transform");
        transformMethod.Should().NotBeNull();
    }

    #endregion

    #region Template Method and Strategy Patterns

    /// <summary>
    /// Verifica l'analisi di pattern comportamentali Template Method e Strategy con classi astratte,
    /// metodi hook protected e implementazioni concrete con lambda e classi dedicate.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleTemplateMethodAndStrategyPatterns()
    {
        // Arrange
        var templateSource = """
            package com.acme.patterns;

            public abstract class ReportGeneratorTemplate {
                public final String generateReport() {
                    StringBuilder sb = new StringBuilder();
                    sb.append(buildHeader());
                    sb.append(buildBody());
                    sb.append(buildFooter());
                    return sb.toString();
                }

                protected abstract String buildHeader();
                protected abstract String buildBody();

                protected String buildFooter() {
                    return "--- End of Report ---\n";
                }
            }
            """;

        var pdfReportSource = """
            package com.acme.patterns;

            public class PdfReportGenerator extends ReportGeneratorTemplate {
                @Override
                protected String buildHeader() {
                    return "%PDF-1.7\n";
                }

                @Override
                protected String buildBody() {
                    return "Content in PDF stream format\n";
                }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeJavaSources(
            ("ReportGeneratorTemplate.java", templateSource),
            ("PdfReportGenerator.java", pdfReportSource));

        // Assert
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());

        var templateClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ReportGeneratorTemplate");
        var pdfClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "PdfReportGenerator");

        templateClass.Should().NotBeNull();
        pdfClass.Should().NotBeNull();

        var inheritsRel = graph.Relationships.Any(r =>
            r.Kind == RelationshipKind.Inherits &&
            r.SourceId == pdfClass!.Id &&
            r.TargetId == templateClass!.Id);

        inheritsRel.Should().BeTrue();
    }

    #endregion

    #region Custom Generic Iterable and Iterator Implementations

    /// <summary>
    /// Verifica la gestione di classi che implementano interfacce standard complesse con generics ricorsivi (Iterable<T>, Iterator<T>)
    /// ed elementi di rimozione e navigazione personalizzata.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleCustomGenericIterableAndIteratorImplementations()
    {
        // Arrange
        var code = """
            package com.acme.collections;

            import java.util.Iterator;
            import java.util.NoSuchElementException;

            public class CircularBuffer<T> implements Iterable<T> {
                private final Object[] elements;
                private int head = 0;
                private int tail = 0;
                private int count = 0;

                public CircularBuffer(int capacity) {
                    this.elements = new Object[capacity];
                }

                public synchronized void add(T item) {
                    elements[tail] = item;
                    tail = (tail + 1) % elements.length;
                    if (count < elements.length) {
                        count++;
                    } else {
                        head = (head + 1) % elements.length;
                    }
                }

                @Override
                public Iterator<T> iterator() {
                    return new BufferIterator();
                }

                private class BufferIterator implements Iterator<T> {
                    private int index = 0;

                    @Override
                    public boolean hasNext() {
                        return index < count;
                    }

                    @Override
                    @SuppressWarnings("unchecked")
                    public T next() {
                        if (!hasNext()) {
                            throw new NoSuchElementException();
                        }
                        T val = (T) elements[(head + index) % elements.length];
                        index++;
                        return val;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("CircularBuffer.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var bufferClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "CircularBuffer");
        bufferClass.Should().NotBeNull();

        var iteratorClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "BufferIterator");
        iteratorClass.Should().NotBeNull();
        iteratorClass!.Accessibility.Should().Be(Accessibility.Private);
    }

    #endregion

    #region Complex Record with Compact Constructor and Validation

    /// <summary>
    /// Verifica l'analisi di Java Records (Java 16+) con costruttori compatti di validazione,
    /// campi statici costanti, metodi di fabbrica statici e componenti generici.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleComplexRecordWithCompactConstructorsAndValidation()
    {
        // Arrange
        var code = """
            package com.acme.domain;

            import java.util.Objects;
            import java.util.UUID;

            public record CustomerAccount(
                UUID id,
                String email,
                String fullName,
                long balanceInCents
            ) {
                public static final long MAX_INITIAL_CREDIT = 100_000_00L;

                public CustomerAccount {
                    Objects.requireNonNull(id, "id must not be null");
                    Objects.requireNonNull(email, "email must not be null");
                    if (!email.contains("@")) {
                        throw new IllegalArgumentException("Invalid email format");
                    }
                    if (balanceInCents < 0) {
                        throw new IllegalArgumentException("Balance cannot be negative");
                    }
                    fullName = fullName == null ? "Unknown" : fullName.strip();
                }

                public static CustomerAccount createNew(String email, String fullName) {
                    return new CustomerAccount(UUID.randomUUID(), email, fullName, 0L);
                }

                public boolean hasPositiveBalance() {
                    return balanceInCents > 0L;
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("CustomerAccount.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var recordSymbol = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Record && s.Name == "CustomerAccount");
        recordSymbol.Should().NotBeNull();

        var createNewMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "createNew");
        createNewMethod.Should().NotBeNull();

        var hasPositiveBalanceMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "hasPositiveBalance");
        hasPositiveBalanceMethod.Should().NotBeNull();
    }

    #endregion

    #region Adapter and Decorator Structural Patterns

    /// <summary>
    /// Verifica l'analisi di pattern strutturali come Adapter e Decorator con delega interna (composition over inheritance).
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleAdapterAndDecoratorStructuralPatterns()
    {
        // Arrange
        var code = """
            package com.acme.structural;

            public interface Notifier {
                void send(String message);
            }

            public class EmailNotifier implements Notifier {
                @Override
                public void send(String message) {
                    System.out.println("Email: " + message);
                }
            }

            public abstract class BaseNotifierDecorator implements Notifier {
                protected final Notifier wrapped;

                public BaseNotifierDecorator(Notifier wrapped) {
                    this.wrapped = wrapped;
                }

                @Override
                public void send(String message) {
                    wrapped.send(message);
                }
            }

            public class SmsNotifierDecorator extends BaseNotifierDecorator {
                public SmsNotifierDecorator(Notifier wrapped) {
                    super(wrapped);
                }

                @Override
                public void send(String message) {
                    super.send(message);
                    sendSms(message);
                }

                private void sendSms(String message) {
                    System.out.println("SMS: " + message);
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("Notifiers.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var notifierInterface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "Notifier");
        var baseDecorator = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "BaseNotifierDecorator");
        var smsDecorator = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "SmsNotifierDecorator");

        notifierInterface.Should().NotBeNull();
        baseDecorator.Should().NotBeNull();
        smsDecorator.Should().NotBeNull();

        var decoratorInheritsBase = graph.Relationships.Any(r =>
            r.Kind == RelationshipKind.Inherits &&
            r.SourceId == smsDecorator!.Id &&
            r.TargetId == baseDecorator!.Id);

        decoratorInheritsBase.Should().BeTrue();
    }

    #endregion

    #region Multi-Line Text Blocks with Embedded Formats

    /// <summary>
    /// Verifica il parsing di stringhe multi-line (Text Blocks Java 15+) contenenti query SQL,
    /// strutture JSON e template HTML senza generare errori di parsing o corruzione dei delimitatori.
    /// </summary>
    [Fact]
    public void JavaExtractor_ShouldHandleMultiLineTextBlocksWithEmbeddedFormats()
    {
        // Arrange
        var code = """"
            package com.acme.templates;

            public class SqlAndJsonTemplates {
                public static final String SELECT_CUSTOMERS_QUERY = """
                    SELECT c.id, c.name, c.email, COUNT(o.id) as order_count
                    FROM customers c
                    LEFT JOIN orders o ON c.id = o.customer_id
                    WHERE c.status = 'ACTIVE'
                      AND c.created_at >= '2024-01-01'
                    GROUP BY c.id, c.name, c.email
                    ORDER BY order_count DESC;
                    """;

                public static final String JSON_PAYLOAD_SCHEMA = """
                    {
                        "$schema": "https://json-schema.org/draft/2020-12/schema",
                        "title": "CustomerEvent",
                        "type": "object",
                        "properties": {
                            "eventId": { "type": "string" },
                            "amount": { "type": "number" }
                        },
                        "required": ["eventId", "amount"]
                    }
                    """;

                public String getFormattedQuery(String minDate) {
                    return SELECT_CUSTOMERS_QUERY.replace("2024-01-01", minDate);
                }
            }
            """";

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("SqlAndJsonTemplates.java", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var templateClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "SqlAndJsonTemplates");
        templateClass.Should().NotBeNull();

        var formatMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "getFormattedQuery");
        formatMethod.Should().NotBeNull();
    }

    #endregion
}
