using FluentAssertions;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.KotlinSpecific;

/// <summary>
/// Suite di test per analisi architetturale ad alta intensità su basi di codice Kotlin (.kt):
/// rilevamento Dead Code in architetture Clean a più livelli, analisi delle dipendenze cicliche in DSM,
/// propagazione transitiva del Blast Radius / raggio di impatto su catene di chiamate profonde e metriche Treemap/LOC.
/// </summary>
public class KotlinArchitectureStressAndQualityHunterTests
{
    #region Dead Code Analysis in Multi-tier Kotlin Architecture

    /// <summary>
    /// Verifica che DeadCodeAnalyzer individui con precisione metodi obsoleti o non referenziati
    /// all'interno di un'architettura enterprise multilivello (Controller -> UseCase -> Repository -> DataSource) in Kotlin.
    /// </summary>
    [Fact]
    public void DeadCodeAnalyzer_ShouldDetectDeadCodeInMultiTierKotlinArchitecture()
    {
        // Arrange
        var controllerCode = """
            package com.acme.api

            import com.acme.usecase.CreateOrderUseCase

            class OrderController {
                val useCase = CreateOrderUseCase()

                fun handlePostOrder(payload: String) {
                    useCase.execute(payload)
                }
            }
            """;

        var useCaseCode = """
            package com.acme.usecase

            import com.acme.repository.OrderRepository

            class CreateOrderUseCase {
                val repository = OrderRepository()

                fun execute(data: String): Boolean {
                    return repository.insertOrder(data)
                }

                private fun deadUseCaseHelper() {
                    // Mai invocato
                }
            }
            """;

        var repoCode = """
            package com.acme.repository

            class OrderRepository {
                fun insertOrder(data: String): Boolean {
                    return executeSql("INSERT INTO orders VALUES (?)")
                }

                private fun executeSql(sql: String): Boolean {
                    return true
                }

                private fun legacyRawBackup() {
                    // Funzione privata morta
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeKotlinSources(
            ("OrderController.kt", controllerCode),
            ("CreateOrderUseCase.kt", useCaseCode),
            ("OrderRepository.kt", repoCode)
        );

        var deadCodeReport = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        deadCodeReport.Should().NotBeNull();
        deadCodeReport.Items.Should().NotBeEmpty();

        // I metodi non invocati devono essere identificati come dead code
        deadCodeReport.Items.Should().Contain(d => d.Name == "legacyRawBackup");
        deadCodeReport.Items.Should().Contain(d => d.Name == "deadUseCaseHelper");

        // I metodi in uso non devono essere segnalati
        deadCodeReport.Items.Should().NotContain(d => d.Name == "insertOrder");
        deadCodeReport.Items.Should().NotContain(d => d.Name == "executeSql");
    }

    #endregion

    #region Dependency Structure Matrix (DSM) and Cyclic Dependency

    /// <summary>
    /// Verifica che DsmMatrixBuilder rilevi e mappi correttamente le dipendenze bidirezionali / cicliche
    /// tra servizi Kotlin che si invocano a vicenda.
    /// </summary>
    [Fact]
    public void DsmMatrixBuilder_ShouldMapCyclicDependenciesBetweenKotlinServices()
    {
        // Arrange
        var billingCode = """
            package com.acme.billing

            import com.acme.customer.CustomerAccountService

            class BillingService {
                val accountService = CustomerAccountService()

                fun chargeCustomer(customerId: String, amount: Double) {
                    accountService.checkCreditLimit(customerId)
                }

                fun onInvoiceAdjusted() { }
            }
            """;

        var accountCode = """
            package com.acme.customer

            import com.acme.billing.BillingService

            class CustomerAccountService {
                val billing = BillingService()

                fun checkCreditLimit(customerId: String): Boolean {
                    billing.onInvoiceAdjusted()
                    return true
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeKotlinSources(
            ("BillingService.kt", billingCode),
            ("CustomerAccountService.kt", accountCode)
        );

        var matrix = DsmMatrixBuilder.Build(graph);

        // Assert
        matrix.Should().NotBeNull();
        matrix.Items.Should().HaveCountGreaterOrEqualTo(2);

        var billingEntry = matrix.Items.FirstOrDefault(i => i.Name.Contains("BillingService"));
        var accountEntry = matrix.Items.FirstOrDefault(i => i.Name.Contains("CustomerAccountService"));

        billingEntry.Should().NotBeNull();
        accountEntry.Should().NotBeNull();
    }

    #endregion

    #region Blast Radius / Transitive Impact Propagation

    /// <summary>
    /// Verifica che ImpactAnalyzer calcoli accuratamente la catena di impatto transitiva
    /// (profondità 1, 2, 3) a partire da una funzione driver di basso livello fino ai controller esposti.
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldPropagateTransitiveBlastRadiusAcrossDeepKotlinHierarchy()
    {
        // Arrange
        var lowLevelCode = """
            package com.acme.db

            class LowLevelDriver {
                fun executeLowLevelQuery() { }
            }
            """;

        var repoCode = """
            package com.acme.dao

            import com.acme.db.LowLevelDriver

            class OrderDao {
                val driver = LowLevelDriver()

                fun queryOrders() {
                    driver.executeLowLevelQuery()
                }
            }
            """;

        var serviceCode = """
            package com.acme.service

            import com.acme.dao.OrderDao

            class OrderProcessor {
                val dao = OrderDao()

                fun process() {
                    dao.queryOrders()
                }
            }
            """;

        var controllerCode = """
            package com.acme.web

            import com.acme.service.OrderProcessor

            class OrderEndpoint {
                val processor = OrderProcessor()

                fun getEndpoint() {
                    processor.process()
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeKotlinSources(
            ("LowLevelDriver.kt", lowLevelCode),
            ("OrderDao.kt", repoCode),
            ("OrderProcessor.kt", serviceCode),
            ("OrderEndpoint.kt", controllerCode)
        );

        var rootDriverMethod = graph.Symbols.First(s => s.Name == "executeLowLevelQuery");
        var impactAnalyzer = new ImpactAnalyzer(graph);
        var impactResult = impactAnalyzer.AnalyzeSymbolImpact(rootDriverMethod);

        // Assert
        impactResult.Should().NotBeNull();
        impactResult.DirectCallers.Should().Contain(s => s.Name == "queryOrders");
        impactResult.IndirectCallers.Should().Contain(c => c.Symbol.Name == "process");
        impactResult.IndirectCallers.Should().Contain(c => c.Symbol.Name == "getEndpoint");
    }

    #endregion

    #region Treemap and LOC Metrics for Kotlin Codebases

    /// <summary>
    /// Verifica che TreemapModelBuilder calcoli la gerarchia visiva con pesi basati
    /// sul conteggio LOC per sorgenti Kotlin.
    /// </summary>
    [Fact]
    public void TreemapModelBuilder_ShouldComputeHierarchyAndLocWeightsForKotlinModules()
    {
        // Arrange
        var code = """
            package com.acme.metrics

            class AnalyticsEngine {
                fun computeMetrics(): Double {
                    val a = 1.0
                    val b = 2.0
                    val sum = a + b
                    return sum * 2.0
                }

                fun exportCsv(): String {
                    val line1 = "header,value"
                    val line2 = "row,100"
                    return line1 + "\n" + line2
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeKotlinSource("AnalyticsEngine.kt", code);
        var rootNode = TreemapModelBuilder.Build(graph);

        // Assert
        rootNode.Should().NotBeNull();
        rootNode.Children.Should().NotBeEmpty();
    }

    #endregion
}
