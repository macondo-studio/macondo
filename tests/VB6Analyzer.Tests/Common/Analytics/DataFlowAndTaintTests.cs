using FluentAssertions;
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class DataFlowAndTaintTests
{
    /// <summary>
    /// Verifica che un percorso da una sorgente non controllata (txtUserInput) verso un sink critico (ExecuteSql)
    /// venga rilevato come TaintPath con la corretta catena di propagazione.
    /// </summary>
    [Fact]
    public void DataFlow_ShouldDetectTaintPathFromSourceToSink()
    {
        // Arrange
        var graph = new CodeGraph();

        var txtInput = new Symbol { Id = Guid.NewGuid(), Name = "txtSearchInput", QualifiedName = "Form1.txtSearchInput", FilePath = "Form1.frm", Kind = SymbolKind.Field };
        var btnClick = new Symbol { Id = Guid.NewGuid(), Name = "btnSearch_Click", QualifiedName = "Form1.btnSearch_Click", FilePath = "Form1.frm", Kind = SymbolKind.Method };
        var helperMethod = new Symbol { Id = Guid.NewGuid(), Name = "RunSearchQuery", QualifiedName = "Service.RunSearchQuery", FilePath = "Service.bas", Kind = SymbolKind.Method };
        var sqlSink = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteSqlQuery", QualifiedName = "Db.ExecuteSqlQuery", FilePath = "Db.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { txtInput, btnClick, helperMethod, sqlSink });

        graph.AddReads(btnClick, txtInput);
        graph.AddCalls(btnClick, helperMethod);
        graph.AddCalls(helperMethod, sqlSink);

        // Act
        var dataset = TaintAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TaintPaths.Should().NotBeEmpty();

        var taint = dataset.TaintPaths.FirstOrDefault(p => p.SinkKind == TaintSinkKind.SqlExecution);
        taint.Should().NotBeNull();
        taint!.SourceSymbol.Name.Should().Be("txtSearchInput");
        taint.SinkSymbol.Name.Should().Be("ExecuteSqlQuery");
        taint.PropagationChain.Should().HaveCount(4);
    }

    /// <summary>
    /// Verifica il rilevamento di variabili con anomalie Dead Store (scritte ma mai lette).
    /// </summary>
    [Fact]
    public void DataFlow_ShouldDetectDeadStoreAnomaly()
    {
        // Arrange
        var graph = new CodeGraph();
        var deadVar = new Symbol { Id = Guid.NewGuid(), Name = "unusedState", QualifiedName = "ModuleA.unusedState", FilePath = "ModuleA.bas", Kind = SymbolKind.Field };
        var writerMethod = new Symbol { Id = Guid.NewGuid(), Name = "InitState", QualifiedName = "ModuleA.InitState", FilePath = "ModuleA.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { deadVar, writerMethod });
        graph.AddWrites(writerMethod, deadVar);

        // Act
        var dataset = TaintAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Anomalies.Should().Contain(a => a.AnomalyType == "DeadStore" && a.VariableSymbol.Name == "unusedState");
    }
}
