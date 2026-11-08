using System.Text.Json;
using FluentAssertions;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Analytics.CodeClones;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Maintainability;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Analytics.ScreenFlow;
using Macondo.Semantic.Analytics.SharedState;
using Macondo.Semantic.Export;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Export;

public class ExportAndSerializationComprehensiveEdgeCasesTests
{
    #region Exporting Empty Graphs Across All Analyzers

    /// <summary>
    /// Verifica che tutti gli esportatori Markdown e JSON gestiscano un CodeGraph completamente vuoto
    /// senza sollevare eccezioni NullReferenceException o formattare markdown malformato.
    /// </summary>
    [Fact]
    public void MarkdownAndJsonExporters_ShouldHandleEmptyGraphAcrossAllAnalyses()
    {
        // Arrange
        var emptyGraph = new CodeGraph();

        var deadCode = DeadCodeAnalyzer.Analyze(emptyGraph);
        var maintainability = MaintainabilityCalculator.Analyze(emptyGraph);
        var cohesion = CohesionCalculator.Analyze(emptyGraph);
        var martin = MartinMetricsCalculator.Analyze(emptyGraph);
        var sharedState = SharedStateAnalyzer.Analyze(emptyGraph);
        var crud = CrudMatrixAnalyzer.Analyze(emptyGraph);
        var taint = TaintAnalyzer.Analyze(emptyGraph);
        var screenFlow = ScreenFlowBuilder.Build(emptyGraph);
        var dsm = DsmMatrixBuilder.Build(emptyGraph);
        var clones = CodeCloneDetector.DetectClones(emptyGraph);
        var arch = ArchitecturalRulesEngine.Evaluate(emptyGraph);

        // Act - Markdown exports
        var mdDeadCode = AnalysisMarkdownExporter.ExportDeadCode(deadCode);
        var mdMaintainability = AnalysisMarkdownExporter.ExportMaintainability(maintainability);
        var mdCohesion = AnalysisMarkdownExporter.ExportCohesion(cohesion);
        var mdMartin = AnalysisMarkdownExporter.ExportMartinMetrics(martin);
        var mdShared = AnalysisMarkdownExporter.ExportSharedState(sharedState);
        var mdCrud = AnalysisMarkdownExporter.ExportCrudMatrix(crud);
        var mdTaint = AnalysisMarkdownExporter.ExportDataFlow(taint);
        var mdScreenFlow = AnalysisMarkdownExporter.ExportScreenFlow(screenFlow);
        var mdDsm = AnalysisMarkdownExporter.ExportDsm(dsm);
        var mdClones = AnalysisMarkdownExporter.ExportCodeClones(clones);
        var mdArch = AnalysisMarkdownExporter.ExportArchitecturalRules(arch);

        // Act - JSON exports
        var jsonDeadCode = AnalysisJsonExporter.ExportToJson(deadCode);
        var jsonMaintainability = AnalysisJsonExporter.ExportToJson(maintainability);
        var jsonDsm = AnalysisJsonExporter.ExportDsmToJson(dsm);

        // Assert
        mdDeadCode.Should().NotBeNullOrWhiteSpace();
        mdMaintainability.Should().NotBeNullOrWhiteSpace();
        mdCohesion.Should().NotBeNullOrWhiteSpace();
        mdMartin.Should().NotBeNullOrWhiteSpace();
        mdShared.Should().NotBeNullOrWhiteSpace();
        mdCrud.Should().NotBeNullOrWhiteSpace();
        mdTaint.Should().NotBeNullOrWhiteSpace();
        mdScreenFlow.Should().NotBeNullOrWhiteSpace();
        mdDsm.Should().NotBeNullOrWhiteSpace();
        mdClones.Should().NotBeNullOrWhiteSpace();
        mdArch.Should().NotBeNullOrWhiteSpace();

        jsonDeadCode.Should().NotBeNullOrWhiteSpace();
        jsonMaintainability.Should().NotBeNullOrWhiteSpace();
        jsonDsm.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Full Relationship Kind Roundtrip Serialization

    /// <summary>
    /// Verifica che la serializzazione JSON conservi tutte le 7 tipologie di relazioni
    /// (Calls, Reads, Writes, Contains, Implements, UsesType, HandlesEvent).
    /// </summary>
    [Fact]
    public void CodeGraphJsonExporter_ShouldRoundtripAllSevenRelationshipKinds()
    {
        // Arrange
        var graph = new CodeGraph();

        var form = new Symbol { Id = Guid.NewGuid(), Name = "OrderForm", QualifiedName = "OrderForm", FilePath = "OrderForm.frm", Kind = SymbolKind.Form };
        var btnClick = new Symbol { Id = Guid.NewGuid(), Name = "cmdSave_Click", QualifiedName = "OrderForm.cmdSave_Click", FilePath = "OrderForm.frm", Kind = SymbolKind.Method };
        var field = new Symbol { Id = Guid.NewGuid(), Name = "_orderId", QualifiedName = "OrderForm._orderId", FilePath = "OrderForm.frm", Kind = SymbolKind.Field };
        var iface = new Symbol { Id = Guid.NewGuid(), Name = "IOrderProcessor", QualifiedName = "IOrderProcessor", FilePath = "IOrderProcessor.cls", Kind = SymbolKind.Interface };
        var cls = new Symbol { Id = Guid.NewGuid(), Name = "OrderProcessor", QualifiedName = "OrderProcessor", FilePath = "OrderProcessor.cls", Kind = SymbolKind.Class };
        var method = new Symbol { Id = Guid.NewGuid(), Name = "ProcessOrder", QualifiedName = "OrderProcessor.ProcessOrder", FilePath = "OrderProcessor.cls", Kind = SymbolKind.Method };
        var extType = new Symbol { Id = Guid.NewGuid(), Name = "PaymentDto", QualifiedName = "PaymentDto", FilePath = "PaymentDto.cs", Kind = SymbolKind.Class };

        graph.Symbols.AddRange(new[] { form, btnClick, field, iface, cls, method, extType });

        // Add all 7 relationship types
        graph.AddContains(form, btnClick);
        graph.AddContains(cls, method);
        graph.AddHandlesEvent(form, btnClick);
        graph.AddReads(btnClick, field);
        graph.AddWrites(btnClick, field);
        graph.AddCalls(btnClick, method);
        graph.AddImplements(cls, iface);
        graph.AddUsesType(method, extType);

        // Act
        var json = CodeGraphJsonExporter.ExportToJson(graph, indented: true);
        var restored = CodeGraphJsonExporter.ImportFromJson(json);

        // Assert
        restored.Should().NotBeNull();
        restored.Symbols.Count.Should().Be(graph.Symbols.Count);
        restored.Relationships.Count.Should().Be(graph.Relationships.Count);

        var restoredKinds = restored.Relationships.Select(r => r.Kind).Distinct().ToList();
        restoredKinds.Should().Contain(RelationshipKind.Contains);
        restoredKinds.Should().Contain(RelationshipKind.HandlesEvent);
        restoredKinds.Should().Contain(RelationshipKind.Reads);
        restoredKinds.Should().Contain(RelationshipKind.Writes);
        restoredKinds.Should().Contain(RelationshipKind.Calls);
        restoredKinds.Should().Contain(RelationshipKind.Implements);
        restoredKinds.Should().Contain(RelationshipKind.UsesType);
    }

    #endregion

    #region Markdown Table Special Character Escaping

    /// <summary>
    /// Verifica che i simboli contenenti pipe, apici e caratteri speciali vengano esportati
    /// in Markdown senza corrompere la struttura delle tabelle.
    /// </summary>
    [Fact]
    public void MarkdownExporter_ShouldHandleSpecialCharactersInSymbolNames()
    {
        // Arrange
        var graph = new CodeGraph();
        var specialSymbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Format | Data <T> & 'Escaped'",
            QualifiedName = "Helpers.Format | Data <T> & 'Escaped'",
            FilePath = "Helpers/Format | Data.cs",
            Kind = SymbolKind.Method,
            LineStart = 1,
            LineEnd = 20
        };
        graph.Symbols.Add(specialSymbol);

        // Act
        var markdown = CodeGraphMarkdownExporter.Export(graph);

        // Assert
        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# CodeBase Knowledge Graph");
    }

    #endregion
}
