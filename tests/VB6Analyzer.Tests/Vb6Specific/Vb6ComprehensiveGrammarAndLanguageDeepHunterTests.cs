using Antlr4.Runtime;
using FluentAssertions;
using Macondo.Parser.Diagnostics;
using Macondo.Parser.Grammar;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6ComprehensiveGrammarAndLanguageDeepHunterTests
{
    #region DefType Statements (DefInt, DefLng, DefStr, DefDbl, DefBool, DefCur, DefDate, DefObj)

    /// <summary>
    /// Verifica il corretto parsing delle direttive VB6 di definizione del tipo predefinito per intervallo di lettere (DefInt, DefLng, DefStr, ecc.).
    /// </summary>
    [Fact]
    public void Parser_ShouldParseDefTypeStatements_WithoutErrors()
    {
        // Arrange
        var code = """
            Attribute VB_Name = "DefTypeMod"
            DefInt I-N
            DefLng L
            DefStr S
            DefDbl D
            DefBool B
            DefCur C
            DefDate T
            DefObj O

            Public Sub CalculateWithDefaults()
                iCount = 10
                sMessage = "Hello World"
                dTotal = 123.45
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
        errors.Should().BeEmpty("DefType statements (DefInt, DefLng, DefStr, etc.) should parse cleanly");
        tree.Should().NotBeNull();
    }

    #endregion

    #region User Defined Types (UDT) with Fixed-Length Strings, Arrays, and Nesting

    /// <summary>
    /// Verifica il parsing e l'estrazione di User-Defined Types (Type ... End Type) complessi
    /// contenenti stringhe a lunghezza fissa (String * N), array statici e tipi UDT annidati.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractComplexUserDefinedTypes_WithFixedStringsAndArrays()
    {
        // Arrange
        var graph = new CodeGraph();
        var code = """
            Attribute VB_Name = "TypeDefinitionsMod"

            Public Type GeoPoint
                Latitude As Double
                Longitude As Double
                Altitude As Single
            End Type

            Public Type CustomerRecord
                Id As Long
                Code As String * 10
                FullName As String * 50
                Location As GeoPoint
                HistoryScores(1 To 12) As Integer
            End Type

            Public Sub ProcessCustomer(ByRef cust As CustomerRecord)
                cust.Id = 1001
                cust.Code = "CUST000001"
            End Sub
            """;

        // Act
        var result = ModuleExtractor.ExtractModule(graph, "TypeDefinitionsMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var geoPointType = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Type && s.Name == "GeoPoint");
        var customerRecordType = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Type && s.Name == "CustomerRecord");

        geoPointType.Should().NotBeNull();
        customerRecordType.Should().NotBeNull();

        var procSub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "ProcessCustomer");
        procSub.Should().NotBeNull();
        procSub!.Parameters.Should().HaveCount(1);
    }

    #endregion

    #region Multi-Variable Declarations on Single Line with Mixed Types and New

    /// <summary>
    /// Verifica il parsing e l'estrazione di dichiarazioni multiple su una singola riga
    /// con tipi eterogenei, array multidimensionali e istanziazioni 'New'.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractMultiVariableDeclarations_WithMixedModifiersAndTypes()
    {
        // Arrange
        var graph = new CodeGraph();
        var code = """
            Attribute VB_Name = "MultiDeclMod"

            Public g_Total As Long, g_Name As String * 30, g_Matrix(10, 20) As Double, g_Items As New Collection

            Private Sub TestMultiDim()
                Dim a As Integer, b As String, c() As Byte, d As New Collection
                a = 1
                b = "Test"
            End Sub
            """;

        // Act
        var result = ModuleExtractor.ExtractModule(graph, "MultiDeclMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var modSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Module && s.Name == "MultiDeclMod");
        modSym.Should().NotBeNull();

        var childIds = graph.Relationships.Where(r => r.Kind == RelationshipKind.Contains && r.SourceId == modSym!.Id).Select(r => r.TargetId).ToHashSet();
        var fields = graph.Symbols.Where(s => childIds.Contains(s.Id) && s.Kind == SymbolKind.Field).ToList();
        fields.Should().Contain(f => f.Name == "g_Total");
        fields.Should().Contain(f => f.Name == "g_Name");
    }

    #endregion

    #region Multi-Argument Parameterized Properties (Property Get / Let Matrix)

    /// <summary>
    /// Verifica l'estrazione e il collegamento di proprietà indicizzate a più dimensioni (es. matrice Property Get/Let).
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractParameterizedProperties_WithMultipleArguments()
    {
        // Arrange
        var graph = new CodeGraph();
        var code = """
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "GridMatrix"
            Attribute VB_GlobalNameSpace = False
            Attribute VB_Creatable = True
            Attribute VB_PredeclaredId = False
            Attribute VB_Exposed = False

            Private m_data(100, 100) As Double

            Public Property Get CellValue(ByVal row As Long, ByVal col As Long) As Double
                CellValue = m_data(row, col)
            End Property

            Public Property Let CellValue(ByVal row As Long, ByVal col As Long, ByVal newValue As Double)
                m_data(row, col) = newValue
            End Property
            """;

        // Act
        var result = ModuleExtractor.ExtractModule(graph, "GridMatrix.cls", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var classSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "GridMatrix");
        classSym.Should().NotBeNull();

        var propGet = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "CellValue");
        propGet.Should().NotBeNull();
        graph.Relationships.Should().Contain(r => r.Kind == RelationshipKind.Contains && r.SourceId == classSym!.Id && r.TargetId == propGet!.Id);
        propGet!.Parameters.Should().HaveCount(2);
        propGet.Parameters[0].Name.Should().Be("row");
        propGet.Parameters[1].Name.Should().Be("col");
    }

    #endregion

    #region Win32 API Declare with Alias, Lib, Any Type, and Ordinals

    /// <summary>
    /// Verifica il parsing di dichiarazioni Win32 API complesse con alias, tipo Any e chiamate a DLL di sistema.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseWin32ApiDeclareStatements_WithAnyAndAlias()
    {
        // Arrange
        var code = """
            Attribute VB_Name = "Win32ApiMod"

            Private Declare Function SendMessage Lib "user32" Alias "SendMessageA" ( _
                ByVal hWnd As Long, _
                ByVal wMsg As Long, _
                ByVal wParam As Long, _
                lParam As Any) As Long

            Public Declare Sub CopyMemory Lib "kernel32" Alias "RtlMoveMemory" ( _
                Destination As Any, _
                Source As Any, _
                ByVal Length As Long)

            Public Declare Function GetTickCount Lib "kernel32" () As Long

            Public Sub SendCustomMessage(ByVal h As Long, ByVal msg As Long)
                Dim res As Long
                res = SendMessage(h, msg, 0&, 0&)
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
        errors.Should().BeEmpty("Declare statements with Lib, Alias and Any parameter types should parse without errors");
        tree.Should().NotBeNull();
    }

    #endregion

    #region Events with ByRef Cancel Pattern and RaiseEvent Invocations

    /// <summary>
    /// Verifica la dichiarazione di eventi con parametri ByRef Cancel e la generazione di chiamate RaiseEvent.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractEvents_WithByRefCancelAndRaiseEvent()
    {
        // Arrange
        var graph = new CodeGraph();
        var code = """
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "DocumentManager"

            Public Event BeforeSave(ByRef Cancel As Boolean, ByVal FileName As String)
            Public Event AfterSave(ByVal FileName As String)

            Public Sub SaveDocument(ByVal path As String)
                Dim shouldCancel As Boolean
                shouldCancel = False
                RaiseEvent BeforeSave(shouldCancel, path)
                If shouldCancel Then Exit Sub
                
                ' Write file logic
                RaiseEvent AfterSave(path)
            End Sub
            """;

        // Act
        var (g, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("DocumentManager.cls", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var clsSym = g.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "DocumentManager");
        clsSym.Should().NotBeNull();

        var evtBefore = g.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Event && s.Name == "BeforeSave");
        var evtAfter = g.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Event && s.Name == "AfterSave");

        evtBefore.Should().NotBeNull();
        evtAfter.Should().NotBeNull();
    }

    #endregion

    #region Numeric Line Numbers and Classic Line Labels

    /// <summary>
    /// Verifica il parsing di codice legacy con numeri di riga (10, 20, 30) e label numeriche per On Error GoTo.
    /// </summary>
    [Fact]
    public void Parser_ShouldParseNumericLineNumbersAndErrorHandling()
    {
        // Arrange
        var code = """
            Attribute VB_Name = "LegacyNumbersMod"

            Public Sub LegacyRoutine()
                10 On Error GoTo 100
                20 Dim x As Long
                30 x = 42
                40 Exit Sub

            100 ' Error handler
                110 MsgBox "An error occurred at line " & Erl
                120 Resume Next
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
        errors.Should().BeEmpty("Numeric line numbers and classic On Error GoTo handlers should parse without errors");
        tree.Should().NotBeNull();
    }

    #endregion

    #region Implements Interface Implementation with Explicit Method Prefixes

    /// <summary>
    /// Verifica l'implementazione di interfacce VB6 (Implements IInterfaceName) e la corretta risoluzione delle relazioni di implementazione.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldResolveImplementsRelationship_AndInterfaceMethods()
    {
        // Arrange
        var ifaceCode = """
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "IOrderProcessor"

            Public Sub ProcessOrder(ByVal orderId As Long)
            End Sub

            Public Function ValidateOrder(ByVal orderId As Long) As Boolean
            End Function
            """;

        var implCode = """
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "StandardOrderProcessor"
            Implements IOrderProcessor

            Private Sub IOrderProcessor_ProcessOrder(ByVal orderId As Long)
                ' Implementation
            End Sub

            Private Function IOrderProcessor_ValidateOrder(ByVal orderId As Long) As Boolean
                IOrderProcessor_ValidateOrder = True
            End Function
            """;

        var graph = new CodeGraph();
        var resIface = ModuleExtractor.ExtractModule(graph, "IOrderProcessor.cls", ifaceCode);
        var resImpl = ModuleExtractor.ExtractModule(graph, "StandardOrderProcessor.cls", implCode);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        // Act
        builder.BuildGraph(new[] { resIface, resImpl });

        // Assert
        resIface.Success.Should().BeTrue();
        resImpl.Success.Should().BeTrue();

        var ifaceSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "IOrderProcessor" || s.Kind == SymbolKind.Class && s.Name == "IOrderProcessor");
        var implSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "StandardOrderProcessor");

        ifaceSym.Should().NotBeNull();
        implSym.Should().NotBeNull();

        var implementsRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Implements && r.SourceId == implSym!.Id && r.TargetId == ifaceSym!.Id);
        implementsRel.Should().NotBeNull();
    }

    #endregion
}
