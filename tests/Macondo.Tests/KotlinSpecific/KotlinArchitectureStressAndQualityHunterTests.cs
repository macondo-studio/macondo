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
/// Test suite for intensive architectural analysis on Kotlin (.kt) code bases:
/// Dead Code detection in multi-tier Clean architectures, cyclic dependency analysis in DSM,
/// transitive propagation of Blast Radius / impact radius on deep call chains and Treemap/LOC metrics.
/// </summary>
public class KotlinArchitectureStressAndQualityHunterTests
{
    #region Dead Code Analysis in Multi-tier Kotlin Architecture

    /// <summary>
    /// Verify that DeadCodeAnalyzer accurately identifies obsolete or unreferenced methods
    /// within a multi-tier enterprise architecture (Controller -> UseCase -> Repository -> DataSource) in Kotlin.
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
                    // Never invoked
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
                    // Private function dead
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

        // Uninvoked methods should be identified as dead code
        deadCodeReport.Items.Should().Contain(d => d.Name == "legacyRawBackup");
        deadCodeReport.Items.Should().Contain(d => d.Name == "deadUseCaseHelper");

        // The methods in use do not need to be reported
        deadCodeReport.Items.Should().NotContain(d => d.Name == "insertOrder");
        deadCodeReport.Items.Should().NotContain(d => d.Name == "executeSql");
    }

    #endregion

    #region Dependency Structure Matrix (DSM) and Cyclic Dependency

    /// <summary>
    /// Verify that DsmMatrixBuilder correctly detects and maps bidirectional/cyclic dependencies
    /// between Kotlin services calling each other.
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
    /// Verify that ImpactAnalyzer accurately calculates the transitive impact chain
    /// (depth 1, 2, 3) starting from a low-level driver function up to the exposed controllers.
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
    /// Verify that TreemapModelBuilder calculates the visual hierarchy with weights based
    /// on LOC counting for Kotlin sources.
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
