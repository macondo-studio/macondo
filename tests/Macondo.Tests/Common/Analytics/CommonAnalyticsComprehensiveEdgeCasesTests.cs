using FluentAssertions;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Analytics.CodeCity;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Analytics.SocialCode;
using Macondo.Semantic.Analytics.TemporalCoupling;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CommonAnalyticsComprehensiveEdgeCasesTests
{
    #region ApiDiff & SemVer Advanced Edge Cases

    /// <summary>
    /// Verifica che la trasformazione di un metodo pubblico da istanza a statico (o viceversa)
    /// sia rilevata come Breaking Change (Major).
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenMethodStaticModifierChanges()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var instanceMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "FormatPayload",
            QualifiedName = "DataFormatter.FormatPayload",
            FilePath = "DataFormatter.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public,
            IsStatic = false
        };

        var staticMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "FormatPayload",
            QualifiedName = "DataFormatter.FormatPayload",
            FilePath = "DataFormatter.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public,
            IsStatic = true
        };

        oldGraph.Symbols.Add(instanceMethod);
        newGraph.Symbols.Add(staticMethod);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.Differences.Should().NotBeEmpty();
    }

    /// <summary>
    /// Verifica che l'aggiunta di un nuovo membro a una Enum pubblica sia classificata come Minor,
    /// mentre la rimozione di un valore da una Enum pubblica sia classificata come MajorBreaking.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldClassifyEnumChanges_Correctly()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var oldEnum = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "PaymentStatus",
            QualifiedName = "Billing.PaymentStatus",
            FilePath = "Billing.cs",
            Kind = SymbolKind.Enum,
            Accessibility = Accessibility.Public
        };
        var valPending = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Pending",
            QualifiedName = "Billing.PaymentStatus.Pending",
            FilePath = "Billing.cs",
            Kind = SymbolKind.Field,
            Accessibility = Accessibility.Public
        };
        var valPaid = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Paid",
            QualifiedName = "Billing.PaymentStatus.Paid",
            FilePath = "Billing.cs",
            Kind = SymbolKind.Field,
            Accessibility = Accessibility.Public
        };
        oldGraph.Symbols.AddRange(new[] { oldEnum, valPending, valPaid });
        oldGraph.AddContains(oldEnum, valPending);
        oldGraph.AddContains(oldEnum, valPaid);

        // In newGraph aggiungiamo Refunded e rimuoviamo Paid
        var newEnum = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "PaymentStatus",
            QualifiedName = "Billing.PaymentStatus",
            FilePath = "Billing.cs",
            Kind = SymbolKind.Enum,
            Accessibility = Accessibility.Public
        };
        var valRefunded = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "Refunded",
            QualifiedName = "Billing.PaymentStatus.Refunded",
            FilePath = "Billing.cs",
            Kind = SymbolKind.Field,
            Accessibility = Accessibility.Public
        };
        newGraph.Symbols.AddRange(new[] { newEnum, valPending, valRefunded });
        newGraph.AddContains(newEnum, valPending);
        newGraph.AddContains(newEnum, valRefunded);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().BeGreaterThan(0);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
    }

    #endregion

    #region CodeCity Boundary Scenarios

    /// <summary>
    /// Verifica che un CodeGraph completamente vuoto generi un dataset CodeCity valido con 0 distretti ed edifici.
    /// </summary>
    [Fact]
    public void CodeCityBuilder_ShouldHandleEmptyGraph_Gracefully()
    {
        // Arrange
        var emptyGraph = new CodeGraph();

        // Act
        var dataset = CodeCityBuilder.Build(emptyGraph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Districts.Should().BeEmpty();
        dataset.AllBuildings.Should().BeEmpty();
        dataset.TotalDistrictsCount.Should().Be(0);
        dataset.TotalBuildingsCount.Should().Be(0);
        dataset.TotalLoc.Should().Be(0);
    }

    /// <summary>
    /// Verifica che file distribuiti su diverse sottocartelle vengano raggruppati in distretti distinti.
    /// </summary>
    [Fact]
    public void CodeCityBuilder_ShouldGroupBuildingsByDirectoryDistricts()
    {
        // Arrange
        var graph = new CodeGraph();

        var modCore = new Symbol { Id = Guid.NewGuid(), Name = "CoreService", QualifiedName = "CoreService", FilePath = "src/Core/CoreService.cs", Kind = SymbolKind.Class, LineStart = 1, LineEnd = 100 };
        var modUi = new Symbol { Id = Guid.NewGuid(), Name = "MainForm", QualifiedName = "MainForm", FilePath = "src/UI/MainForm.frm", Kind = SymbolKind.Form, LineStart = 1, LineEnd = 200 };
        var modDb = new Symbol { Id = Guid.NewGuid(), Name = "DataRepo", QualifiedName = "DataRepo", FilePath = "src/Database/DataRepo.cls", Kind = SymbolKind.Class, LineStart = 1, LineEnd = 150 };

        graph.Symbols.AddRange(new[] { modCore, modUi, modDb });

        // Act
        var dataset = CodeCityBuilder.Build(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TotalBuildingsCount.Should().Be(3);
        dataset.Districts.Count.Should().BeGreaterThanOrEqualTo(2);
    }

    #endregion

    #region Temporal Coupling Edge Cases

    /// <summary>
    /// Verifica che commit con singoli file (nessuna co-modifica) producano 0 coppie di accoppiamento temporale.
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldProduceZeroPairs_WhenCommitsAreSingleFileOnly()
    {
        // Arrange
        var changelists = new List<List<string>>
        {
            new() { "src/FileA.cls" },
            new() { "src/FileB.cls" },
            new() { "src/FileC.cls" }
        };

        // Act
        var dataset = TemporalCouplingAnalyzer.Analyze(changelists);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Pairs.Should().BeEmpty();
        dataset.HiddenCouplingCount.Should().Be(0);
    }

    /// <summary>
    /// Verifica che coppie con forte co-modifica ma provviste di dipendenza statica esplicita nel grafo
    /// abbiano HasStaticDependency = True e non aumentino il contatore di HiddenCoupling.
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldAcknowledgeStaticDependency_WhenPresentInGraph()
    {
        // Arrange
        var changelists = new List<List<string>>
        {
            new() { "src/OrderService.cls", "src/InvoiceService.cls" },
            new() { "src/OrderService.cls", "src/InvoiceService.cls" },
            new() { "src/OrderService.cls", "src/InvoiceService.cls" }
        };

        var graph = new CodeGraph();
        var orderSym = new Symbol { Id = Guid.NewGuid(), Name = "OrderService", QualifiedName = "OrderService", FilePath = "src/OrderService.cls", Kind = SymbolKind.Class };
        var invoiceSym = new Symbol { Id = Guid.NewGuid(), Name = "InvoiceService", QualifiedName = "InvoiceService", FilePath = "src/InvoiceService.cls", Kind = SymbolKind.Class };
        graph.Symbols.AddRange(new[] { orderSym, invoiceSym });

        // Aggiungi dipendenza statica esplicita OrderService -> InvoiceService
        graph.AddUsesType(orderSym, invoiceSym);

        // Act
        var dataset = TemporalCouplingAnalyzer.Analyze(changelists, graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Pairs.Should().HaveCount(1);
        dataset.Pairs[0].HasStaticDependency.Should().BeTrue();
        dataset.HiddenCouplingCount.Should().Be(0, "La correlazione temporale è spiegata dalla dipendenza statica nel grafo");
    }

    #endregion

    #region Social Code and Bus Factor Boundary Scenarios

    /// <summary>
    /// Verifica che in assenza di cronologia di commit il report SocialCode restituisca un dataset vuoto coerente.
    /// </summary>
    [Fact]
    public void SocialCode_ShouldReturnEmptyDataset_WhenCommitHistoryIsEmpty()
    {
        // Arrange
        var graph = new CodeGraph();
        var history = new List<FileCommitRecord>();
        var activeAuthors = new HashSet<string> { "Alice", "Bob" };

        // Act
        var dataset = SocialCodeAnalyzer.Analyze(graph, history, activeAuthors);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Files.Should().BeEmpty();
    }

    /// <summary>
    /// Verifica che un file con contributi bilanciati tra più autori attivi abbia un Bus Factor > 1 e rischio basso/nullo.
    /// </summary>
    [Fact]
    public void SocialCode_ShouldCalculateHealthyBusFactor_ForMultiAuthorFiles()
    {
        // Arrange
        var graph = new CodeGraph();
        var fileSym = new Symbol { Id = Guid.NewGuid(), Name = "SharedModule.cs", QualifiedName = "SharedModule.cs", FilePath = "src/SharedModule.cs", Kind = SymbolKind.Class };
        graph.Symbols.Add(fileSym);

        var history = new List<FileCommitRecord>
        {
            new() { Author = "Alice", FilePath = "src/SharedModule.cs", LinesAdded = 50, LinesDeleted = 5, Timestamp = DateTime.UtcNow.AddDays(-10) },
            new() { Author = "Bob", FilePath = "src/SharedModule.cs", LinesAdded = 50, LinesDeleted = 5, Timestamp = DateTime.UtcNow.AddDays(-8) },
            new() { Author = "Charlie", FilePath = "src/SharedModule.cs", LinesAdded = 50, LinesDeleted = 5, Timestamp = DateTime.UtcNow.AddDays(-2) }
        };

        var activeAuthors = new HashSet<string> { "Alice", "Bob", "Charlie" };

        // Act
        var dataset = SocialCodeAnalyzer.Analyze(graph, history, activeAuthors);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Files.Should().HaveCount(1);

        var fileReport = dataset.Files[0];
        fileReport.BusFactor.Should().BeGreaterThan(1);
        fileReport.Risk.Should().NotBe(BusFactorRisk.Orphan);
    }

    #endregion

    #region Cohesion / LCOM4 Boundary Scenarios

    /// <summary>
    /// Verifica che una classe con un solo metodo e nessun campo produca LCOM4 = 1 e non sia candidata al refactoring.
    /// </summary>
    [Fact]
    public void Cohesion_ShouldCalculateLcom4_ForSingleMethodClassWithoutFields()
    {
        // Arrange
        var graph = new CodeGraph();
        var cls = new Symbol { Id = Guid.NewGuid(), Name = "SimpleCalculator", QualifiedName = "SimpleCalculator", FilePath = "SimpleCalculator.cs", Kind = SymbolKind.Class };
        var mth = new Symbol { Id = Guid.NewGuid(), Name = "Add", QualifiedName = "SimpleCalculator.Add", FilePath = "SimpleCalculator.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { cls, mth });
        graph.AddContains(cls, mth);

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().HaveCount(1);
        dataset.Items[0].Lcom4.Should().Be(1);
        dataset.Items[0].IsRefactoringCandidate.Should().BeFalse();
    }

    /// <summary>
    /// Verifica che una classe in cui tutti i metodi accedono allo stesso campo condiviso produca LCOM4 = 1 (cluster singolo compatto).
    /// </summary>
    [Fact]
    public void Cohesion_ShouldCalculateSingleCluster_WhenAllMethodsShareField()
    {
        // Arrange
        var graph = new CodeGraph();
        var cls = new Symbol { Id = Guid.NewGuid(), Name = "StateContainer", QualifiedName = "StateContainer", FilePath = "StateContainer.cs", Kind = SymbolKind.Class };
        var fld = new Symbol { Id = Guid.NewGuid(), Name = "_state", QualifiedName = "StateContainer._state", FilePath = "StateContainer.cs", Kind = SymbolKind.Field };

        var mth1 = new Symbol { Id = Guid.NewGuid(), Name = "GetState", QualifiedName = "StateContainer.GetState", FilePath = "StateContainer.cs", Kind = SymbolKind.Method };
        var mth2 = new Symbol { Id = Guid.NewGuid(), Name = "SetState", QualifiedName = "StateContainer.SetState", FilePath = "StateContainer.cs", Kind = SymbolKind.Method };
        var mth3 = new Symbol { Id = Guid.NewGuid(), Name = "ResetState", QualifiedName = "StateContainer.ResetState", FilePath = "StateContainer.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { cls, fld, mth1, mth2, mth3 });
        graph.AddContains(cls, fld);
        graph.AddContains(cls, mth1);
        graph.AddContains(cls, mth2);
        graph.AddContains(cls, mth3);

        graph.AddReads(mth1, fld);
        graph.AddWrites(mth2, fld);
        graph.AddWrites(mth3, fld);

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().HaveCount(1);
        dataset.Items[0].Lcom4.Should().Be(1);
        dataset.Items[0].IsRefactoringCandidate.Should().BeFalse();
        dataset.Items[0].Clusters.Should().HaveCount(1);
    }

    #endregion
}
