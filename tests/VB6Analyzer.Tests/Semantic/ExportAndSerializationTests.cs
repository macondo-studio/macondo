using FluentAssertions;
using Macondo.Semantic.Export;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Semantic;

public class ExportAndSerializationTests
{
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
}
