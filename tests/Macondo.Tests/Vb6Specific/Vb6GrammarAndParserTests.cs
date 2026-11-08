using Antlr4.Runtime;
using FluentAssertions;
using Macondo.Parser.Diagnostics;
using Macondo.Parser.Grammar;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6GrammarAndParserTests
{
    /// <summary>
    /// Verifica che il lexer e parser ANTLR4 eseguano il parsing di una Sub VB6 standard senza generare errori di sintassi.
    /// </summary>
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

    /// <summary>
    /// Verifica la corretta gestione del carattere di continuazione riga (' _') su istruzioni e stringhe multilinea.
    /// </summary>
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

    /// <summary>
    /// Verifica che il CollectingErrorListener intercetti e memorizzi tutti gli errori sintattici in presenza di codice malformato.
    /// </summary>
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

    /// <summary>
    /// Verifica il parsing di blocchi 'With ... End With' e definizioni di Property Get/Let/Set.
    /// </summary>
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

    /// <summary>
    /// Verifica il supporto per numeri di riga numerici legacy (es. '10 On Error GoTo Errore') ed etichette prima delle istruzioni.
    /// </summary>
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

    /// <summary>
    /// Verifica il parsing dell'istruzione Load/Unload con argomenti multipli o espressioni.
    /// </summary>
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

    /// <summary>
    /// Verifica il parsing delle dichiarazioni Win32 API ('Declare Function/Sub ... Lib ... Alias ...').
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportWin32DeclareStatements_WithLibAndAlias()
    {
        // Arrange
        var code = """
            Private Declare Function GetTickCount Lib "kernel32" () As Long
            Public Declare Sub CopyMemory Lib "kernel32" Alias "RtlMoveMemory" (ByRef hpvDest As Any, ByVal hpvSource As Long, ByVal cbCopy As Long)
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
        errors.Should().BeEmpty("Win32 API Declare Function/Sub statements should parse without errors");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica il supporto per identificatori racchiusi tra parentesi quadre (bracketed identifiers, es. '[Date]', '[Select]').
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportBracketedIdentifiers()
    {
        // Arrange
        var code = """
            Public Sub TestKeywordsAsIdentifiers()
                Dim [Date] As String
                Dim [Select] As Integer
                [Date] = "2026-09-15"
                [Select] = 42
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
        errors.Should().BeEmpty("Bracketed reserved words should parse cleanly as identifiers");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica il supporto per istruzioni multiple separate da due punti (':') sulla stessa riga di codice.
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportMultiStatementOnSingleLine_WithColon()
    {
        // Arrange
        var code = """
            Public Sub MultiStatements()
                Dim a As Long: Dim b As Long: Dim c As Long
                a = 1: b = 2: c = a + b
                a = a + 1: b = b + 1: c = c + 1
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
        errors.Should().BeEmpty("Multiple statements separated by colons on a single line should parse without errors");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica il parsing di dichiarazioni di eventi ('Public Event ...') e istruzioni 'RaiseEvent'.
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportEventDeclarationAndRaiseEvent()
    {
        // Arrange
        var code = """
            Public Event DataChanged(ByVal newId As Long, ByVal status As String)

            Public Sub TriggerEvent(ByVal id As Long)
                RaiseEvent DataChanged(id, "Success")
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
        errors.Should().BeEmpty("Event declarations and RaiseEvent statements should parse without errors");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica il parsing di direttive di compilazione condizionale ('#If ... #Else ... #End If').
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportConditionalCompilationDirectives()
    {
        // Arrange
        var code = """
            #If Win32 Then
            Public Sub ProcessData()
                Dim x As Long
                x = 100
            End Sub
            #Else
            Public Sub ProcessData()
                Dim x As Integer
                x = 50
            End Sub
            #End If
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
        errors.Should().BeEmpty("Conditional compilation directives (#If...#Else...#End If) should be parsed cleanly");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica la gestione di doppi apici escapati all'interno di stringhe letterali VB6 (es. '""John Doe""').
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportEscapedQuotesInStringLiterals()
    {
        // Arrange
        var code = """"
            Public Sub TestQuotes()
                Dim sql As String
                sql = "SELECT * FROM Users WHERE Name = ""John Doe"""
            End Sub
            """";

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
        errors.Should().BeEmpty("String literals with escaped double quotes should parse cleanly");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica il parsing della clausola 'Implements InterfaceName' a livello di modulo di classe.
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportImplementsStatement()
    {
        // Arrange
        var code = """
            Implements IOrderProcessor

            Private Sub IOrderProcessor_Process()
                ' Interface implementation
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
        errors.Should().BeEmpty("Implements statements at module level should parse without errors");
        tree.Should().NotBeNull();
    }
}
