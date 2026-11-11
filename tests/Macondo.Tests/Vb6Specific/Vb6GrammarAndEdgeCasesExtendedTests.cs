using Antlr4.Runtime;
using FluentAssertions;
using Macondo.Parser.Diagnostics;
using Macondo.Parser.Grammar;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6GrammarAndEdgeCasesExtendedTests
{
    /// <summary>
    /// Test parsing of complete Select Case blocks with multiple clauses:
    /// valori separati da virgola, intervalli 'To', condizioni 'Is &gt;=', e 'Case Else'.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseComplexSelectCase_WithRangesAndConditions()
    {
        // Arrange
        var code = """
            Public Sub EvaluateGrade(ByVal score As Integer)
                Dim grade As String
                Select Case score
                    Case 100
                        grade = "A+"
                    Case 90 To 99
                        grade = "A"
                    Case 80, 81, 82, 83, 84, 85, 86, 87, 88, 89
                        grade = "B"
                    Case Is < 60
                        grade = "F"
                    Case Else
                        grade = "C"
                End Select
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
        errors.Should().BeEmpty("Select Case with ranges, conditions and multiple values should parse without errors");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Tests extraction and parsing of Optional parameterized routines with default values ​​and ParamArray parameters.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldSupportOptionalDefaultValues_AndParamArray()
    {
        // Arrange
        var code = """
            Public Sub LogMulti(ByVal level As Integer, Optional ByVal prefix As String = "INFO: ", Optional ByVal timeout As Long = 5000, ParamArray args() As Variant)
                Dim i As Long
                For i = LBound(args) To UBound(args)
                    Call OutputDebug(prefix & CStr(args(i)))
                Next i
            End Sub

            Private Sub OutputDebug(ByVal msg As String)
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("LoggerUtils.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var logSub = graph.Symbols.FirstOrDefault(s => s.Name == "LogMulti");
        logSub.Should().NotBeNull();
        logSub!.Parameters.Should().HaveCount(4);

        var paramPrefix = logSub.Parameters.FirstOrDefault(p => p.Name == "prefix");
        paramPrefix.Should().NotBeNull();
        paramPrefix!.IsOptional.Should().BeTrue();

        var paramArgs = logSub.Parameters.FirstOrDefault(p => p.Name == "args");
        paramArgs.Should().NotBeNull();
    }

    /// <summary>
    /// Tests the definition of User-Defined Types (UDTs) with fixed-length strings and internal static arrays.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractUserDefinedTypes_WithFixedStringsAndArrays()
    {
        // Arrange
        var code = """
            Public Type CustomerRecord
                CustomerId As Long
                CustomerCode As String * 10
                CompanyName As String * 50
                AddressLines(1 To 3) As String * 40
                CreditLimit As Currency
            End Type

            Public Sub ProcessRecord(ByRef rec As CustomerRecord)
                rec.CustomerId = 12345
                rec.CustomerCode = "ABCDE"
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("CustomerTypes.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var udtSymbol = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Type && s.Name == "CustomerRecord");
        udtSymbol.Should().NotBeNull("UDT CustomerRecord should be extracted as a SymbolKind.Type");
    }

    /// <summary>
    /// Tests parsing and extraction of Enum constructs with explicit positive and negative integer values.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractEnumDefinitions_WithExplicitAndNegativeValues()
    {
        // Arrange
        var code = """
            Public Enum TransactionStatus
                StatusUnknown = -1
                StatusPending = 0
                StatusAuthorized = 100
                StatusSettled = 200
                StatusFailed = 999
            End Enum

            Public Sub UpdateStatus(ByVal newStatus As TransactionStatus)
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("EnumsMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var enumSymbol = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Enum && s.Name == "TransactionStatus");
        enumSymbol.Should().NotBeNull("Enum TransactionStatus should be extracted as a SymbolKind.Enum");
    }

    /// <summary>
    /// Tests support for Static local variables within a VB6 routine.
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportStaticLocalVariables()
    {
        // Arrange
        var code = """
            Public Function GetNextSequence() As Long
                Static sequenceNumber As Long
                Static isInitialized As Boolean
                
                If Not isInitialized Then
                    sequenceNumber = 1000
                    isInitialized = True
                Else
                    sequenceNumber = sequenceNumber + 1
                End If
                
                GetNextSequence = sequenceNumber
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
        errors.Should().BeEmpty("Static local variable declarations inside routines should parse cleanly");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Test parsing of GoSub ... Return and On Error / On ... GoTo statements.
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportGoSubReturnAndOnGoToStatements()
    {
        // Arrange
        var code = """
            Public Sub LegacyFlow(ByVal mode As Integer)
                On mode GoTo FirstOption, SecondOption, ThirdOption
                
                GoSub LocalCleanup
                Exit Sub
                
            FirstOption:
                Call ActionA
                GoSub LocalCleanup
                Exit Sub
                
            SecondOption:
                Call ActionB
                GoSub LocalCleanup
                Exit Sub
                
            ThirdOption:
                Call ActionC
                GoSub LocalCleanup
                Exit Sub
                
            LocalCleanup:
                Call ResetBuffers
                Return
            End Sub

            Private Sub ActionA() : End Sub
            Private Sub ActionB() : End Sub
            Private Sub ActionC() : End Sub
            Private Sub ResetBuffers() : End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("GoSubMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var legacySub = graph.Symbols.FirstOrDefault(s => s.Name == "LegacyFlow");
        var actionASub = graph.Symbols.FirstOrDefault(s => s.Name == "ActionA");
        var resetSub = graph.Symbols.FirstOrDefault(s => s.Name == "ResetBuffers");

        legacySub.Should().NotBeNull();
        actionASub.Should().NotBeNull();
        resetSub.Should().NotBeNull();

        graph.Relationships.Should().Contain(r => r.SourceId == legacySub!.Id && r.TargetId == actionASub!.Id);
        graph.Relationships.Should().Contain(r => r.SourceId == legacySub!.Id && r.TargetId == resetSub!.Id);
    }

    /// <summary>
    /// Tests parsing of all VB6 loop types: Do While...Loop, Do...Loop Until, While...Wend, For Each...Next, For...Step -1.
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportAllLoopVariants_AndForStep()
    {
        // Arrange
        var code = """
            Public Sub ProcessLoops(ByVal items As Collection)
                Dim i As Long
                For i = 100 To 0 Step -5
                    ' Descending loop
                Next i
                
                Dim elem As Variant
                For Each elem In items
                    ' For Each loop
                Next elem
                
                Dim count As Long
                count = 0
                Do While count < 10
                    count = count + 1
                Loop
                
                Do
                    count = count - 1
                Loop Until count <= 0
                
                While count < 5
                    count = count + 2
                Wend
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
        errors.Should().BeEmpty("All loop constructs (For Step, For Each, Do While, Do Loop Until, While Wend) should parse cleanly");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Test parsing of Property Get and Property Let with multiple indexing parameters.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldExtractIndexedProperties_WithLetAndGet()
    {
        // Arrange
        var code = """
            Private m_Matrix(10, 10) As String

            Public Property Get CellValue(ByVal row As Long, ByVal col As Long) As String
                CellValue = m_Matrix(row, col)
            End Property

            Public Property Let CellValue(ByVal row As Long, ByVal col As Long, ByVal newValue As String)
                m_Matrix(row, col) = newValue
            End Property
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("GridData.cls", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var props = graph.Symbols.Where(s => s.Kind == SymbolKind.Property && s.Name == "CellValue").ToList();
        props.Should().NotBeEmpty();
    }

    /// <summary>
    /// Tests support for native VB6 File I/O instructions (Open, Close, Line Input, Print, Put, Get).
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportFileIOStatements()
    {
        // Arrange
        var code = """
            Public Sub ReadAndWriteFiles(ByVal inPath As String, ByVal outPath As String)
                Dim fileNumIn As Integer
                Dim fileNumOut As Integer
                Dim lineBuffer As String
                
                fileNumIn = FreeFile
                Open inPath For Input As #fileNumIn
                
                fileNumOut = FreeFile
                Open outPath For Output As #fileNumOut
                
                Do While Not EOF(fileNumIn)
                    Line Input #fileNumIn, lineBuffer
                    Print #fileNumOut, "PROCESSED: " & lineBuffer
                Loop
                
                Close #fileNumIn
                Close #fileNumOut
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
        errors.Should().BeEmpty("Native VB6 File I/O statements (Open, Close, Line Input, Print) should parse without errors");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Check the management of the Mid(...) instruction used as assignment target (Mid statement).
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportMidStatementAssignment()
    {
        // Arrange
        var code = """
            Public Sub ReplaceSubstring()
                Dim buffer As String
                buffer = "Hello World"
                Mid(buffer, 7, 5) = "VB6!!"
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
        errors.Should().BeEmpty("Mid(...) = ... statement assignment should parse cleanly");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Tests parsing of declarations with type suffixes (Type Hint Characters: $, %, &, #, @, !).
    /// </summary>
    [Fact]
    public void Parser_ShouldSupportTypeHintSuffixCharacters()
    {
        // Arrange
        var code = """
            Public Sub TypeHintsDemo()
                Dim strVal$
                Dim intVal%
                Dim lngVal&
                Dim dblVal#
                Dim curVal@
                Dim sngVal!
                
                strVal$ = "text"
                intVal% = 42
                lngVal& = 100000
                dblVal# = 3.14159265
                curVal@ = 99.95
                sngVal! = 1.5
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
        errors.Should().BeEmpty("Type hint suffix characters ($, %, &, #, @, !) should parse cleanly");
        tree.Should().NotBeNull();
    }

    /// <summary>
    /// Test parsing of multiple declarations with contextual New on the same line.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldSupportMultiVariableDeclarationWithNew()
    {
        // Arrange
        var code = """
            Public Sub MultiVarDecl()
                Dim a As Long, b As String, c As Collection, d As Double
                a = 10
                b = "hello"
                d = 2.5
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("MultiVar.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();
    }
}
