using FluentAssertions;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class ArchitecturalRulesTests
{
    /// <summary>
    /// Verify that a ForbiddenDependency rule reports correctly
    /// a violation when the Domain attempts to access the Infrastructure or UI.
    /// </summary>
    [Fact]
    public void ArchitecturalRules_ShouldDetectForbiddenLayerDependency()
    {
        // Arrange
        var graph = new CodeGraph();

        var domainEntity = new Symbol { Id = Guid.NewGuid(), Name = "CustomerModel", QualifiedName = "Domain.CustomerModel", FilePath = "Domain/Customer.cs", Kind = SymbolKind.Class };
        var domainMethod = new Symbol { Id = Guid.NewGuid(), Name = "CalculateTotal", QualifiedName = "Domain.CustomerModel.CalculateTotal", FilePath = "Domain/Customer.cs", Kind = SymbolKind.Method };

        var sqlRepository = new Symbol { Id = Guid.NewGuid(), Name = "SqlRepository", QualifiedName = "DataAccess.SqlRepository", FilePath = "DataAccess/SqlRepo.cs", Kind = SymbolKind.Class };
        var repoMethod = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteRawQuery", QualifiedName = "DataAccess.SqlRepository.ExecuteRawQuery", FilePath = "DataAccess/SqlRepo.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { domainEntity, domainMethod, sqlRepository, repoMethod });
        graph.AddContains(domainEntity, domainMethod);
        graph.AddContains(sqlRepository, repoMethod);

        // Violation: Domain calls low-level repository directly
        graph.AddCalls(domainMethod, repoMethod);

        var rule = new ArchitecturalRule
        {
            Name = "Domain Isolation",
            Type = RuleType.ForbiddenDependency,
            Severity = RuleSeverity.Critical,
            SourceSelector = new ComponentSelector { Name = "Domain", Pattern = "*Domain*" },
            TargetSelector = new ComponentSelector { Name = "DataAccess", Pattern = "*DataAccess*;*Sql*" }
        };

        // Act
        var report = ArchitecturalRulesEngine.Evaluate(graph, new[] { rule });

        // Assert
        report.Should().NotBeNull();
        report.TotalViolations.Should().Be(1);
        report.Violations[0].Rule.Name.Should().Be("Domain Isolation");
        report.Violations[0].SourceSymbol.Name.Should().Be("CalculateTotal");
        report.Violations[0].TargetSymbol.Name.Should().Be("ExecuteRawQuery");
    }

    /// <summary>
    /// Check the evaluation of the default default rules.
    /// </summary>
    [Fact]
    public void ArchitecturalRules_ShouldProvideAndEvaluateDefaultRules()
    {
        // Arrange
        var graph = new CodeGraph();
        var form = new Symbol { Id = Guid.NewGuid(), Name = "frmMain", QualifiedName = "frmMain", FilePath = "frmMain.frm", Kind = SymbolKind.Form };
        var repo = new Symbol { Id = Guid.NewGuid(), Name = "AdoRepository", QualifiedName = "AdoRepository", FilePath = "AdoRepository.bas", Kind = SymbolKind.Module };
        graph.Symbols.AddRange(new[] { form, repo });
        graph.AddCalls(form, repo);

        // Act
        var report = ArchitecturalRulesEngine.Evaluate(graph);

        // Assert
        report.Should().NotBeNull();
        report.Rules.Should().NotBeEmpty();
        report.Violations.Should().Contain(v => v.Rule.Name.Contains("No Direct DB Access"));
    }
}
