using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6GrammarAndSemanticsDeepTests
{
    #region Complex User-Defined Types (UDT) and Multi-Dimensional Arrays

    /// <summary>
    /// Verifica il parsing e l'estrazione di strutture UDT complesse con stringhe a lunghezza fissa,
    /// matrici multi-dimensionali a limiti espliciti (1 To 4, 1 To 4) e tipi definiti dall'utente nidificati.
    /// </summary>
    [Fact]
    public void Vb6Extractor_ShouldExtractComplexUDTWithMultiDimArraysAndFixedStrings()
    {
        // Arrange
        var code = """
            VERSION 5.00
            Attribute VB_Name = "DataTypesMod"

            Public Type Coordinates
                X As Double
                Y As Double
                Z As Double
            End Type

            Public Type SpatialRecord
                RecordId As Long
                Label As String * 64
                Matrix(1 To 4, 1 To 4) As Double
                Position As Coordinates
                Flags(0 To 7) As Byte
            End Type

            Public Sub InitializeRecord(ByRef rec As SpatialRecord)
                rec.RecordId = 100
                rec.Label = "Default spatial entity"
                rec.Position.X = 0#
                rec.Position.Y = 0#
                rec.Position.Z = 0#
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("DataTypesMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var coordType = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Type && s.Name == "Coordinates");
        var recordType = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Type && s.Name == "SpatialRecord");
        var initSub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "InitializeRecord");

        coordType.Should().NotBeNull();
        recordType.Should().NotBeNull();
        initSub.Should().NotBeNull();
        initSub!.Parameters.Should().HaveCount(1);
    }

    #endregion

    #region Multi-Parameter Property Get / Let Indexers

    /// <summary>
    /// Verifica che le procedure Property Get e Property Let con molteplici parametri indice
    /// (es. accesso a griglie matriciali a due dimensioni) vengano estratte con tutti i parametri posizionali.
    /// </summary>
    [Fact]
    public void Vb6Extractor_ShouldExtractMultiParameterPropertyIndexers()
    {
        // Arrange
        var code = """
            VERSION 5.00
            Attribute VB_Name = "GridMatrix"

            Private m_Cells(0 To 99, 0 To 99) As Variant

            Public Property Get CellValue(ByVal row As Long, ByVal col As Long) As Variant
                CellValue = m_Cells(row, col)
            End Property

            Public Property Let CellValue(ByVal row As Long, ByVal col As Long, ByVal newValue As Variant)
                m_Cells(row, col) = newValue
            End Property

            Public Sub ClearCell(ByVal row As Long, ByVal col As Long)
                CellValue(row, col) = Empty
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("GridMatrix.cls", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var propGets = graph.Symbols.Where(s => s.Kind == SymbolKind.Property && s.Name == "CellValue").ToList();
        propGets.Should().NotBeEmpty();

        var clearSub = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "ClearCell");
        clearSub.Should().NotBeNull();
    }

    #endregion

    #region Line Continuations with Interleaved Comments and Multi-Statements

    /// <summary>
    /// Verifica la tolleranza del parser a costrutti complessi con continuazioni di riga '_'
    /// intervallate da commenti e statement multipli su una singola riga separati da due punti ':'.
    /// </summary>
    [Fact]
    public void Vb6Extractor_ShouldParseComplexLineContinuationsAndColonSeparatedStatements()
    {
        // Arrange
        var code = """
            VERSION 5.00
            Attribute VB_Name = "FlowMod"

            Public Sub ProcessComplexMath(ByVal a As Long, ByVal b As Long, ByRef result As Long)
                Dim temp1 As Long: Dim temp2 As Long: Dim temp3 As Long
                
                temp1 = a * 2: temp2 = b * 3
                
                result = temp1 + _
                         temp2 + _
                         (a * b)
                         
                If result > 100 Then: result = 100: temp3 = 1: Else: temp3 = 0: End If
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("FlowMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var method = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "ProcessComplexMath");
        method.Should().NotBeNull();
        method!.Parameters.Should().HaveCount(3);
    }

    #endregion

    #region Multiple Interface Implementation via Implements

    /// <summary>
    /// Verifica che una classe VB6 che implementa molteplici interfacce tramite la keyword Implements
    /// registri correttamente sia le interfacce implementate che i metodi di interfaccia espliciti.
    /// </summary>
    [Fact]
    public void Vb6Extractor_ShouldHandleMultipleImplementsInterfacesInSingleClass()
    {
        // Arrange
        var iface1 = """
            VERSION 5.00
            Attribute VB_Name = "ISerializable"
            Public Sub Serialize(ByVal targetStream As Object)
            End Sub
            Public Sub Deserialize(ByVal sourceStream As Object)
            End Sub
            """;

        var iface2 = """
            VERSION 5.00
            Attribute VB_Name = "IValidator"
            Public Function Validate() As Boolean
            End Function
            """;

        var implClass = """
            VERSION 5.00
            Attribute VB_Name = "DocumentEntity"

            Implements ISerializable
            Implements IValidator

            Private Sub ISerializable_Serialize(ByVal targetStream As Object)
                ' implementation
            End Sub

            Private Sub ISerializable_Deserialize(ByVal sourceStream As Object)
                ' implementation
            End Sub

            Private Function IValidator_Validate() As Boolean
                IValidator_Validate = True
            End Function
            """;

        var graph = new CodeGraph();
        var res1 = ModuleExtractor.ExtractModule(graph, "ISerializable.cls", iface1);
        var res2 = ModuleExtractor.ExtractModule(graph, "IValidator.cls", iface2);
        var res3 = ModuleExtractor.ExtractModule(graph, "DocumentEntity.cls", implClass);

        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);
        var builder = new CallGraphBuilder(graph, symbolResolver);

        // Act
        builder.BuildGraph(new[] { res1, res2, res3 });

        // Assert
        res1.Success.Should().BeTrue();
        res2.Success.Should().BeTrue();
        res3.Success.Should().BeTrue();

        var docClass = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "DocumentEntity");
        var implementsRels = graph.Relationships.Where(r => r.SourceId == docClass.Id && r.Kind == RelationshipKind.Implements).ToList();
        implementsRels.Should().HaveCount(2);
    }

    #endregion

    #region Enums with Negative and Hexadecimal Values

    /// <summary>
    /// Verifica che enumerazioni con costanti esadecimali (&H...) e negative (-1)
    /// vengano analizzate senza errori e i singoli membri vengano estratti.
    /// </summary>
    [Fact]
    public void Vb6Extractor_ShouldExtractEnumsWithHexAndNegativeConstants()
    {
        // Arrange
        var code = """
            VERSION 5.00
            Attribute VB_Name = "ConstantsMod"

            Public Enum SecurityFlags
                SEC_NONE = 0
                SEC_READ = &H1
                SEC_WRITE = &H2
                SEC_EXECUTE = &H4
                SEC_ALL_ACCESS = &HFFFF
                SEC_INVALID = -1
            End Enum

            Public Function CheckAccess(ByVal flags As SecurityFlags) As Boolean
                CheckAccess = (flags And SEC_READ) <> 0
            End Function
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("ConstantsMod.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var enumSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Enum && s.Name == "SecurityFlags");
        enumSym.Should().NotBeNull();

        var members = graph.GetChildren(enumSym!.Id).ToList();
        members.Should().NotBeEmpty();
        members.Select(m => m.Name).Should().Contain(new[] { "SEC_ALL_ACCESS", "SEC_INVALID" });
    }

    #endregion
}
