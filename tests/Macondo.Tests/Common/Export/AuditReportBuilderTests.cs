using FluentAssertions;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Export;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Export;

public class AuditReportBuilderTests
{
    [Fact]
    public void RunAll_ShouldExecuteAllAnalyticsAndPopulateResult()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var result = AuditReportBuilder.RunAll(graph);

        // Assert
        result.Should().NotBeNull();
        result.Graph.Should().BeSameAs(graph);
        result.DeadCode.Should().NotBeNull();
        result.Maintainability.Should().NotBeNull();
        result.Rules.Should().NotBeNull();
        result.Clones.Should().NotBeNull();
        result.Cohesion.Should().NotBeNull();
        result.Martin.Should().NotBeNull();
        result.SharedState.Should().NotBeNull();
        result.CrudMatrix.Should().NotBeNull();
        result.DataFlow.Should().NotBeNull();
        result.Dsm.Should().NotBeNull();
        result.ScreenFlow.Should().NotBeNull();
    }

    [Fact]
    public void RunAll_WithCustomRules_ShouldEvaluateSuppliedRules()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var customRules = new List<ArchitecturalRule>
        {
            new ArchitecturalRule
            {
                Name = "Test Custom Rule",
                Description = "Disallow all dependencies from any module to any other",
                Type = RuleType.ForbiddenDependency,
                Severity = RuleSeverity.Warning,
                SourceSelector = new ComponentSelector { Pattern = "*" },
                TargetSelector = new ComponentSelector { Pattern = "*" }
            }
        };

        // Act
        var result = AuditReportBuilder.RunAll(graph, customRules);

        // Assert
        result.Rules.Rules.Should().ContainSingle(r => r.Name == "Test Custom Rule");
    }

    [Fact]
    public void RunAll_WithProgressCallback_ShouldInvokeCallbackForPhases()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var messages = new List<string>();

        // Act
        var result = AuditReportBuilder.RunAll(graph, progressCallback: msg => messages.Add(msg));

        // Assert
        messages.Should().NotBeEmpty();
        messages.Should().Contain(m => m.Contains("Dead Code"));
        messages.Should().Contain(m => m.Contains("Maintainability"));
        messages.Should().Contain(m => m.Contains("DSM"));
    }
}
