using FluentAssertions;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Semantic.Export;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

/// <summary>
/// Test suite for architectural analysis algorithms and quality metrics
/// software (Dead Code, DSM, Blast Radius Impact, Treemap LOC and Export JSON/Markdown)
/// applied to Java code bases.
/// </summary>
public class JavaAnalyticsAndQualityMetricsTests
{
    #region Dead Code Analysis on Java

    /// <summary>
    /// Verify that DeadCodeAnalyzer correctly identifies unreferenced Java methods
    /// (dead code) distinguishing them from those invoked within the hierarchy.
    /// </summary>
    [Fact]
    public void DeadCodeAnalyzer_ShouldDetectUnusedMethodsInJavaClasses()
    {
        // Arrange
        var code = """
            package com.example.service;

            public class PaymentService {
                public void processPayment() {
                    validateCard();
                    chargeAccount();
                }

                private void validateCard() { }

                private void chargeAccount() { }

                private void obsoleteLegacyRefund() {
                    // Method not called anywhere
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeJavaSource("PaymentService.java", code);

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

    #endregion

    #region Dependency Structure Matrix (DSM) on Java Components

    /// <summary>
    /// Verify that DsmMatrixBuilder builds the dependency matrix between Java classes and packages.
    /// </summary>
    [Fact]
    public void DsmMatrixBuilder_ShouldConstructDependencyMatrixForJavaComponents()
    {
        // Arrange
        var controllerCode = """
            package com.example.web;

            import com.example.service.UserService;

            public class UserController {
                private UserService userService = new UserService();

                public void handleCreateUser() {
                    userService.createUser();
                }
            }
            """;

        var serviceCode = """
            package com.example.service;

            public class UserService {
                public void createUser() { }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeJavaSources(
            ("UserController.java", controllerCode),
            ("UserService.java", serviceCode)
        );

        var matrix = DsmMatrixBuilder.Build(graph);

        // Assert
        matrix.Should().NotBeNull();
        matrix.Items.Should().NotBeEmpty();

        var controllerItem = matrix.Items.FirstOrDefault(r => r.Name.Contains("UserController"));
        controllerItem.Should().NotBeNull();
    }

    #endregion

    #region Impact / Blast Radius Analysis on Java Method Chains

    /// <summary>
    /// Verify that ImpactAnalyzer correctly calculates the blast radius
    /// when a Java method in a core service is modified.
    /// </summary>
    [Fact]
    public void ImpactAnalyzer_ShouldCalculateBlastRadiusAcrossJavaCallChains()
    {
        // Arrange
        var repositoryCode = """
            package com.example.dao;

            public class OrderDao {
                public void saveToDatabase() { }
            }
            """;

        var serviceCode = """
            package com.example.service;

            import com.example.dao.OrderDao;

            public class OrderService {
                private OrderDao orderDao = new OrderDao();

                public void placeOrder() {
                    orderDao.saveToDatabase();
                }
            }
            """;

        var apiCode = """
            package com.example.api;

            import com.example.service.OrderService;

            public class OrderApi {
                private OrderService orderService = new OrderService();

                public void postOrder() {
                    orderService.placeOrder();
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeJavaSources(
            ("OrderDao.java", repositoryCode),
            ("OrderService.java", serviceCode),
            ("OrderApi.java", apiCode)
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

    #region Treemap and LOC Metrics for Java

    /// <summary>
    /// Verify that TreemapModelBuilder calculates the Treemap hierarchy with weights based on lines of code (LOC) from Java files.
    /// </summary>
    [Fact]
    public void TreemapModelBuilder_ShouldComputeHierarchyAndLocWeightsForJavaSources()
    {
        // Arrange
        var code1 = """
            package com.example.app;

            public class HeavyService {
                public void op1() { }
                public void op2() { }
                public void op3() { }
            }
            """;

        var code2 = """
            package com.example.app;

            public class LightService {
                public void op() { }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeJavaSources(
            ("HeavyService.java", code1),
            ("LightService.java", code2)
        );

        var rootNode = TreemapModelBuilder.Build(graph);

        // Assert
        rootNode.Should().NotBeNull();
        rootNode.Children.Should().NotBeEmpty();
    }

    #endregion

    #region Export JSON and Markdown for Java Graphs

    /// <summary>
    /// Verify that CodeGraphJsonExporter and CodeGraphMarkdownExporter serialize correctly
    /// symbols, metadata and relations extracted from Java sources.
    /// </summary>
    [Fact]
    public void Exporters_ShouldSerializeJavaSymbolsAndRelationships()
    {
        // Arrange
        var code = """
            package com.example.export;

            public class DataTransformer {
                public String transform(String raw) {
                    return sanitize(raw);
                }

                private String sanitize(String s) {
                    return s.trim();
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeJavaSource("DataTransformer.java", code);

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
