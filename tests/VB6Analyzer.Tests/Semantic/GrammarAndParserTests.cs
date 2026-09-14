using Antlr4.Runtime;
using FluentAssertions;
using Macondo.Parser.Diagnostics;
using Macondo.Parser.Grammar;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Semantic;

public class GrammarAndParserTests
{
    [Fact]
    public void Parser_ShouldParseSimpleSub_WithoutErrors()
    {
        // Arrange
        var code = """
            Public Sub CalculateTotal()
                Dim x As Integer
                x = 10 + 20
            End Sub
            """;

        var errors = new List<string>();
        var lexer = new VisualBasic6Lexer(new AntlrInputStream(code));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new CollectingErrorListener(errors));

        var parser = new VisualBasic6Parser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        // Act
        var tree = parser.startRule();

        // Assert
        errors.Should().BeEmpty();
        tree.Should().NotBeNull();
    }

    [Fact]
    public void Parser_ShouldHandleLineContinuation_Successfully()
    {
        // Arrange
        var code = """
            Public Sub LongStatement()
                Dim result As String
                result = "Hello " & _
                         "World " & _
                         "from VB6!"
            End Sub
            """;

        var errors = new List<string>();
        var lexer = new VisualBasic6Lexer(new AntlrInputStream(code));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new CollectingErrorListener(errors));

        var parser = new VisualBasic6Parser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        // Act
        var tree = parser.startRule();

        // Assert
        errors.Should().BeEmpty();
        tree.Should().NotBeNull();
    }

    [Fact]
    public void Parser_ShouldCollectSyntaxErrors_WhenCodeIsMalformed()
    {
        // Arrange
        var code = """
            Public Sub BrokenRoutine(
                Dim x As
            End
            """;

        var errors = new List<string>();
        var lexer = new VisualBasic6Lexer(new AntlrInputStream(code));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new CollectingErrorListener(errors));

        var parser = new VisualBasic6Parser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        // Act
        parser.startRule();

        // Assert
        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Parser_ShouldParseWithBlocksAndProperties()
    {
        // Arrange
        var code = """
            Public Property Get TotalAmount() As Double
                TotalAmount = 100.50
            End Property

            Public Sub ApplySettings()
                With UserForm
                    .Caption = "Test"
                    .Visible = True
                End With
            End Sub
            """;

        var errors = new List<string>();
        var lexer = new VisualBasic6Lexer(new AntlrInputStream(code));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new CollectingErrorListener(errors));

        var parser = new VisualBasic6Parser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        // Act
        var tree = parser.startRule();

        // Assert
        errors.Should().BeEmpty();
        tree.Should().NotBeNull();
    }

    [Fact]
    public void Parser_ShouldSupportNumericLineNumbers_AndLabelsBeforeStatements()
    {
        // Arrange
        var code = """
            Public Sub TestLineNumbers()
            10    On Error GoTo Errore
            20    Dim x As Long
            30    x = 100
                  MyLabel: x = 200
            40:   x = 300
            50
            Errore:
                  Exit Sub
            End Sub
            """;

        var errors = new List<string>();
        var lexer = new VisualBasic6Lexer(new AntlrInputStream(code));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new CollectingErrorListener(errors));

        var parser = new VisualBasic6Parser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        // Act
        var tree = parser.startRule();

        // Assert
        errors.Should().BeEmpty();
        tree.Should().NotBeNull();
    }

    [Fact]
    public void Parser_ShouldSupportLoadWithMultipleArguments()
    {
        // Arrange
        var code = """
            Public Sub TestLoad()
                load CLng(idP), lingua
                unload Form1
            End Sub
            """;

        var errors = new List<string>();
        var lexer = new VisualBasic6Lexer(new AntlrInputStream(code));
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(new CollectingErrorListener(errors));

        var parser = new VisualBasic6Parser(new CommonTokenStream(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(new CollectingErrorListener(errors));

        // Act
        var tree = parser.startRule();

        // Assert
        errors.Should().BeEmpty();
        tree.Should().NotBeNull();
    }

    [Fact]
    public void ModuleExtractor_ShouldExtractSampleClassCls_WithAllMethods()
    {
        var path = @".\SampleLegacyApp\SampleLegacyApp\SampleClass.cls";
        if (!File.Exists(path)) return;

        var graph = new CodeGraph();
        var source = File.ReadAllText(path);
        var result = ModuleExtractor.ExtractModule(graph, path, source);

        if (result.ParseErrors.Count > 0)
        {
            throw new Exception($"SampleClass.cls still has {result.ParseErrors.Count} errors:\n" + string.Join("\n", result.ParseErrors));
        }

        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var comandaSymbol = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "SampleClass");
        comandaSymbol.Should().NotBeNull();

        // Verifichiamo che i metodi interni di SampleClass (es. local_AddRow, GetTotale, ecc.) siano estratti nel grafo
        var childIds = graph.Relationships
            .Where(r => r.SourceId == comandaSymbol!.Id && r.Kind == RelationshipKind.Contains)
            .Select(r => r.TargetId)
            .ToHashSet();
        var children = graph.Symbols.Where(s => childIds.Contains(s.Id)).ToList();
        children.Should().NotBeEmpty();
        children.Count.Should().BeGreaterThan(20);

        var localAddRiga = children.FirstOrDefault(s => s.Name.Equals("local_AddRow", StringComparison.OrdinalIgnoreCase));
        localAddRiga.Should().NotBeNull();
    }

    [Fact]
    public void ModuleExtractor_ShouldParseDueSampleAppProblematicFiles_Successfully()
    {
        var dir = @".\SampleLegacyApp\SampleLegacyApp";
        if (!Directory.Exists(dir)) return;

        var targetFileNames = new[] { "SampleClass.cls", "SampleAccount.cls", "SampleMonitor.cls", "SampleDish.cls", "SampleSuspended.cls" };
        var graph = new CodeGraph();
        var failedFiles = new List<string>();

        foreach (var fileName in targetFileNames)
        {
            var filePath = Path.Combine(dir, fileName);
            if (!File.Exists(filePath)) continue;

            var source = File.ReadAllText(filePath);
            var result = ModuleExtractor.ExtractModule(graph, filePath, source);

            if (!result.Success)
            {
                failedFiles.Add($"{fileName}: {result.ParseErrors.Count} errors (first: {result.ParseErrors[0]})");
            }
        }

        failedFiles.Should().BeEmpty();
    }
}
