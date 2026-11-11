using FluentAssertions;
using Macondo.Semantic.Export;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Export;

public class ExportFormattingAndEscapeEdgeCasesTests
{
    /// <summary>
    /// Verify that CodeGraphJsonExporter handles symbols containing special characters,
    /// quotes, slashes, backslashes and XML tags in the name and paths without corrupting the generated JSON.
    /// </summary>
    [Fact]
    public void JsonExporter_ShouldHandleSpecialCharactersAndEscaping()
    {
        var graph = new CodeGraph();
        var sym = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Method_With_\"Quotes\"_&_<Tags>_\\Backslash",
            QualifiedName = "Module.Special\"Method\"",
            FilePath = @"C:\Path with spaces & # symbols\File.cls",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public,
            ReturnType = "List<Dictionary<string, int>>",
            LineStart = 1,
            LineEnd = 50
        };

        sym.Parameters.Add(new ParameterSymbol
        {
            Name = "paramWith<T>",
            DeclaredTypeName = "T?",
            IsOptional = true,
            DefaultValue = "\"default\\escaped\""
        });

        graph.Symbols.Add(sym);

        // Act
        var json = CodeGraphJsonExporter.ExportToJson(graph, indented: true);
        var reloaded = CodeGraphJsonExporter.ImportFromJson(json);

        // Assert
        reloaded.Should().NotBeNull();
        reloaded.Symbols.Should().HaveCount(1);
        var reloadedSym = reloaded.Symbols[0];
        reloadedSym.Name.Should().Be(sym.Name);
        reloadedSym.QualifiedName.Should().Be(sym.QualifiedName);
        reloadedSym.ReturnType.Should().Be("List<Dictionary<string, int>>");
        reloadedSym.Parameters.Should().HaveCount(1);
        reloadedSym.Parameters[0].DefaultValue.Should().Be("\"default\\escaped\"");
    }

    /// <summary>
    /// Verify that CodeGraphMarkdownExporter generates valid markdown even for completely empty graphs.
    /// </summary>
    [Fact]
    public void MarkdownExporter_ShouldHandleEmptyGraphSafely()
    {
        var graph = new CodeGraph();

        var md = CodeGraphMarkdownExporter.Export(graph);

        md.Should().NotBeNullOrWhiteSpace();
        md.Should().Contain("# CodeBase Knowledge Graph");
    }

    /// <summary>
    /// Verify that Markdown exports a graph with relationship-free symbols
    /// lists symbols correctly without causing NullReferenceException.
    /// </summary>
    [Fact]
    public void MarkdownExporter_ShouldHandleIsolatedSymbols()
    {
        var graph = new CodeGraph();
        var orphan = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "OrphanRoutine",
            QualifiedName = "Utils.OrphanRoutine",
            FilePath = "Utils.bas",
            Kind = SymbolKind.Method
        };
        graph.Symbols.Add(orphan);

        var md = CodeGraphMarkdownExporter.Export(graph);

        md.Should().NotBeNullOrWhiteSpace();
        md.Should().Contain("OrphanRoutine");
    }

    /// <summary>
    /// Test exporting a Markdown slice for an orphaned symbol with no callers and callees.
    /// </summary>
    [Fact]
    public void MarkdownExporter_ExportSlice_ShouldHandleOrphanSymbol()
    {
        var graph = new CodeGraph();
        var orphan = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "StandaloneFunc",
            QualifiedName = "Helpers.StandaloneFunc",
            FilePath = "Helpers.bas",
            Kind = SymbolKind.Method
        };
        graph.Symbols.Add(orphan);

        var slice = CodeGraphMarkdownExporter.ExportSlice(graph, orphan);

        slice.Should().NotBeNullOrWhiteSpace();
        slice.Should().Contain("StandaloneFunc");
    }
}
