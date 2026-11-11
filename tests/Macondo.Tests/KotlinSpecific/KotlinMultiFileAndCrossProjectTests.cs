using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.KotlinSpecific;

/// <summary>
/// Test suite for semantic resolution of call graph,inheritance hierarchies
/// and radius of impact across multiple Kotlin (.kt) source files and polyglot scenarios.
/// </summary>
public class KotlinMultiFileAndCrossProjectTests
{
    /// <summary>
    /// In Kotlin, files belonging to the same package do not require import declarations.
    /// Verify that CallGraphBuilder correctly links calls between classes in separate files in the same package.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldResolveCallsAcrossMultipleKotlinFilesInSamePackage()
    {
        // Arrange
        var file1 = """
            package com.acme.shop

            class OrderService {
                val gateway: PaymentGateway = PaymentGateway()

                fun placeOrder() {
                    gateway.process()
                }
            }
            """;

        var file2 = """
            package com.acme.shop

            class PaymentGateway {
                fun process() {
                    logAudit()
                }

                private fun logAudit() { }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeKotlinSources(
            ("OrderService.kt", file1),
            ("PaymentGateway.kt", file2)
        );

        // Assert
        results.Should().HaveCount(2);

        var placeOrderMethod = graph.Symbols.First(s => s.Name == "placeOrder");
        var processMethod = graph.Symbols.First(s => s.Name == "process");
        var auditMethod = graph.Symbols.First(s => s.Name == "logAudit");

        var callProcessRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == placeOrderMethod.Id &&
            r.TargetId == processMethod.Id &&
            r.Kind == RelationshipKind.Calls);
        callProcessRel.Should().NotBeNull();

        var callAuditRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == processMethod.Id &&
            r.TargetId == auditMethod.Id &&
            r.Kind == RelationshipKind.Calls);
        callAuditRel.Should().NotBeNull();
    }

    /// <summary>
    /// Verify that CallGraphBuilder resolves invocations between Kotlin files in different packages
    /// via explicit imports.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldResolveCallsAcrossDifferentPackagesWithImports()
    {
        // Arrange
        var taxFile = """
            package com.acme.tax

            class TaxEngine {
                fun calculate(amount: Double): Double = amount * 0.22
            }
            """;

        var invoiceFile = """
            package com.acme.billing

            import com.acme.tax.TaxEngine

            class InvoiceService {
                val taxEngine: TaxEngine = TaxEngine()

                fun generateInvoice(baseAmount: Double): Double {
                    return taxEngine.calculate(baseAmount)
                }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeKotlinSources(
            ("TaxEngine.kt", taxFile),
            ("InvoiceService.kt", invoiceFile)
        );

        // Assert
        results.Should().HaveCount(2);

        var generateInvoice = graph.Symbols.First(s => s.Name == "generateInvoice");
        var calculate = graph.Symbols.First(s => s.Name == "calculate");

        var callRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == generateInvoice.Id &&
            r.TargetId == calculate.Id &&
            r.Kind == RelationshipKind.Calls);
        callRel.Should().NotBeNull();
    }

    /// <summary>
    /// Verify that inheritance (open class) and interface implementation hierarchies
    /// declared in separate Kotlin files are linked with INHERITS and IMPLEMENTS relationships.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldResolveInheritanceAndInterfacesAcrossMultipleKotlinFiles()
    {
        // Arrange
        var contractsFile = """
            package com.acme.domain

            interface Notifier {
                fun notifyUser(message: String)
            }

            open class BaseService {
                open fun logOperation(op: String) { }
            }
            """;

        var implFile = """
            package com.acme.domain

            class EmailNotifierService : BaseService(), Notifier {
                override fun notifyUser(message: String) {
                    logOperation("Sent: " + message)
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeKotlinSources(
            ("Contracts.kt", contractsFile),
            ("EmailNotifierService.kt", implFile)
        );

        // Assert
        var baseClass = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "BaseService");
        var notifierInterface = graph.Symbols.First(s => s.Kind == SymbolKind.Interface && s.Name == "Notifier");
        var implClass = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "EmailNotifierService");

        var inheritsRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == implClass.Id &&
            r.TargetId == baseClass.Id &&
            r.Kind == RelationshipKind.Inherits);
        inheritsRel.Should().NotBeNull();

        var implementsRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == implClass.Id &&
            r.TargetId == notifierInterface.Id &&
            r.Kind == RelationshipKind.Implements);
        implementsRel.Should().NotBeNull();
    }

    /// <summary>
    /// Verify that BlastRadiusEngine properly cascades impact across projects
    /// multi-file in Kotlin.
    /// </summary>
    [Fact]
    public void BlastRadiusEngine_ShouldCalculateImpactOnMultiFileKotlinProjects()
    {
        // Arrange
        var fileApp = """
            package com.acme.app

            class Application {
                val controller: Controller = Controller()

                fun start() {
                    controller.handleRequest()
                }
            }
            """;

        var fileCtrl = """
            package com.acme.app

            class Controller {
                val db: Database = Database()

                fun handleRequest() {
                    db.save()
                }
            }
            """;

        var fileDb = """
            package com.acme.app

            class Database {
                fun save() { }
            }
            """;

        // Act
        var (graph, results) = TestGraphFactory.CreateAndAnalyzeKotlinSources(
            ("Application.kt", fileApp),
            ("Controller.kt", fileCtrl),
            ("Database.kt", fileDb)
        );

        var saveMethod = graph.Symbols.First(s => s.Name == "save");
        var impactAnalyzer = new ImpactAnalyzer(graph);
        var impact = impactAnalyzer.AnalyzeSymbolImpact(saveMethod);

        // Assert
        impact.DirectCallers.Should().ContainSingle(c => c.Name == "handleRequest");
        impact.IndirectCallers.Should().ContainSingle(c => c.Symbol.Name == "start");
        impact.AffectedClasses.Select(c => c.Name).Should().Contain(new[] { "Database", "Controller", "Application" });
    }

    /// <summary>
    /// Test combined polyglot analysis between Kotlin sources (.kt) and Java sources (.java).
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldSupportMixedPolyglotKotlinAndJava()
    {
        // Arrange
        var graph = new CodeGraph();
        var javaCode = """
            package com.acme.security;

            public class JavaSecurityVault {
                public void unlock(String key) { }
            }
            """;

        var kotlinCode = """
            package com.acme.bridge

            import com.acme.security.JavaSecurityVault

            class KotlinKeyManager {
                val vault: JavaSecurityVault = JavaSecurityVault()

                fun accessVault() {
                    vault.unlock("secret-passphrase")
                }
            }
            """;

        // Act
        var javaResult = JavaExtractor.ExtractJavaFile(graph, "JavaSecurityVault.java", javaCode);
        var kotlinResult = KotlinExtractor.ExtractKotlinFile(graph, "KotlinKeyManager.kt", kotlinCode);

        var typeResolver = new Semantic.Resolution.TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new Semantic.Resolution.SymbolResolver(graph, typeResolver);

        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(Array.Empty<ModuleExtractionResult>(), null, new[] { javaResult }, new[] { kotlinResult });

        // Assert
        var accessVault = graph.Symbols.First(s => s.Name == "accessVault");
        var unlock = graph.Symbols.First(s => s.Name == "unlock");

        var callRel = graph.Relationships.FirstOrDefault(r =>
            r.SourceId == accessVault.Id &&
            r.TargetId == unlock.Id &&
            r.Kind == RelationshipKind.Calls);
        callRel.Should().NotBeNull();
    }
}
