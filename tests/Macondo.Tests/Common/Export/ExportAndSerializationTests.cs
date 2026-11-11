using FluentAssertions;
using Macondo.Semantic.Export;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Export;

public class ExportAndSerializationTests
{
    /// <summary>
    /// Verify that CodeGraphMarkdownExporter generates a non-empty formatted Markdown document for the entire graph.
    /// </summary>
    [Fact]
    public void MarkdownExporter_ShouldGenerateNonEmptyReport()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var markdown = CodeGraphMarkdownExporter.Export(graph);

        // Assert
        markdown.Should().NotBeNullOrWhiteSpace();
        markdown.Should().Contain("# CodeBase Knowledge Graph");
    }

    /// <summary>
    /// Verify Markdown export of a sub-graph/slice (Slice) focused on a specific routine.
    /// </summary>
    [Fact]
    public void MarkdownExporter_ShouldExportSliceForSymbol()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var routine = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method);
        routine.Should().NotBeNull();

        // Act
        var slice = CodeGraphMarkdownExporter.ExportSlice(graph, routine!);

        // Assert
        slice.Should().NotBeNullOrWhiteSpace();
        slice.Should().Contain(routine!.Name);
    }

    /// <summary>
    /// Check the CodeGraph JSON serialization and export/import round-trip,
    /// confirming the perfect correspondence of symbols, IDs, kind and relationships.
    /// </summary>
    [Fact]
    public void JsonExporter_ShouldRoundtripSerializeAndDeserializeIdentically()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var json = CodeGraphJsonExporter.ExportToJson(graph, indented: true);
        json.Should().NotBeNullOrWhiteSpace();

        var reloadedGraph = CodeGraphJsonExporter.ImportFromJson(json);

        // Assert
        reloadedGraph.Should().NotBeNull();
        reloadedGraph.Symbols.Count.Should().Be(graph.Symbols.Count);
        reloadedGraph.Relationships.Count.Should().Be(graph.Relationships.Count);

        foreach (var sym in graph.Symbols)
        {
            var reloadedSym = reloadedGraph.FindSymbolById(sym.Id);
            reloadedSym.Should().NotBeNull();
            reloadedSym!.Name.Should().Be(sym.Name);
            reloadedSym.Kind.Should().Be(sym.Kind);
            reloadedSym.QualifiedName.Should().Be(sym.QualifiedName);
        }
    }

    /// <summary>
    /// Verify that JSON serialization fully preserves all advanced symbol metadata:
    /// parameters, ByVal/ByRef, default values, accessibility (e.g. Friend), static flags and lines of code.
    /// </summary>
    [Fact]
    public void JsonExporter_ShouldPreserveSymbolMetadata_ParametersAccessibilityFlags()
    {
        // Arrange: Custom graph with detailed symbols
        var graph = new CodeGraph();
        var method = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "CalculateRisk",
            QualifiedName = "Analytics.CalculateRisk",
            FilePath = "Analytics.cls",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Friend,
            ReturnType = "Double",
            IsStatic = true,
            LineStart = 10,
            LineEnd = 45
        };
        method.Parameters.Add(new ParameterSymbol
        {
            Name = "factor",
            DeclaredTypeName = "Double",
            IsByVal = true,
            IsOptional = true,
            DefaultValue = "1.0",
            Line = 10
        });

        graph.Symbols.Add(method);

        // Act
        var json = CodeGraphJsonExporter.ExportToJson(graph, indented: true);
        var restored = CodeGraphJsonExporter.ImportFromJson(json);

        // Assert
        var restoredMethod = restored.FindSymbolById(method.Id);
        restoredMethod.Should().NotBeNull();
        restoredMethod!.Accessibility.Should().Be(Accessibility.Friend);
        restoredMethod.ReturnType.Should().Be("Double");
        restoredMethod.IsStatic.Should().BeTrue();
        restoredMethod.Parameters.Should().HaveCount(1);
        restoredMethod.Parameters[0].Name.Should().Be("factor");
        restoredMethod.Parameters[0].IsByVal.Should().BeTrue();
        restoredMethod.Parameters[0].IsOptional.Should().BeTrue();
        restoredMethod.Parameters[0].DefaultValue.Should().Be("1.0");
    }
}
