using FluentAssertions;
using Macondo.Semantic.Extraction;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpSymbolExtractionTests
{
    /// <summary>
    /// Verifica che CSharpExtractor riconosca ed estragga correttamente tutti i costrutti di tipo C#
    /// fondamentali (Classi, Interfacce, Struct, Record, ed Enum con i relativi valori numerici/costanti).
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractClassStructInterfaceRecordAndEnum()
    {
        // Arrange
        var code = """
            namespace MyApp.Core
            {
                public class ServiceManager
                {
                    public void Run() { }
                }

                public interface IWorker
                {
                    void Execute();
                }

                public struct Point2D
                {
                    public double X;
                    public double Y;
                }

                public record UserDto(string Name, int Age);

                public enum AppStatus
                {
                    Inactive = 0,
                    Active = 1,
                    Paused = 2
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("CoreTypes.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        result.ParseErrors.Should().BeEmpty();

        var classSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "ServiceManager");
        classSym.Should().NotBeNull();
        classSym!.QualifiedName.Should().Be("MyApp.Core.ServiceManager");
        classSym.Accessibility.Should().Be(Accessibility.Public);

        var ifaceSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Interface && s.Name == "IWorker");
        ifaceSym.Should().NotBeNull();
        ifaceSym!.QualifiedName.Should().Be("MyApp.Core.IWorker");

        var structSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Struct && s.Name == "Point2D");
        structSym.Should().NotBeNull();
        structSym!.QualifiedName.Should().Be("MyApp.Core.Point2D");

        var recordSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Record && s.Name == "UserDto");
        recordSym.Should().NotBeNull();
        recordSym!.QualifiedName.Should().Be("MyApp.Core.UserDto");

        var enumSym = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Enum && s.Name == "AppStatus");
        enumSym.Should().NotBeNull();
        enumSym!.QualifiedName.Should().Be("MyApp.Core.AppStatus");

        var enumMembers = graph.GetChildren(enumSym.Id).ToList();
        enumMembers.Should().HaveCount(3);
        enumMembers.Select(m => m.Name).Should().Contain(new[] { "Inactive", "Active", "Paused" });
    }

    /// <summary>
    /// Verifica il supporto ai File-Scoped Namespaces introdotti in C# 10 (es. 'namespace Foo.Bar;')
    /// e la corretta propagazione del QualifiedName sui tipi e metodi contenuti.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldSupportFileScopedNamespaces()
    {
        // Arrange
        var code = """
            namespace MyApp.Services.Billing;

            public class InvoiceProcessor
            {
                public void GenerateInvoice() { }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("InvoiceProcessor.cs", code);

        // Assert
        result.Success.Should().BeTrue();
        var cls = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name == "InvoiceProcessor");
        cls.Should().NotBeNull();
        cls!.QualifiedName.Should().Be("MyApp.Services.Billing.InvoiceProcessor");

        var method = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "GenerateInvoice");
        method.Should().NotBeNull();
        method!.QualifiedName.Should().Be("MyApp.Services.Billing.InvoiceProcessor.GenerateInvoice");
    }

    /// <summary>
    /// Verifica l'estrazione delle firme dei metodi C#, inclusi tipi di ritorno (void e tipi specifici),
    /// parametri posizionali e parametri opzionali con valori di default.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractMethods_WithParametersAndReturnTypes()
    {
        // Arrange
        var code = """
            namespace MyApp
            {
                public class Calculator
                {
                    public int Add(int a, int b)
                    {
                        return a + b;
                    }

                    private string FormatResult(double val, string prefix = "Val: ")
                    {
                        return prefix + val;
                    }

                    public static void Reset() { }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Calculator.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var addMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Add");
        addMethod.Should().NotBeNull();
        addMethod!.Kind.Should().Be(SymbolKind.Method);
        addMethod.ReturnType.Should().Be("int");
        addMethod.Accessibility.Should().Be(Accessibility.Public);
        addMethod.Parameters.Should().HaveCount(2);
        addMethod.Parameters[0].Name.Should().Be("a");
        addMethod.Parameters[0].DeclaredTypeName.Should().Be("int");
        addMethod.Parameters[0].IsOptional.Should().BeFalse();
        addMethod.Parameters[1].Name.Should().Be("b");

        var formatMethod = graph.Symbols.FirstOrDefault(s => s.Name == "FormatResult");
        formatMethod.Should().NotBeNull();
        formatMethod!.Accessibility.Should().Be(Accessibility.Private);
        formatMethod.ReturnType.Should().Be("string");
        formatMethod.Parameters.Should().HaveCount(2);
        formatMethod.Parameters[1].IsOptional.Should().BeTrue();

        var resetMethod = graph.Symbols.FirstOrDefault(s => s.Name == "Reset");
        resetMethod.Should().NotBeNull();
        resetMethod!.IsStatic.Should().BeTrue();
        resetMethod.ReturnType.Should().Be("void");
    }

    /// <summary>
    /// Verifica che i costruttori di classe C# vengano estratti come simboli Method con ReturnType nullo
    /// e con l'elenco completo dei parametri di istanziazione.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractConstructors()
    {
        // Arrange
        var code = """
            namespace MyApp
            {
                public class Person
                {
                    private string _name;

                    public Person(string name)
                    {
                        _name = name;
                    }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Person.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var ctor = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method && s.Name == "Person");
        ctor.Should().NotBeNull();
        ctor!.ReturnType.Should().BeNull();
        ctor.Accessibility.Should().Be(Accessibility.Public);
        ctor.Parameters.Should().HaveCount(1);
        ctor.Parameters[0].Name.Should().Be("name");
    }

    /// <summary>
    /// Verifica l'estrazione di proprietà, campi di istanza, campi statici, costanti ('const')
    /// ed eventi ('event EventHandler') con i rispettivi tipi dichiarati.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractPropertiesFieldsEventsAndConstants()
    {
        // Arrange
        var code = """
            namespace MyApp
            {
                public class ConfigModel
                {
                    public const int MaxLimit = 100;
                    public static readonly string DefaultName = "App";
                    private int _retryCount;
                    public event System.EventHandler OnChanged;

                    public int RetryCount
                    {
                        get { return _retryCount; }
                        set { _retryCount = value; }
                    }

                    public static bool IsEnabled { get; set; }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("ConfigModel.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        var constSym = graph.Symbols.FirstOrDefault(s => s.Name == "MaxLimit");
        constSym.Should().NotBeNull();
        constSym!.Kind.Should().Be(SymbolKind.Constant);
        constSym.Accessibility.Should().Be(Accessibility.Public);
        constSym.IsStatic.Should().BeTrue();

        var fieldSym = graph.Symbols.FirstOrDefault(s => s.Name == "_retryCount");
        fieldSym.Should().NotBeNull();
        fieldSym!.Kind.Should().Be(SymbolKind.Field);
        fieldSym.Accessibility.Should().Be(Accessibility.Private);

        var eventSym = graph.Symbols.FirstOrDefault(s => s.Name == "OnChanged");
        eventSym.Should().NotBeNull();
        eventSym!.Kind.Should().Be(SymbolKind.Event);

        var propSym = graph.Symbols.FirstOrDefault(s => s.Name == "RetryCount");
        propSym.Should().NotBeNull();
        propSym!.Kind.Should().Be(SymbolKind.Property);
        propSym.DeclaredTypeName.Should().Be("int");

        var staticProp = graph.Symbols.FirstOrDefault(s => s.Name == "IsEnabled");
        staticProp.Should().NotBeNull();
        staticProp!.IsStatic.Should().BeTrue();
    }

    /// <summary>
    /// Verifica la mappatura corretta di tutti i modificatori di visibilità del linguaggio C#
    /// (public, private, protected, internal, protected internal, private protected) nell'enum Accessibility.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldExtractAccessibilitiesCorrectly()
    {
        // Arrange
        var code = """
            namespace MyApp
            {
                public class AccessTest
                {
                    public void Pub() { }
                    private void Priv() { }
                    protected void Prot() { }
                    internal void Intern() { }
                    protected internal void ProtIntern() { }
                    private protected void PrivProt() { }
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("AccessTest.cs", code);

        // Assert
        result.Success.Should().BeTrue();

        graph.Symbols.First(s => s.Name == "Pub").Accessibility.Should().Be(Accessibility.Public);
        graph.Symbols.First(s => s.Name == "Priv").Accessibility.Should().Be(Accessibility.Private);
        graph.Symbols.First(s => s.Name == "Prot").Accessibility.Should().Be(Accessibility.Protected);
        graph.Symbols.First(s => s.Name == "Intern").Accessibility.Should().Be(Accessibility.Internal);
        graph.Symbols.First(s => s.Name == "ProtIntern").Accessibility.Should().Be(Accessibility.ProtectedInternal);
        graph.Symbols.First(s => s.Name == "PrivProt").Accessibility.Should().Be(Accessibility.PrivateProtected);
    }

    /// <summary>
    /// Verifica che in caso di sintassi C# errata o incompleta l'estrattore non generi eccezioni non gestite,
    /// ma restituisca un risultato con Success == false e l'elenco dei messaggi di errore Roslyn in ParseErrors.
    /// </summary>
    [Fact]
    public void CSharpExtractor_ShouldCaptureParseErrorsOnInvalidSyntax()
    {
        // Arrange
        var invalidCode = """
            namespace InvalidApp
            {
                public class BrokenClass
                {
                    public void BadMethod( {
                }
            }
            """;

        // Act
        var (graph, result) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Broken.cs", invalidCode);

        // Assert
        result.Success.Should().BeFalse();
        result.ParseErrors.Should().NotBeEmpty();
    }
}
