using Antlr4.Runtime;
using FluentAssertions;
using Macondo.Parser.Diagnostics;
using Macondo.Parser.Grammar;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6AdvancedGrammarAndFlowEdgeCasesTests
{
    #region Type Suffix Characters and Multi-Statement Lines

    /// <summary>
    /// Verify that the parser and extractor handle VB6 type suffixes
    /// (% = Integer, & = Long, ! = Single, # = Double, $ = String, @ = Currency)
    /// on variable declarations and routines.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleTypeSuffixCharacters_OnVariablesAndRoutines()
    {
        // Arrange
        var code = """
            Public Function CalculateMetrics%(ByVal factor#)
                Dim count%
                Dim total&
                Dim rate!
                Dim label$
                Dim price@

                count% = 10
                total& = 100000
                rate! = 3.14
                label$ = "Metrics"
                price@ = 49.99

                CalculateMetrics% = count%
            End Function
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("TypeSuffixMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var func = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name.StartsWith("CalculateMetrics"));
        func.Should().NotBeNull();
    }

    /// <summary>
    /// Test multiple statements separated by colons with numeric labels and inline comments.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseMultiStatementLinesWithColonsAndLabels()
    {
        // Arrange
        var code = """
            Public Sub MultiLineProc()
            100: Dim x As Integer: x = 1: Dim y As Integer: y = 2
            200: If x > 0 Then y = 10: x = 20 ' inline comment
                 x = x + 1: y = y + 1
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
        errors.Should().BeEmpty("Multi-statement lines with colons, labels and inline comments should parse cleanly");
        tree.Should().NotBeNull();
    }

    #endregion

    #region Property Set and Object Properties

    /// <summary>
    /// Verify the definition and extraction of property triplets: Property Get, Property Let and Property Set.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractPropertyGetLetAndSet()
    {
        // Arrange
        var code = """
            Private m_Connection As Object

            Public Property Get Connection() As Object
                Set Connection = m_Connection
            End Property

            Public Property Set Connection(ByRef newConn As Object)
                Set m_Connection = newConn
            End Property
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("ConnectionHolder.cls", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var props = graph.Symbols.Where(s => s.Kind == SymbolKind.Property && s.Name == "Connection").ToList();
        props.Should().NotBeEmpty();
    }

    #endregion

    #region Complex Select Case and Flow Controls

    /// <summary>
    /// Test parsing of advanced Select Cases with multiple lists, ranges (To), and relational conditions (Is >, Is <=).
    /// </summary>
    [Fact]
    public void Parser_ShouldParseComplexSelectCaseWithRangesAndRelationalConditions()
    {
        // Arrange
        var code = """
            Public Function CategorizeAge(ByVal age As Integer) As String
                Select Case age
                    Case Is < 0
                        CategorizeAge = "Invalid"
                    Case 0, 1, 2
                        CategorizeAge = "Infant"
                    Case 3 To 12
                        CategorizeAge = "Child"
                    Case 13 To 19
                        CategorizeAge = "Teenager"
                    Case 20 To 64
                        CategorizeAge = "Adult"
                    Case Is >= 65
                        CategorizeAge = "Senior"
                    Case Else
                        CategorizeAge = "Unknown"
                End Select
            End Function
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
        errors.Should().BeEmpty("Complex Select Case statements should parse without syntax errors");
        tree.Should().NotBeNull();
    }

    #endregion

    #region Module Level Directives (Option Explicit, Option Compare, Option Base)

    /// <summary>
    /// Tests parsing of VB6 module global directives: Option Explicit, Option Compare Binary/Text, Option Base 0/1.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseModuleDirectives_OptionExplicitOptionCompareOptionBase()
    {
        // Arrange
        var code = """
            Option Explicit
            Option Compare Text
            Option Base 1

            Public Sub InitArray()
                Dim items(10) As String
                items(1) = "First"
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
        errors.Should().BeEmpty("Option Explicit, Option Compare, Option Base directives should parse cleanly");
        tree.Should().NotBeNull();
    }

    #endregion

    #region Nested With Blocks and Leading Dot Method Invocations

    /// <summary>
    /// Tests parsing of nested With blocks with leading dot method invocations and property assignments (.Save, .Refresh).
    /// </summary>
    [Fact]
    public void Parser_ShouldParseNestedWithBlocksAndLeadingDotInvocations()
    {
        // Arrange
        var code = """
            Public Sub ConfigureUI()
                With MainForm
                    .Caption = "Macondo VB6"
                    .Visible = True
                    With .StatusBar
                        .Panels(1).Text = "Ready"
                        .Refresh
                    End With
                    .Repaint
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
        errors.Should().BeEmpty("Nested With blocks with leading dot member invocations should parse without errors");
        tree.Should().NotBeNull();
    }

    #endregion
}
