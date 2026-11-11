using FluentAssertions;
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class DataFlowAndTaintExtendedTests
{
    /// <summary>
    /// Check that when multiple UI controls (e.g. textbox and combobox)
    /// converge on the same SQL query method, multiple distinct TaintPaths are generated.
    /// </summary>
    [Fact]
    public void DataFlow_ShouldDetectMultipleDistinctTaintSourcesConvergingOnSameSink()
    {
        // Arrange
        var graph = new CodeGraph();

        var txtUser = new Symbol { Id = Guid.NewGuid(), Name = "txtUsername", QualifiedName = "LoginForm.txtUsername", FilePath = "LoginForm.frm", Kind = SymbolKind.Field };
        var txtPass = new Symbol { Id = Guid.NewGuid(), Name = "txtPassword", QualifiedName = "LoginForm.txtPassword", FilePath = "LoginForm.frm", Kind = SymbolKind.Field };

        var btnLogin = new Symbol { Id = Guid.NewGuid(), Name = "btnLogin_Click", QualifiedName = "LoginForm.btnLogin_Click", FilePath = "LoginForm.frm", Kind = SymbolKind.Method };
        var authService = new Symbol { Id = Guid.NewGuid(), Name = "Authenticate", QualifiedName = "AuthService.Authenticate", FilePath = "AuthService.cs", Kind = SymbolKind.Method };
        var sqlSink = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteSqlQuery", QualifiedName = "Db.ExecuteSqlQuery", FilePath = "Db.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { txtUser, txtPass, btnLogin, authService, sqlSink });

        // btnLogin reads both fields
        graph.AddReads(btnLogin, txtUser);
        graph.AddReads(btnLogin, txtPass);

        // btnLogin calls authService which calls sqlSink
        graph.AddCalls(btnLogin, authService);
        graph.AddCalls(authService, sqlSink);

        // Act
        var dataset = TaintAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TaintPaths.Should().HaveCount(2);
        dataset.TaintPaths.Select(p => p.SourceSymbol.Name).Should().Contain(new[] { "txtUsername", "txtPassword" });
        dataset.TaintPaths.Should().OnlyContain(p => p.SinkSymbol.Name == "ExecuteSqlQuery");
    }

    /// <summary>
    /// Verify that a CodeGraph without symbols or edges returns an empty dataset without errors.
    /// </summary>
    [Fact]
    public void DataFlow_ShouldHandleEmptyGraph_Gracefully()
    {
        // Arrange
        var emptyGraph = new CodeGraph();

        // Act
        var dataset = TaintAnalyzer.Analyze(emptyGraph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TaintPaths.Should().BeEmpty();
        dataset.Anomalies.Should().BeEmpty();
    }
}
