using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Vb6Specific;

public class Vb6SymbolExtractionTests
{
    /// <summary>
    /// Verify that ModuleExtractor correctly extracts VB6 Sub and Function routines, including parameters
    /// for value/reference (ByVal/ByRef), optional parameters and return types.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractSubAndFunctionSymbols()
    {
        // Arrange
        var code = """
            Public Sub DoWork(ByVal id As Long, Optional ByRef name As String = "test")
                Dim localVal As Integer
            End Sub

            Private Function Calculate(ByVal a As Double, ByVal b As Double) As Double
                Calculate = a + b
            End Function
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("TestModule.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var moduleSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Module && s.Name == "TestModule");
        moduleSym.Should().NotBeNull();

        var doWorkSym = graph.Symbols.FirstOrDefault(s => s.Name == "DoWork");
        doWorkSym.Should().NotBeNull();
        doWorkSym!.Kind.Should().Be(SymbolKind.Method);
        doWorkSym.Accessibility.Should().Be(Accessibility.Public);
        doWorkSym.Parameters.Should().HaveCount(2);
        doWorkSym.Parameters[0].Name.Should().Be("id");
        doWorkSym.Parameters[0].DeclaredTypeName.Should().Be("Long");
        doWorkSym.Parameters[0].IsByVal.Should().BeTrue();
        doWorkSym.Parameters[1].Name.Should().Be("name");
        doWorkSym.Parameters[1].IsOptional.Should().BeTrue();

        var calcSym = graph.Symbols.FirstOrDefault(s => s.Name == "Calculate");
        calcSym.Should().NotBeNull();
        calcSym!.Kind.Should().Be(SymbolKind.Method);
        calcSym.Accessibility.Should().Be(Accessibility.Private);
        calcSym.ReturnType.Should().Be("Double");
    }

    /// <summary>
    /// Test VB6 property extraction in Property Get, Property Let, and Property Set variants.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractProperties_GetLetSet()
    {
        // Arrange
        var code = """
            Private m_Name As String

            Public Property Get Name() As String
                Name = m_Name
            End Property

            Public Property Let Name(ByVal value As String)
                m_Name = value
            End Property
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("Customer.cls", code);

        // Assert
        result.Success.Should().BeTrue();

        var propGet = graph.Symbols.FirstOrDefault(s => s.Name == "Name" && s.ReturnType == "String");
        propGet.Should().NotBeNull();
        propGet!.Kind.Should().Be(SymbolKind.Property);

        var fieldSym = graph.Symbols.FirstOrDefault(s => s.Name == "m_Name");
        fieldSym.Should().NotBeNull();
        fieldSym!.Kind.Should().Be(SymbolKind.Field);
    }

    /// <summary>
    /// Tests the extraction of constants ('Public/Private Const') and enumerations ('Enum ... End Enum') with their members.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractConstantsAndEnums()
    {
        // Arrange
        var code = """
            Public Const MAX_RETRIES As Integer = 5
            Private Const DEFAULT_SERVER = "127.0.0.1"

            Public Enum OrderStatus
                Pending = 0
                Approved = 1
                Cancelled = 2
            End Enum
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("Config.bas", code);

        // Assert
        result.Success.Should().BeTrue();

        var constSym = graph.Symbols.FirstOrDefault(s => s.Name == "MAX_RETRIES");
        constSym.Should().NotBeNull();
        constSym!.Kind.Should().Be(SymbolKind.Constant);
        constSym.Accessibility.Should().Be(Accessibility.Public);

        var enumSym = graph.Symbols.FirstOrDefault(s => s.Name == "OrderStatus");
        enumSym.Should().NotBeNull();
        enumSym!.Kind.Should().Be(SymbolKind.Enum);

        var enumMembers = graph.GetChildren(enumSym.Id).ToList();
        enumMembers.Should().HaveCount(3);
        enumMembers.Select(m => m.Name).Should().Contain(new[] { "Pending", "Approved", "Cancelled" });
    }

    /// <summary>
    /// Verifies that module members are linked to the container module via CONTAINS relationships.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractContainsRelationships()
    {
        // Arrange
        var code = """
            Public Sub MethodA()
            End Sub
            Public Sub MethodB()
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("MyModule.bas", code);

        // Assert
        result.Success.Should().BeTrue();
        var moduleSym = graph.Symbols.First(s => s.Kind == SymbolKind.Module);
        var containsRels = graph.Relationships.Where(r => r.Kind == RelationshipKind.Contains && r.SourceId == moduleSym.Id).ToList();

        containsRels.Should().HaveCount(2);
    }

    /// <summary>
    /// Tests the extraction of UDT ('Type ... End Type') structures with primitive fields and fixed-size arrays.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractUserDefinedTypes_UDT_WithMembersAndArrays()
    {
        // Arrange
        var code = """
            Public Type CustomerInfo
                Id As Long
                Name As String
                Addresses(10) As String
                IsActive As Boolean
            End Type
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("DataTypes.bas", code);

        // Assert
        result.Success.Should().BeTrue();

        var udtSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Type && s.Name == "CustomerInfo");
        udtSym.Should().NotBeNull();
        udtSym!.Accessibility.Should().Be(Accessibility.Public);

        var fields = graph.GetChildren(udtSym.Id).ToList();
        fields.Should().HaveCount(4);

        var idField = fields.FirstOrDefault(f => f.Name == "Id");
        idField.Should().NotBeNull();
        idField!.DeclaredTypeName.Should().Be("Long");

        var addrField = fields.FirstOrDefault(f => f.Name == "Addresses");
        addrField.Should().NotBeNull();
        addrField!.IsArray.Should().BeTrue();
    }

    /// <summary>
    /// Check the extraction of the UI controls defined in the descriptive header of the VB6 forms (.frm).
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldExtractFormControls_FromFrmHeader()
    {
        // Arrange
        var code = """
            VERSION 5.00
            Begin VB.Form MainForm
               Caption = "Main Form"
               Begin VB.CommandButton cmdSubmit
                  Caption = "Submit"
               End
               Begin VB.TextBox txtUsername
                  Text = ""
               End
            End
            Attribute VB_Name = "MainForm"

            Private Sub cmdSubmit_Click()
                MsgBox txtUsername.Text
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("MainForm.frm", code);

        // Assert
        result.Success.Should().BeTrue();

        var formSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Form && s.Name == "MainForm");
        formSym.Should().NotBeNull();

        var controls = graph.GetChildren(formSym!.Id).Where(s => s.Kind == SymbolKind.Field).ToList();
        controls.Should().Contain(c => c.Name == "cmdSubmit" && c.DeclaredTypeName == "VB.CommandButton");
        controls.Should().Contain(c => c.Name == "txtUsername" && c.DeclaredTypeName == "VB.TextBox");
    }

    /// <summary>
    /// Check the marking of the IsNewInstance (for 'Dim ... As New') and IsArray (for buffer and array) flags.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldCaptureIsNewInstance_AndIsArrayFlags()
    {
        // Arrange
        var code = """
            Private m_Helper As New DatabaseHelper
            Private m_Buffer() As Byte
            Public m_Numbers(5) As Integer
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("Service.cls", code);

        // Assert
        result.Success.Should().BeTrue();

        var helperSym = graph.Symbols.FirstOrDefault(s => s.Name == "m_Helper");
        helperSym.Should().NotBeNull();
        helperSym!.IsNewInstance.Should().BeTrue();
        helperSym.DeclaredTypeName.Should().Be("DatabaseHelper");

        var bufferSym = graph.Symbols.FirstOrDefault(s => s.Name == "m_Buffer");
        bufferSym.Should().NotBeNull();
        bufferSym!.IsArray.Should().BeTrue();

        var numbersSym = graph.Symbols.FirstOrDefault(s => s.Name == "m_Numbers");
        numbersSym.Should().NotBeNull();
        numbersSym!.IsArray.Should().BeTrue();
    }

    /// <summary>
    /// Tests extraction of procedures and variables declared with Static modifier in VB6.
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldCaptureStaticProceduresAndVariables()
    {
        // Arrange
        var code = """
            Public Static Sub RunProcess()
                Static hitCount As Long
                hitCount = hitCount + 1
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("Utility.bas", code);

        // Assert
        result.Success.Should().BeTrue();

        var runProcess = graph.Symbols.FirstOrDefault(s => s.Name == "RunProcess");
        runProcess.Should().NotBeNull();
        runProcess!.IsStatic.Should().BeTrue();

        var hitCount = graph.Symbols.FirstOrDefault(s => s.Name == "hitCount");
        hitCount.Should().NotBeNull();
        hitCount!.IsStatic.Should().BeTrue();
    }

    /// <summary>
    /// Check heuristic detection of UI event handlers based on naming conventions (e.g. 'cmdSubmit_Click', 'Form_Load').
    /// </summary>
    [Fact]
    public void ModuleExtractor_ShouldDetectLikelyEventHandlerNamingConventions()
    {
        // Arrange
        var code = """
            Private Sub cmdSubmit_Click()
            End Sub

            Private Sub Form_Load()
            End Sub

            Private Sub txtName_Change()
            End Sub

            Private Sub RegularRoutine()
            End Sub
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeSingleModule("Form1.frm", code);

        // Assert
        result.Success.Should().BeTrue();
        result.LikelyEventHandlerNames.Should().Contain("cmdSubmit_Click");
        result.LikelyEventHandlerNames.Should().Contain("Form_Load");
        result.LikelyEventHandlerNames.Should().Contain("txtName_Change");
        result.LikelyEventHandlerNames.Should().NotContain("RegularRoutine");
    }
}
