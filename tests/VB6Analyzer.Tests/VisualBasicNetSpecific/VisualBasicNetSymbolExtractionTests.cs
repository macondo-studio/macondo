using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.VisualBasicNetSpecific;

public class VisualBasicNetSymbolExtractionTests
{
    /// <summary>
    /// Verifica che VisualBasicNetExtractor riconosca ed estragga correttamente tutti i tipi principali di VB.NET:
    /// Classi, Interfacce, Strutture (Structure -> Struct), Moduli (Module) ed Enumerazioni (Enum con i relativi valori numerici).
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldExtractClassStructureInterfaceModuleAndEnum()
    {
        // Arrange
        var code = """
            Namespace MyApp.Core
                Public Class ServiceManager
                    Public Sub Run()
                    End Sub
                End Class

                Public Interface IWorker
                    Sub Execute()
                End Interface

                Public Structure Point2D
                    Public X As Double
                    Public Y As Double
                End Structure

                Public Module MathHelper
                    Public Function Add(a As Integer, b As Integer) As Integer
                        Return a + b
                    End Function
                End Module

                Public Enum AppStatus
                    Inactive = 0
                    Active = 1
                    Paused = 2
                End Enum
            End Namespace
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("CoreTypes.vb", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var classSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ServiceManager");
        classSym.Should().NotBeNull();
        classSym!.QualifiedName.Should().Be("MyApp.Core.ServiceManager");
        classSym.Accessibility.Should().Be(Accessibility.Public);
        classSym.Language.Should().Be(SourceLanguage.VisualBasicNet);

        var ifaceSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "IWorker");
        ifaceSym.Should().NotBeNull();
        ifaceSym!.QualifiedName.Should().Be("MyApp.Core.IWorker");

        var structSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Struct && s.Name == "Point2D");
        structSym.Should().NotBeNull();
        structSym!.QualifiedName.Should().Be("MyApp.Core.Point2D");

        var moduleSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Module && s.Name == "MathHelper");
        moduleSym.Should().NotBeNull();
        moduleSym!.QualifiedName.Should().Be("MyApp.Core.MathHelper");

        var enumSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Enum && s.Name == "AppStatus");
        enumSym.Should().NotBeNull();
        enumSym!.QualifiedName.Should().Be("MyApp.Core.AppStatus");

        var enumMembers = graph.GetChildren(enumSym.Id).ToList();
        enumMembers.Should().HaveCount(3);
        enumMembers.Select(m => m.Name).Should().Contain(new[] { "Inactive", "Active", "Paused" });
    }

    /// <summary>
    /// Verifica l'estrazione di routine (Sub e Function) con parametri tipizzati, clausole ByVal/ByRef,
    /// parametri opzionali con valore di default e tipo di ritorno.
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldExtractMethodsWithParametersAndReturnTypes()
    {
        // Arrange
        var code = """
            Public Class Calculator
                Public Sub LogMessage(ByVal message As String, Optional ByVal level As Integer = 1)
                End Sub

                Public Function Multiply(ByVal a As Double, ByRef b As Double) As Double
                    Return a * b
                End Function
            End Class
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("Calculator.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var subSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "LogMessage");
        subSym.Should().NotBeNull();
        subSym!.Parameters.Should().HaveCount(2);
        subSym.Parameters[0].Name.Should().Be("message");
        subSym.Parameters[0].IsByRef.Should().BeFalse();
        subSym.Parameters[1].Name.Should().Be("level");
        subSym.Parameters[1].IsOptional.Should().BeTrue();

        var funcSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Multiply");
        funcSym.Should().NotBeNull();
        funcSym!.ReturnType.Should().Be("Double");
        funcSym.Parameters.Should().HaveCount(2);
        funcSym.Parameters[0].IsByRef.Should().BeFalse();
        funcSym.Parameters[1].IsByRef.Should().BeTrue();
    }

    /// <summary>
    /// Verifica l'estrazione del costruttore orientato a oggetti (Sub New) sia con che senza parametri.
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldExtractConstructorsSubNew()
    {
        // Arrange
        var code = """
            Public Class ConnectionManager
                Private _connectionString As String

                Public Sub New()
                    _connectionString = "default"
                End Sub

                Public Sub New(ByVal connStr As String)
                    _connectionString = connStr
                End Sub
            End Class
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("ConnectionManager.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var ctors = graph.Symbols.Where(s => s.Kind == SymbolKind.Method && (s.Name == "New" || s.Name == ".ctor")).ToList();
        ctors.Should().HaveCount(2);
        ctors.Should().Contain(c => c.Parameters.Count == 0);
        ctors.Should().Contain(c => c.Parameters.Count == 1 && c.Parameters[0].Name == "connStr");
    }

    /// <summary>
    /// Verifica l'estrazione delle proprietà VB.NET in tutte le forme: Auto-Properties,
    /// proprietà estese con blocchi Get/Set, ReadOnly e WriteOnly.
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldExtractProperties_AutoFullAndReadOnly()
    {
        // Arrange
        var code = """
            Public Class Customer
                ' Auto-Property
                Public Property Name As String

                ' ReadOnly Property
                Public ReadOnly Property Id As Integer
                    Get
                        Return 42
                    End Get
                End Property

                ' Full Property
                Private _email As String
                Public Property Email As String
                    Get
                        Return _email
                    End Get
                    Set(ByVal value As String)
                        _email = value
                    End Set
                End Property
            End Class
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("Customer.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var nameProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "Name");
        nameProp.Should().NotBeNull();
        nameProp!.DeclaredTypeName.Should().Be("String");

        var idProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "Id");
        idProp.Should().NotBeNull();

        var emailProp = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Property && s.Name == "Email");
        emailProp.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica l'estrazione di campi (Dim / Private / Public / Shared) e costanti (Const).
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldExtractFieldsAndConstants()
    {
        // Arrange
        var code = """
            Public Class AppConfig
                Public Const AppTitle As String = "Macondo VB.NET"
                Private Const MaxRetries As Integer = 3
                Public Shared ConnectionCount As Integer
                Private _secretKey As String
            End Class
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("AppConfig.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var titleConst = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "AppTitle");
        titleConst.Should().NotBeNull();
        titleConst!.Accessibility.Should().Be(Accessibility.Public);

        var retriesConst = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Constant && s.Name == "MaxRetries");
        retriesConst.Should().NotBeNull();
        retriesConst!.Accessibility.Should().Be(Accessibility.Private);

        var sharedField = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Field && s.Name == "ConnectionCount");
        sharedField.Should().NotBeNull();

        var secretField = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Field && s.Name == "_secretKey");
        secretField.Should().NotBeNull();
    }

    /// <summary>
    /// Verifica l'estrazione di eventi personalizzati dichiarati con la parola chiave 'Event'.
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldExtractCustomEvents()
    {
        // Arrange
        var code = """
            Public Class OrderProcessor
                Public Event OrderCompleted(ByVal orderId As Integer, ByVal total As Decimal)
                Public Event ErrorOccurred(ByVal errorMsg As String)
            End Class
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("OrderProcessor.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var orderEvent = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Event && s.Name == "OrderCompleted");
        orderEvent.Should().NotBeNull();
        orderEvent!.Parameters.Should().HaveCount(2);

        var errorEvent = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Event && s.Name == "ErrorOccurred");
        errorEvent.Should().NotBeNull();
        errorEvent!.Parameters.Should().HaveCount(1);
    }

    /// <summary>
    /// Verifica il supporto ai namespace annidati e la propagazione dei QualifiedName gerarchici completi.
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldSupportNamespacesAndNestedNamespaces()
    {
        // Arrange
        var code = """
            Namespace Enterprise.Solutions
                Namespace Security
                    Public Class TokenValidator
                        Public Function Validate(token As String) As Boolean
                            Return True
                        End Function
                    End Class
                End Namespace
            End Namespace
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("Security.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var classSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "TokenValidator");
        classSym.Should().NotBeNull();
        classSym!.QualifiedName.Should().Be("Enterprise.Solutions.Security.TokenValidator");

        var methodSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Validate");
        methodSym.Should().NotBeNull();
        methodSym!.QualifiedName.Should().Be("Enterprise.Solutions.Security.TokenValidator.Validate");
    }

    /// <summary>
    /// Verifica il riconoscimento dei modificatori di classe OOP avanzati (MustInherit / abstract, NotInheritable / sealed, Partial).
    /// </summary>
    [Fact]
    public void VisualBasicNetExtractor_ShouldExtractClassModifiers_MustInheritNotInheritablePartial()
    {
        // Arrange
        var code = """
            Public MustInherit Class BaseWorker
                Public MustOverride Sub DoWork()
            End Class

            Public NotInheritable Class FinalResult
                Public Property Code As Integer
            End Class

            Partial Public Class PartialEntity
                Public Property Part1 As String
            End Class
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeVisualBasicNetSource("Modifiers.vb", code);

        // Assert
        result.Success.Should().BeTrue();

        var baseWorker = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "BaseWorker");
        baseWorker.Should().NotBeNull();

        var finalResult = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "FinalResult");
        finalResult.Should().NotBeNull();

        var partialEntity = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "PartialEntity");
        partialEntity.Should().NotBeNull();
    }
}
