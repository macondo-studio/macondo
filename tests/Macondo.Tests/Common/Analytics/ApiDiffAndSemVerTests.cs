using FluentAssertions;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class ApiDiffAndSemVerTests
{
    /// <summary>
    /// Verifica che la rimozione di un metodo pubblico venga classificata come Major / Breaking Change.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenPublicMethodIsRemoved()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var publicMethodOld = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "CalculateDiscount",
            QualifiedName = "PricingService.CalculateDiscount",
            FilePath = "PricingService.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        oldGraph.Symbols.Add(publicMethodOld);

        // Nel newGraph il metodo pubblico è stato rimosso

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(1);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
        report.Differences[0].ChangeKind.Should().Be(ApiChangeKind.RemovedSymbol);
    }

    /// <summary>
    /// Verifica che l'aggiunta di una nuova interfaccia/metodo pubblico venga classificata come Minor.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectMinorAddition_WhenNewPublicApiIsAdded()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var newMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ExportToPdf",
            QualifiedName = "ReportService.ExportToPdf",
            FilePath = "ReportService.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        newGraph.Symbols.Add(newMethod);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(0);
        report.AdditionsCount.Should().Be(1);
        report.RecommendedBump.Should().Be(ApiChangeImpact.Minor);
    }

    /// <summary>
    /// Regression Test: Verifica che la presenza di sovraccarichi (overloads) di metodi o costruttori
    /// con lo stesso identico QualifiedName non sollevi System.ArgumentException su chiavi duplicate.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldHandle_OverloadedMethodsAndConstructors_WithoutThrowing()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        // 1. Costruttore senza argomenti
        var ctorParameterless = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "SelectedItemsDictionary",
            QualifiedName = "Unity.Cloud.Collaborate.UserInterface.SelectedItemsDictionary.SelectedItemsDictionary",
            FilePath = "SelectedItemsDictionary.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };

        // 2. Costruttore sovraccaricato con argomento int
        var ctorWithInt = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "SelectedItemsDictionary",
            QualifiedName = "Unity.Cloud.Collaborate.UserInterface.SelectedItemsDictionary.SelectedItemsDictionary",
            FilePath = "SelectedItemsDictionary.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        ctorWithInt.Parameters.Add(new ParameterSymbol { Name = "capacity", DeclaredTypeName = "int" });

        // 3. Metodo sovraccaricato 1: Add(TItem)
        var methodAdd1 = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Add",
            QualifiedName = "Unity.Cloud.Collaborate.UserInterface.SelectedItemsDictionary.Add",
            FilePath = "SelectedItemsDictionary.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        methodAdd1.Parameters.Add(new ParameterSymbol { Name = "item", DeclaredTypeName = "TItem" });

        // 4. Metodo sovraccaricato 2: Add(TKey, TItem)
        var methodAdd2 = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Add",
            QualifiedName = "Unity.Cloud.Collaborate.UserInterface.SelectedItemsDictionary.Add",
            FilePath = "SelectedItemsDictionary.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        methodAdd2.Parameters.Add(new ParameterSymbol { Name = "key", DeclaredTypeName = "TKey" });
        methodAdd2.Parameters.Add(new ParameterSymbol { Name = "item", DeclaredTypeName = "TItem" });

        // Popola sia baseline che current con tutti i sovraccarichi
        oldGraph.Symbols.AddRange(new[] { ctorParameterless, ctorWithInt, methodAdd1, methodAdd2 });
        newGraph.Symbols.AddRange(new[] { ctorParameterless, ctorWithInt, methodAdd1, methodAdd2 });

        // Act & Assert (non deve lanciare eccezioni e deve rilevare 0 differenze per grafi identici)
        var act = () => ApiDiffAnalyzer.Compare(oldGraph, newGraph);
        act.Should().NotThrow<System.ArgumentException>();

        var report = act();
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(0);
        report.AdditionsCount.Should().Be(0);
        report.TotalChanges.Should().Be(0);
    }

    /// <summary>
    /// Verifica che la rimozione di un singolo overload specifico su tre sia identificata come Breaking Change.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenSingleOverloadIsRemoved()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var overload1 = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Render",
            QualifiedName = "Renderer.Render",
            FilePath = "Renderer.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };

        var overload2 = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Render",
            QualifiedName = "Renderer.Render",
            FilePath = "Renderer.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        overload2.Parameters.Add(new ParameterSymbol { Name = "context", DeclaredTypeName = "RenderContext" });

        // Baseline ha entrambi gli overload
        oldGraph.Symbols.AddRange(new[] { overload1, overload2 });

        // Nuova versione mantiene solo overload1; overload2 è stato rimosso
        newGraph.Symbols.Add(overload1);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(1);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
        report.Differences.Should().ContainSingle(d => d.ChangeKind == ApiChangeKind.RemovedSymbol && d.SymbolQualifiedName == "Renderer.Render");
    }

    /// <summary>
    /// Verifica che l'aggiunta di un nuovo overload sia classificata come Minor Feature retrocompatibile.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectMinorAddition_WhenNewOverloadIsAdded()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var baseOverload = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Log",
            QualifiedName = "Logger.Log",
            FilePath = "Logger.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        baseOverload.Parameters.Add(new ParameterSymbol { Name = "message", DeclaredTypeName = "string" });

        var newOverload = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Log",
            QualifiedName = "Logger.Log",
            FilePath = "Logger.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        newOverload.Parameters.Add(new ParameterSymbol { Name = "exception", DeclaredTypeName = "Exception" });
        newOverload.Parameters.Add(new ParameterSymbol { Name = "message", DeclaredTypeName = "string" });

        oldGraph.Symbols.Add(baseOverload);
        newGraph.Symbols.AddRange(new[] { baseOverload, newOverload });

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(0);
        report.AdditionsCount.Should().Be(1);
        report.RecommendedBump.Should().Be(ApiChangeImpact.Minor);
        report.Differences.Should().ContainSingle(d => d.ChangeKind == ApiChangeKind.AddedSymbol && d.SymbolQualifiedName == "Logger.Log");
    }

    /// <summary>
    /// Verifica che simboli duplicati nello stesso grafo (es. classi parziali o definizioni multiple)
    /// vengano gestiti in modo resiliente senza duplicare errori o lanciare eccezioni.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldHandle_DuplicateIdenticalSymbols_Resiliently()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var sym1 = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "PartialService",
            QualifiedName = "MyNamespace.PartialService",
            FilePath = "PartialService.Part1.cs",
            Kind = SymbolKind.Class,
            Accessibility = Accessibility.Public
        };

        var sym2Duplicate = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "PartialService",
            QualifiedName = "MyNamespace.PartialService",
            FilePath = "PartialService.Part2.cs",
            Kind = SymbolKind.Class,
            Accessibility = Accessibility.Public
        };

        oldGraph.Symbols.AddRange(new[] { sym1, sym2Duplicate });
        newGraph.Symbols.AddRange(new[] { sym1, sym2Duplicate });

        // Act
        var act = () => ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        act.Should().NotThrow();
        var report = act();
        report.TotalChanges.Should().Be(0);
    }

    /// <summary>
    /// Verifica il comportamento delle proprietà VB6 con Property Get e Property Let con medesimo QualifiedName.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldHandle_VB6PropertyAccessors_WithSameQualifiedName()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var propGet = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "CustomerName",
            QualifiedName = "Customer.CustomerName",
            FilePath = "Customer.cls",
            Kind = SymbolKind.Property,
            Accessibility = Accessibility.Public,
            ReturnType = "String"
        };

        var propLet = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "CustomerName",
            QualifiedName = "Customer.CustomerName",
            FilePath = "Customer.cls",
            Kind = SymbolKind.Property,
            Accessibility = Accessibility.Public
        };
        propLet.Parameters.Add(new ParameterSymbol { Name = "vData", DeclaredTypeName = "String" });

        oldGraph.Symbols.AddRange(new[] { propGet, propLet });
        newGraph.Symbols.AddRange(new[] { propGet, propLet });

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.TotalChanges.Should().Be(0);
    }

    /// <summary>
    /// Verifica che la modifica del tipo di ritorno di un metodo pubblico venga identificata come Breaking Change.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenMethodReturnTypeChanges()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var oldMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "GetStatus",
            QualifiedName = "Service.GetStatus",
            FilePath = "Service.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public,
            ReturnType = "string"
        };
        var newMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "GetStatus",
            QualifiedName = "Service.GetStatus",
            FilePath = "Service.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public,
            ReturnType = "int"
        };

        oldGraph.Symbols.Add(oldMethod);
        newGraph.Symbols.Add(newMethod);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().BeGreaterThan(0);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
    }

    /// <summary>
    /// Verifica che la modifica del tipo di un parametro di un metodo pubblico venga identificata come Breaking Change.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenParameterTypeChanges()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var oldMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "FindById",
            QualifiedName = "Repo.FindById",
            FilePath = "Repo.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        oldMethod.Parameters.Add(new ParameterSymbol { Name = "id", DeclaredTypeName = "int" });

        var newMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "FindById",
            QualifiedName = "Repo.FindById",
            FilePath = "Repo.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        newMethod.Parameters.Add(new ParameterSymbol { Name = "id", DeclaredTypeName = "Guid" });

        oldGraph.Symbols.Add(oldMethod);
        newGraph.Symbols.Add(newMethod);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().BeGreaterThan(0);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
    }

    /// <summary>
    /// Verifica che la modifica del tipo di simbolo (es. da Class a Interface) venga identificata come Breaking Change.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenSymbolKindChanges()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var oldSymbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "DataHandler",
            QualifiedName = "Engine.DataHandler",
            FilePath = "DataHandler.cs",
            Kind = SymbolKind.Class,
            Accessibility = Accessibility.Public
        };

        var newSymbol = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "DataHandler",
            QualifiedName = "Engine.DataHandler",
            FilePath = "DataHandler.cs",
            Kind = SymbolKind.Interface,
            Accessibility = Accessibility.Public
        };

        oldGraph.Symbols.Add(oldSymbol);
        newGraph.Symbols.Add(newSymbol);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().BeGreaterThan(0);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
    }
}
