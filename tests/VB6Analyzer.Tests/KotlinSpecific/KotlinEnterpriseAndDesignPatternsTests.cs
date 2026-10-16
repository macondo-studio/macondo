using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.KotlinSpecific;

/// <summary>
/// Suite di test avanzata per pattern enterprise, architetturali e di design del linguaggio Kotlin (.kt),
/// inclusi Repository & Service con Dependency Injection, Factory tramite Companion Object, Extension Functions,
/// Class Delegation via 'by', Value Classes idiomatiche, annotazioni custom e DSL Builder.
/// </summary>
public class KotlinEnterpriseAndDesignPatternsTests
{
    #region Repository and Service Pattern with Dependency Injection

    /// <summary>
    /// Verifica l'estrazione e il tracciamento del pattern Repository e Service:
    /// interfaccia astratta, classe concreta che la implementa e Service che riceve
    /// il repository tramite Primary Constructor Injection invocandone le funzioni.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldExtractRepositoryPatternAndResolveServiceCalls()
    {
        // Arrange
        var code = """
            package com.acme.domain.users

            data class User(val id: String, val name: String, val active: Boolean)

            interface UserRepository {
                fun findById(id: String): User?
                fun save(user: User): Boolean
                fun delete(id: String)
            }

            class SqlUserRepository : UserRepository {
                override fun findById(id: String): User? {
                    return User(id, "Test User", true)
                }

                override fun save(user: User): Boolean {
                    return true
                }

                override fun delete(id: String) { }
            }

            class UserService(private val repository: UserRepository) {
                fun activateUser(userId: String): Boolean {
                    val user = repository.findById(userId)
                    if (user != null) {
                        val updated = User(user.id, user.name, true)
                        return repository.save(updated)
                    }
                    return false
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("UserServices.kt", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var userRepoIface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "UserRepository");
        userRepoIface.Should().NotBeNull();

        var sqlRepoClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "SqlUserRepository");
        sqlRepoClass.Should().NotBeNull();

        var userServiceClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "UserService");
        userServiceClass.Should().NotBeNull();

        var inheritsRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == sqlRepoClass!.Id &&
            r.TargetId == userRepoIface!.Id &&
            (r.Kind == RelationshipKind.Inherits || r.Kind == RelationshipKind.Implements));
        inheritsRel.Should().NotBeNull();

        var activateMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "activateUser");
        activateMethod.Should().NotBeNull();

        var findByIdMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "findById");
        findByIdMethod.Should().NotBeNull();

        var saveMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "save");
        saveMethod.Should().NotBeNull();

        var calls = graph.GetOutgoingRelationships(activateMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        calls.Should().Contain(r => r.TargetId == findByIdMethod!.Id);
        calls.Should().Contain(r => r.TargetId == saveMethod!.Id);
    }

    #endregion

    #region Factory Pattern with Companion Object

    /// <summary>
    /// Verifica il pattern Factory realizzato con costruttore privato e metodi factory nel Companion Object,
    /// assicurando la corretta estrazione dei metodi statici e la relazione di istanziazione.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldExtractFactoryPatternInCompanionObject()
    {
        // Arrange
        var code = """
            package com.acme.data

            class DatabaseConnection private constructor(val url: String, val poolSize: Int) {
                fun executeQuery(sql: String): List<String> = listOf()

                companion object Factory {
                    fun createDevelopment(): DatabaseConnection {
                        return DatabaseConnection("jdbc:h2:mem:test", 5)
                    }

                    fun createProduction(url: String): DatabaseConnection {
                        return DatabaseConnection(url, 50)
                    }
                }
            }

            class ApplicationLauncher {
                fun start() {
                    val conn = DatabaseConnection.createDevelopment()
                    conn.executeQuery("SELECT 1")
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Database.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var dbClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "DatabaseConnection");
        dbClass.Should().NotBeNull();

        var companionFactory = graph.Symbols.FirstOrDefault(s => s.Name == "Factory");
        companionFactory.Should().NotBeNull();

        var createDevMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "createDevelopment");
        createDevMethod.Should().NotBeNull();

        var startMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "start");
        startMethod.Should().NotBeNull();

        var queryMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "executeQuery");
        queryMethod.Should().NotBeNull();

        var startCalls = graph.GetOutgoingRelationships(startMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        startCalls.Should().Contain(r => r.TargetId == createDevMethod!.Id);
        startCalls.Should().Contain(r => r.TargetId == queryMethod!.Id);
    }

    #endregion

    #region Extension Functions and Properties Pattern

    /// <summary>
    /// Verifica l'estrazione e il call graph di Extension Functions ed Extension Properties,
    /// caratteristiche idiomatiche cardine di Kotlin per estendere classi senza ereditarietà.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldExtractExtensionFunctionsAndResolveInvocations()
    {
        // Arrange
        var code = """
            package com.acme.extensions

            class Money(val amount: Double, val currency: String)

            fun Money.formatted(): String {
                return "$currency $amount"
            }

            fun Money.applyTax(rate: Double): Money {
                return Money(amount * (1.0 + rate), currency)
            }

            class InvoiceReport {
                fun printInvoice(cost: Money): String {
                    val total = cost.applyTax(0.22)
                    return total.formatted()
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("MoneyExtensions.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var moneyClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "Money");
        moneyClass.Should().NotBeNull();

        var formattedMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "formatted");
        formattedMethod.Should().NotBeNull();

        var applyTaxMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "applyTax");
        applyTaxMethod.Should().NotBeNull();

        var printInvoice = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "printInvoice");
        printInvoice.Should().NotBeNull();

        var outgoing = graph.GetOutgoingRelationships(printInvoice!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        outgoing.Should().Contain(r => r.TargetId == applyTaxMethod!.Id);
        outgoing.Should().Contain(r => r.TargetId == formattedMethod!.Id);
    }

    #endregion

    #region Class Delegation via 'by' Keyword

    /// <summary>
    /// Verifica l'estrazione di classi Kotlin che utilizzano la delega nativa dell'implementazione
    /// tramite parola chiave 'by' (Decorator / Delegation Pattern a livello di compilatore).
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldExtractClassDelegationViaByClause()
    {
        // Arrange
        var code = """
            package com.acme.delegation

            interface DocumentPrinter {
                fun printDocument(title: String)
                fun getStatus(): String
            }

            class StandardPrinter : DocumentPrinter {
                override fun printDocument(title: String) { }
                override fun getStatus(): String = "READY"
            }

            class AuditedPrinter(private val delegate: DocumentPrinter) : DocumentPrinter by delegate {
                fun auditBeforePrint(title: String) {
                    recordAuditLog(title)
                    delegate.printDocument(title)
                }

                private fun recordAuditLog(name: String) { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Delegation.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var iface = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "DocumentPrinter");
        iface.Should().NotBeNull();

        var standardPrinter = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "StandardPrinter");
        standardPrinter.Should().NotBeNull();

        var auditedPrinter = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "AuditedPrinter");
        auditedPrinter.Should().NotBeNull();

        var inheritsRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == auditedPrinter!.Id &&
            r.TargetId == iface!.Id &&
            (r.Kind == RelationshipKind.Inherits || r.Kind == RelationshipKind.Implements));
        inheritsRel.Should().NotBeNull();

        var auditMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "auditBeforePrint");
        auditMethod.Should().NotBeNull();

        var recordAuditLog = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "recordAuditLog");
        recordAuditLog.Should().NotBeNull();

        var calls = graph.GetOutgoingRelationships(auditMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        calls.Should().Contain(r => r.TargetId == recordAuditLog!.Id);
    }

    #endregion

    #region Value Classes and Strongly Typed Identifiers

    /// <summary>
    /// Verifica la corretta gestione di @JvmInline value class (Domain-Driven Design primitive obsession avoidance).
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldExtractValueClassesAndMembers()
    {
        // Arrange
        var code = """
            package com.acme.domain

            @JvmInline
            value class AccountNumber(val rawValue: String) {
                fun isValid(): Boolean = rawValue.length == 10
            }

            class AccountManager {
                fun verify(account: AccountNumber): Boolean {
                    return account.isValid()
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("ValueClass.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var valueCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "AccountNumber");
        valueCls.Should().NotBeNull();

        var rawValueProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "rawValue");
        rawValueProp.Should().NotBeNull();

        var isValidMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "isValid");
        isValidMethod.Should().NotBeNull();

        var verifyMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "verify");
        verifyMethod.Should().NotBeNull();

        var calls = graph.GetOutgoingRelationships(verifyMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        calls.Should().Contain(r => r.TargetId == isValidMethod!.Id);
    }

    #endregion

    #region Custom Annotations and Metaprogramming

    /// <summary>
    /// Verifica l'estrazione di dichiarazioni di annotazioni custom ('annotation class') con parametri e default.
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldExtractCustomAnnotationClassesAndTargets()
    {
        // Arrange
        var code = """
            package com.acme.security

            @Target(AnnotationTarget.CLASS, AnnotationTarget.FUNCTION)
            @Retention(AnnotationRetention.RUNTIME)
            annotation class RequiresRole(val role: String, val level: Int = 1)

            @RequiresRole("ADMIN", level = 2)
            class AdminConsoleService {
                @RequiresRole("SUPER_ADMIN")
                fun wipeAllData() { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Annotations.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var annotClass = graph.Symbols.FirstOrDefault(s => s.Name == "RequiresRole");
        annotClass.Should().NotBeNull();

        var adminService = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "AdminConsoleService");
        adminService.Should().NotBeNull();

        var wipeMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "wipeAllData");
        wipeMethod.Should().NotBeNull();
    }

    #endregion

    #region DSL Builder Pattern with Lambda with Receiver

    /// <summary>
    /// Verifica l'estrazione di pattern DSL basati su lambda con receiver (T.() -> Unit), tipici di Kotlin (Ktor, Gradle, HTML builders).
    /// </summary>
    [Fact]
    public void KotlinExtractor_ShouldExtractTypeSafeDslBuilders()
    {
        // Arrange
        var code = """
            package com.acme.dsl

            class Tag(val name: String) {
                val children = mutableListOf<Tag>()
                fun addTag(tag: Tag) {
                    children.add(tag)
                }
            }

            class HtmlBuilder {
                val root = Tag("html")

                fun body(init: BodyBuilder.() -> Unit): Tag {
                    val body = BodyBuilder()
                    body.init()
                    root.addTag(body.tag)
                    return root
                }
            }

            class BodyBuilder {
                val tag = Tag("body")
                fun p(text: String) {
                    tag.addTag(Tag("p"))
                }
            }

            class DocumentRenderer {
                fun generate(): Tag {
                    val builder = HtmlBuilder()
                    return builder.body {
                        p("Hello World")
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("DslBuilders.kt", code);

        // Assert
        result.Success.Should().BeTrue();

        var htmlBuilderCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "HtmlBuilder");
        htmlBuilderCls.Should().NotBeNull();

        var bodyBuilderCls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "BodyBuilder");
        bodyBuilderCls.Should().NotBeNull();

        var generateMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "generate");
        generateMethod.Should().NotBeNull();

        var bodyMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "body");
        bodyMethod.Should().NotBeNull();

        var calls = graph.GetOutgoingRelationships(generateMethod!.Id).Where(r => r.Kind == RelationshipKind.Calls).ToList();
        calls.Should().Contain(r => r.TargetId == bodyMethod!.Id);
    }

    #endregion
}
