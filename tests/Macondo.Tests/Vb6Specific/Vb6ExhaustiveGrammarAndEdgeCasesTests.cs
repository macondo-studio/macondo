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

public class Vb6ExhaustiveGrammarAndEdgeCasesTests
{
    #region DefType Directives and Implicit Typing

    /// <summary>
    /// Verifica il parsing e l'estrazione di moduli VB6 che utilizzano direttive DefType
    /// (DefInt, DefLng, DefStr, DefBool, DefDbl, DefObj, DefVar) per la tipizzazione implicita per intervallo alfabetico.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleDefTypeDirectives()
    {
        // Arrange
        var code = """
            DefInt A-C
            DefLng I-N
            DefStr S-Z
            DefBool B
            DefDbl D
            DefObj O
            DefVar V

            Public Sub CalculateValues(iCount, sName, bFlag, dRate)
                Dim aIndex
                Dim sResult
                aIndex = 10
                sResult = sName & CStr(iCount)
                Call ProcessOutput(sResult)
            End Sub

            Private Sub ProcessOutput(ByVal text As String)
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("DefTypeMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var calcSub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "CalculateValues");
        calcSub.Should().NotBeNull();
        calcSub!.Parameters.Should().HaveCount(4);

        var procSub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "ProcessOutput");
        procSub.Should().NotBeNull();

        var callRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == calcSub.Id && r.TargetId == procSub!.Id);
        callRel.Should().NotBeNull();
    }

    #endregion

    #region Fixed-Length Strings and Multidimensional Arrays in UDTs

    /// <summary>
    /// Verifica il parsing e l'estrazione di strutture UDT (User-Defined Types)
    /// contenenti stringhe a lunghezza fissa (String * N) e array multidimensionali a dimensione fissa.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleFixedLengthStringsAndMultidimensionalArraysInUDTs()
    {
        // Arrange
        var code = """
            Public Type RecordHeader
                RecordId As Long
                RecordType As String * 4
                Timestamp As Double
            End Type

            Public Type CustomerEntity
                Header As RecordHeader
                CustomerCode As String * 10
                CompanyName As String * 40
                TaxNumber As String * 16
                Matrix(1 To 12, 1 To 31) As Currency
                HistoryBuffer(0 To 255) As Byte
            End Type

            Public Sub ProcessCustomer(ByRef cust As CustomerEntity)
                cust.CustomerCode = "CUST0001"
                cust.CompanyName = "Acme Corporation"
                Call SaveCustomer(cust)
            End Sub

            Private Sub SaveCustomer(ByRef cust As CustomerEntity)
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("CustomerTypes.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var headerType = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Type && s.Name == "RecordHeader");
        var customerType = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Type && s.Name == "CustomerEntity");

        headerType.Should().NotBeNull();
        customerType.Should().NotBeNull();

        var procMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "ProcessCustomer");
        var saveMethod = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "SaveCustomer");

        procMethod.Should().NotBeNull();
        saveMethod.Should().NotBeNull();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == procMethod!.Id).ToList();
        calls.Should().Contain(r => r.TargetId == saveMethod!.Id);
    }

    #endregion

    #region Parameterized Properties (Property Get / Let with Index Parameters)

    /// <summary>
    /// Verifica il parsing e l'estrazione di proprietà parametrizzate (Property Get / Let / Set con parametri indice/matrice).
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleParameterizedPropertiesWithMultipleIndices()
    {
        // Arrange
        var code = """
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "MatrixGrid"

            Private m_Values(1 To 100, 1 To 100) As Double

            Public Property Get CellValue(ByVal row As Long, ByVal col As Long) As Double
                CellValue = m_Values(row, col)
            End Property

            Public Property Let CellValue(ByVal row As Long, ByVal col As Long, ByVal value As Double)
                m_Values(row, col) = value
                Call NotifyCellUpdated(row, col)
            End Property

            Private Sub NotifyCellUpdated(ByVal row As Long, ByVal col As Long)
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("MatrixGrid.cls", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var gridClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "MatrixGrid");
        gridClass.Should().NotBeNull();

        var getProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "CellValue" && s.Parameters.Count == 2);
        getProp.Should().NotBeNull();

        var notifySub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "NotifyCellUpdated");
        notifySub.Should().NotBeNull();
    }

    #endregion

    #region Multiple Interface Implementation on Single Class

    /// <summary>
    /// Verifica che una classe VB6 che implementa molteplici interfacce distinte (Implements IFoo, Implements IBar)
    /// venga correttamente analizzata con tutte le relative relazioni ImplementsInterface e firme dei metodi associati.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldExtractMultipleImplementedInterfacesOnSingleClass()
    {
        // Arrange
        var iface1 = """
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "IPersistable"
            Public Sub Save()
            End Sub
            Public Sub Load(ByVal id As Long)
            End Sub
            """;

        var iface2 = """
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "IPrintable"
            Public Sub PrintDocument()
            End Sub
            """;

        var iface3 = """
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "ILoggable"
            Public Function GetLogSummary() As String
            End Function
            """;

        var implClass = """
            VERSION 1.0 CLASS
            BEGIN
              MultiUse = -1  'True
            END
            Attribute VB_Name = "OrderDocument"

            Implements IPersistable
            Implements IPrintable
            Implements ILoggable

            Private Sub IPersistable_Save()
                Call InternalSave
            End Sub

            Private Sub IPersistable_Load(ByVal id As Long)
            End Sub

            Private Sub IPrintable_PrintDocument()
            End Sub

            Private Function ILoggable_GetLogSummary() As String
                ILoggable_GetLogSummary = "OrderLog"
            End Function

            Private Sub InternalSave()
            End Sub
            """;

        var graph = new CodeGraph();
        var res1 = ModuleExtractor.ExtractModule(graph, "IPersistable.cls", iface1);
        var res2 = ModuleExtractor.ExtractModule(graph, "IPrintable.cls", iface2);
        var res3 = ModuleExtractor.ExtractModule(graph, "ILoggable.cls", iface3);
        var resImpl = ModuleExtractor.ExtractModule(graph, "OrderDocument.cls", implClass);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        // Act
        builder.BuildGraph(new[] { res1, res2, res3, resImpl });

        // Assert
        resImpl.Success.Should().BeTrue();
        resImpl.ParseErrors.Should().BeEmpty();

        var orderClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "OrderDocument");
        orderClass.Should().NotBeNull();

        var ifaceSym1 = graph.Symbols.FirstOrDefault(s => s.Name == "IPersistable");
        var ifaceSym2 = graph.Symbols.FirstOrDefault(s => s.Name == "IPrintable");
        var ifaceSym3 = graph.Symbols.FirstOrDefault(s => s.Name == "ILoggable");

        ifaceSym1.Should().NotBeNull();
        ifaceSym2.Should().NotBeNull();
        ifaceSym3.Should().NotBeNull();

        var implementsRels = graph.Relationships.Where(r => r.Kind == RelationshipKind.Implements && r.SourceId == orderClass!.Id).ToList();
        implementsRels.Should().HaveCount(3);
        implementsRels.Should().Contain(r => r.TargetId == ifaceSym1!.Id);
        implementsRels.Should().Contain(r => r.TargetId == ifaceSym2!.Id);
        implementsRels.Should().Contain(r => r.TargetId == ifaceSym3!.Id);
    }

    #endregion

    #region Win32 API Declare with Ordinal and Custom Aliases

    /// <summary>
    /// Verifica il parsing di dichiarazioni Win32 API con alias contenenti ordinali numerici (es. Alias "#42")
    /// e alias con nomi espliciti (es. Alias "CreateWindowExA").
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleApiDeclaresWithOrdinalAndCustomAliases()
    {
        // Arrange
        var code = """
            Private Declare Function GetSpecialProc Lib "custom32.dll" Alias "#105" (ByVal flag As Long) As Long
            Private Declare Sub ShowWindow Lib "user32.dll" Alias "ShowWindow" (ByVal hWnd As Long, ByVal nCmdShow As Long)
            Private Declare Function FormatMessage Lib "kernel32" Alias "FormatMessageA" (ByVal dwFlags As Long, lpSource As Any, ByVal dwMessageId As Long, ByVal dwLanguageId As Long, ByVal lpBuffer As String, ByVal nSize As Long, Arguments As Long) As Long

            Public Sub InitializeWindow(ByVal hWnd As Long)
                Call ShowWindow(hWnd, 1)
                Dim res As Long
                res = GetSpecialProc(1)
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("ApiDeclares.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var getSpecialProc = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "GetSpecialProc");
        var showWindow = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "ShowWindow");
        var formatMessage = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "FormatMessage");

        getSpecialProc.Should().NotBeNull();
        showWindow.Should().NotBeNull();
        formatMessage.Should().NotBeNull();

        var initSub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "InitializeWindow");
        initSub.Should().NotBeNull();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == initSub!.Id).ToList();
        calls.Should().Contain(r => r.TargetId == showWindow!.Id);
        calls.Should().Contain(r => r.TargetId == getSpecialProc!.Id);
    }

    #endregion

    #region Dynamic Array ReDim Preserve and Erase

    /// <summary>
    /// Verifica il parsing di istruzioni per la gestione di array dinamici:
    /// ReDim, ReDim Preserve multidimensionale, Erase, LBound e UBound.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleDynamicArraysReDimPreserveAndErase()
    {
        // Arrange
        var code = """
            Public Sub ManageDynamicMatrix()
                Dim buffer() As Byte
                Dim grid() As Double
                Dim i As Long
                Dim j As Long

                ReDim buffer(0 To 1024)
                ReDim grid(1 To 10, 1 To 20)

                grid(1, 1) = 3.14
                ReDim Preserve grid(1 To 10, 1 To 50)

                For i = LBound(grid, 1) To UBound(grid, 1)
                    For j = LBound(grid, 2) To UBound(grid, 2)
                        grid(i, j) = i * j
                    Next j
                Next i

                Erase buffer
                Erase grid
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("DynamicArrays.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var manageSub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "ManageDynamicMatrix");
        manageSub.Should().NotBeNull();
    }

    #endregion

    #region ParamArray with Optional and ByRef Arguments

    /// <summary>
    /// Verifica il parsing di routine con parametri facoltativi (Optional con valori predefiniti)
    /// combinati con l'ultimo parametro ParamArray.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleParamArrayWithOptionalDefaults()
    {
        // Arrange
        var code = """
            Public Sub LogDiagnostic(ByVal categoryId As Long, Optional ByVal prefix As String = "INFO: ", Optional ByVal includeTimestamp As Boolean = True, ParamArray details() As Variant)
                Dim i As Long
                For i = LBound(details) To UBound(details)
                    Call WriteOutput(prefix & CStr(details(i)))
                Next i
            End Sub

            Private Sub WriteOutput(ByVal msg As String)
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("ParamArrayMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var logSub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "LogDiagnostic");
        logSub.Should().NotBeNull();
        logSub!.Parameters.Should().HaveCount(4);

        var paramNames = logSub.Parameters.Select(p => p.Name).ToList();
        paramNames.Should().Contain("categoryId");
        paramNames.Should().Contain("prefix");
        paramNames.Should().Contain("includeTimestamp");
        paramNames.Should().Contain("details");

        var writeSub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "WriteOutput");
        writeSub.Should().NotBeNull();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == logSub.Id).ToList();
        calls.Should().Contain(r => r.TargetId == writeSub!.Id);
    }

    #endregion

    #region Static Local Variables and Numeric Line Labels

    /// <summary>
    /// Verifica il parsing e l'estrazione di routine con variabili locali Static e gestione errori con label numeriche.
    /// </summary>
    [Fact]
    public void ParserAndExtractor_ShouldHandleStaticLocalsAndNumericLabels()
    {
        // Arrange
        var code = """
            Public Function GetSequentialId() As Long
                Static nextId As Long
                Static isInitialized As Boolean
                
                On Error GoTo 9000

            10: If Not isInitialized Then
            20:     nextId = 1000
            30:     isInitialized = True
            40: End If

            50: nextId = nextId + 1
            60: GetSequentialId = nextId
                Exit Function

            9000:
                Call HandleError("GetSequentialId", Err.Number)
                GetSequentialId = -1
            End Function

            Private Sub HandleError(ByVal routine As String, ByVal errCode As Long)
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("StaticAndLabels.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var seqFunc = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "GetSequentialId");
        seqFunc.Should().NotBeNull();

        var errSub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "HandleError");
        errSub.Should().NotBeNull();

        var calls = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == seqFunc!.Id).ToList();
        calls.Should().Contain(r => r.TargetId == errSub!.Id);
    }

    #endregion
}
