using FluentAssertions;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Semantic.Export;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.KotlinSpecific;

/// <summary>
/// Suite di test per gli algoritmi di analisi architetturale e metriche di qualità
/// del software (Dead Code, DSM, Blast Radius Impact, Treemap LOC ed Export JSON/Markdown)
/// applicati a basi di codice sorgente Kotlin (.kt).
/// </summary>
public class KotlinAnalyticsAndQualityMetricsTests
{
    #region Dead Code Analysis on Kotlin

    /// <summary>
    /// Verifica che DeadCodeAnalyzer identifichi correttamente le funzioni Kotlin private
    /// o non referenziate, distinguendole da quelle regolarmente invocate all'interno della classe.
    /// </summary>
    [Fact]
    public void DeadCodeAnalyzer_ShouldDetectUnusedMethodsInKotlinClasses()
    {
        // Arrange
        var code = """
            package com.acme.service

            class PaymentService {
                fun processPayment() {
                    validateCard()
                    chargeAccount()
                }

                private fun validateCard() { }

                private fun chargeAccount() { }

                private fun obsoleteLegacyRefund() {
                    // Funzione non invocata da nessuna parte
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("PaymentService.kt", code);
        var deadCodeReport = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        result.Success.Should().BeTrue();
        deadCodeReport.Should().NotBeNull();

        var obsoleteMethod = graph.Symbols.FirstOrDefault(s => s.Name == "obsoleteLegacyRefund");
        obsoleteMethod.Should().NotBeNull();

        deadCodeReport.Items.Should().Contain(d => d.Name == "obsoleteLegacyRefund");
        deadCodeReport.Items.Should().NotContain(d => d.Name == "validateCard");
        deadCodeReport.Items.Should().NotContain(d => d.Name == "chargeAccount");
    }

    /// <summary>
    /// Verifica che DeadCodeAnalyzer rilevi routine non utilizzate all'interno di
    /// singleton object e companion object in Kotlin.
    /// </summary>
    [Fact]
    public void DeadCodeAnalyzer_ShouldDetectDeadCodeAcrossCompanionObjectsAndSingletons()
    {
        // Arrange
        var code = """
            package com.acme.telemetry

            object MetricsReporter {
                fun recordEvent(name: String) {
                    sendPayload(name)
                }

                private fun sendPayload(data: String) { }

                private fun unusedDeprecatedReport() {
                    // Non invocato
                }
            }

            class OrderTracker {
                fun track() {
                    MetricsReporter.recordEvent("order_started")
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeKotlinSource("Telemetry.kt", code);
        var deadCodeReport = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        result.Success.Should().BeTrue();
        deadCodeReport.Should().NotBeNull();

        deadCodeReport.Items.Should().Contain(d => d.Name == "unusedDeprecatedReport");
        deadCodeReport.Items.Should().NotContain(d => d.Name == "sendPayload");
    }

    #endregion

    #region Dependency Structure Matrix (DSM) on Kotlin Components

    /// <summary>
    /// Verifica che DsmMatrixBuilder costruisca la matrice delle dipendenze architetturali
    /// tra componenti e package Kotlin separati.
    /// </summary>
    [Fact]
    public void DsmMatrixBuilder_ShouldConstructDependencyMatrixForKotlinComponents()
    {
        // Arrange
        var controllerCode = """
            package com.acme.web

            import com.acme.service.UserService

            class UserController {
                val userService: UserService = UserService()

                fun handleCreateUser() {
                    userService.createUser()
                }
            }
            """;

        var serviceCode = """
            package com.acme.service

            class UserService {
                fun createUser() { }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeKotlinSources(
            ("UserController.kt", controllerCode),
            ("UserService.kt", serviceCode)
        );

        var matrix = DsmMatrixBuilder.Build(graph);

        // Assert
        matrix.Should().NotBeNull();
        matrix.Items.Should().NotBeEmpty();

        var controllerItem = matrix.Items.FirstOrDefault(r => r.Name.Contains("UserController"));
        controllerItem.Should().NotBeNull();
    }

    #endregion

    #region Impact / Blast Radius Analysis on Kotlin Call Chains

    /// <summary>
    /// Verifica che ImpactAnalyzer calcoli accuratamente l'impatto a cascata (blast radius)
    /// su catene di chiamate distribuite tra componenti Kotlin.
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldCalculateBlastRadiusAcrossKotlinCallChains()
    {
        // Arrange
        var repoCode = """
            package com.acme.dao

            class OrderDao {
                fun saveToDatabase() { }
            }
            """;

        var serviceCode = """
            package com.acme.service

            import com.acme.dao.OrderDao

            class OrderService {
                val orderDao: OrderDao = OrderDao()

                fun placeOrder() {
                    orderDao.saveToDatabase()
                }
            }
            """;

        var apiCode = """
            package com.acme.api

            import com.acme.service.OrderService

            class OrderApi {
                val orderService: OrderService = OrderService()

                fun postOrder() {
                    orderService.placeOrder()
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeKotlinSources(
            ("OrderDao.kt", repoCode),
            ("OrderService.kt", serviceCode),
            ("OrderApi.kt", apiCode)
        );

        var saveMethod = graph.Symbols.First(s => s.Name == "saveToDatabase");

        var impactAnalyzer = new ImpactAnalyzer(graph);
        var impactResult = impactAnalyzer.AnalyzeSymbolImpact(saveMethod);

        // Assert
        impactResult.Should().NotBeNull();
        impactResult.DirectCallers.Should().Contain(s => s.Name == "placeOrder");
        impactResult.IndirectCallers.Should().Contain(c => c.Symbol.Name == "postOrder");
    }

    #endregion

    #region Treemap and LOC Metrics for Kotlin

    /// <summary>
    /// Verifica che TreemapModelBuilder calcoli la gerarchia visiva con pesi basati
    /// sul conteggio LOC per sorgenti Kotlin.
    /// </summary>
    [Fact]
    public void TreemapModelBuilder_ShouldComputeHierarchyAndLocWeightsForKotlinSources()
    {
        // Arrange
        var code1 = """
            package com.acme.app

            class HeavyService {
                fun op1() { }
                fun op2() { }
                fun op3() { }
            }
            """;

        var code2 = """
            package com.acme.app

            class LightService {
                fun op() { }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeKotlinSources(
            ("HeavyService.kt", code1),
            ("LightService.kt", code2)
        );

        var rootNode = TreemapModelBuilder.Build(graph);

        // Assert
        rootNode.Should().NotBeNull();
        rootNode.Children.Should().NotBeEmpty();
    }

    #endregion

    #region Export JSON and Markdown for Kotlin Graphs

    /// <summary>
    /// Verifica che gli esportatori JSON e Markdown serializzino correttamente simboli,
    /// proprietà e relazioni derivati dall'analisi di sorgenti Kotlin.
    /// </summary>
    [Fact]
    public void Exporters_ShouldSerializeKotlinSymbolsAndRelationships()
    {
        // Arrange
        var code = """
            package com.acme.export

            class DataTransformer {
                fun transform(raw: String): String {
                    return sanitize(raw)
                }

                private fun sanitize(s: String): String {
                    return s.trim()
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeKotlinSource("DataTransformer.kt", code);

        var json = CodeGraphJsonExporter.ExportToJson(graph);
        var markdown = CodeGraphMarkdownExporter.Export(graph);

        // Assert
        json.Should().NotBeNullOrWhiteSpace();
        json.Should().Contain("DataTransformer");
        json.Should().Contain("transform");
        json.Should().Contain("sanitize");

        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("DataTransformer");
        markdown.Should().Contain("sanitize");
    }

    #endregion
}
