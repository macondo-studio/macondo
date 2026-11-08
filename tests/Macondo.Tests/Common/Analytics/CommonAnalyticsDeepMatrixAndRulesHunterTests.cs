using FluentAssertions;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Analytics.ControlFlow;
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.Maintainability;
using Macondo.Semantic.Analytics.SocialCode;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CommonAnalyticsDeepMatrixAndRulesHunterTests
{
    #region ArchitecturalRules: Multiple Layer Forbidden Dependencies & Whitelists

    /// <summary>
    /// Verifica la rilevazione di violazioni architetturali complesse con regole multiple e regole con selettori specifici.
    /// </summary>
    [Fact]
    public void ArchitecturalRules_ShouldDetectViolations_AcrossMultiTierSystem()
    {
        // Arrange
        var graph = new CodeGraph();

        var presentationCls = new Symbol { Id = Guid.NewGuid(), Name = "BillingForm", QualifiedName = "Presentation.BillingForm", FilePath = "Presentation/BillingForm.frm", Kind = SymbolKind.Form };
        var presentationMth = new Symbol { Id = Guid.NewGuid(), Name = "btnSubmit_Click", QualifiedName = "Presentation.BillingForm.btnSubmit_Click", FilePath = "Presentation/BillingForm.frm", Kind = SymbolKind.Method };

        var infrastructureCls = new Symbol { Id = Guid.NewGuid(), Name = "SqlDriver", QualifiedName = "Infrastructure.SqlDriver", FilePath = "Infrastructure/SqlDriver.bas", Kind = SymbolKind.Module };
        var infrastructureMth = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteInsert", QualifiedName = "Infrastructure.SqlDriver.ExecuteInsert", FilePath = "Infrastructure/SqlDriver.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { presentationCls, presentationMth, infrastructureCls, infrastructureMth });
        graph.AddContains(presentationCls, presentationMth);
        graph.AddContains(infrastructureCls, infrastructureMth);

        // Forbidden call directly bypassing service layer
        graph.AddCalls(presentationMth, infrastructureMth);

        var rule = new ArchitecturalRule
        {
            Name = "Strict Layering: Presentation to Infrastructure Forbidden",
            Type = RuleType.ForbiddenDependency,
            Severity = RuleSeverity.Error,
            SourceSelector = new ComponentSelector { Name = "Presentation Layer", Pattern = "*Presentation*" },
            TargetSelector = new ComponentSelector { Name = "Infrastructure Layer", Pattern = "*Infrastructure*" }
        };

        // Act
        var report = ArchitecturalRulesEngine.Evaluate(graph, new[] { rule });

        // Assert
        report.Should().NotBeNull();
        report.TotalViolations.Should().Be(1);
        report.Violations[0].Rule.Name.Should().Be("Strict Layering: Presentation to Infrastructure Forbidden");
        report.Violations[0].SourceSymbol.Name.Should().Be("btnSubmit_Click");
        report.Violations[0].TargetSymbol.Name.Should().Be("ExecuteInsert");
    }

    #endregion

    #region ApiDiff: Method Parameter Modifications and Return Type Alterations

    /// <summary>
    /// Verifica che la rimozione o modifica dei parametri di un metodo pubblico venga rilevata come Breaking Change (Major).
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenPublicMethodSignatureChanges()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var oldMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "CalculateDiscount",
            QualifiedName = "PricingService.CalculateDiscount",
            FilePath = "PricingService.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        oldMethod.Parameters.Add(new ParameterSymbol { Name = "customerType", DeclaredTypeName = "String" });
        oldMethod.Parameters.Add(new ParameterSymbol { Name = "orderTotal", DeclaredTypeName = "Double" });
        oldGraph.Symbols.Add(oldMethod);

        // In the new version, a required parameter is removed or altered incompatibly
        var newMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "CalculateDiscount",
            QualifiedName = "PricingService.CalculateDiscount",
            FilePath = "PricingService.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        newMethod.Parameters.Add(new ParameterSymbol { Name = "customerGuid", DeclaredTypeName = "Guid" });
        newGraph.Symbols.Add(newMethod);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().BeGreaterThan(0);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
    }

    #endregion

    #region Cohesion Calculator: Multi-Cluster LCOM4 and Edge Cases

    /// <summary>
    /// Verifica che una classe con 3 cluster completamente isolati produca LCOM4 = 3 e sia candidata al refactoring.
    /// </summary>
    [Fact]
    public void CohesionCalculator_ShouldDetectThreeDistinctClusters_Lcom4Three()
    {
        // Arrange
        var graph = new CodeGraph();
        var cls = new Symbol { Id = Guid.NewGuid(), Name = "MegaUtilityService", QualifiedName = "MegaUtilityService", FilePath = "MegaUtilityService.cs", Kind = SymbolKind.Class };

        // Cluster 1 (Math)
        var fldMath = new Symbol { Id = Guid.NewGuid(), Name = "_precision", QualifiedName = "MegaUtilityService._precision", FilePath = "MegaUtilityService.cs", Kind = SymbolKind.Field };
        var mthMath = new Symbol { Id = Guid.NewGuid(), Name = "Compute", QualifiedName = "MegaUtilityService.Compute", FilePath = "MegaUtilityService.cs", Kind = SymbolKind.Method };

        // Cluster 2 (Text)
        var fldText = new Symbol { Id = Guid.NewGuid(), Name = "_encoding", QualifiedName = "MegaUtilityService._encoding", FilePath = "MegaUtilityService.cs", Kind = SymbolKind.Field };
        var mthText = new Symbol { Id = Guid.NewGuid(), Name = "Format", QualifiedName = "MegaUtilityService.Format", FilePath = "MegaUtilityService.cs", Kind = SymbolKind.Method };

        // Cluster 3 (Network)
        var fldNet = new Symbol { Id = Guid.NewGuid(), Name = "_timeout", QualifiedName = "MegaUtilityService._timeout", FilePath = "MegaUtilityService.cs", Kind = SymbolKind.Field };
        var mthNet = new Symbol { Id = Guid.NewGuid(), Name = "Ping", QualifiedName = "MegaUtilityService.Ping", FilePath = "MegaUtilityService.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { cls, fldMath, mthMath, fldText, mthText, fldNet, mthNet });
        graph.AddContains(cls, fldMath);
        graph.AddContains(cls, mthMath);
        graph.AddContains(cls, fldText);
        graph.AddContains(cls, mthText);
        graph.AddContains(cls, fldNet);
        graph.AddContains(cls, mthNet);

        graph.AddReads(mthMath, fldMath);
        graph.AddReads(mthText, fldText);
        graph.AddReads(mthNet, fldNet);

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Items.Should().HaveCount(1);
        dataset.Items[0].Lcom4.Should().Be(3);
        dataset.Items[0].IsRefactoringCandidate.Should().BeTrue();
    }

    /// <summary>
    /// Verifica che classi senza metodi o con un solo metodo vengano gestite senza divisioni per zero o eccezioni.
    /// </summary>
    [Fact]
    public void CohesionCalculator_ShouldHandleSingleMethodOrEmptyClassGracefully()
    {
        // Arrange
        var graph = new CodeGraph();
        var clsEmpty = new Symbol { Id = Guid.NewGuid(), Name = "EmptyClass", QualifiedName = "EmptyClass", FilePath = "Empty.cs", Kind = SymbolKind.Class };
        var clsSingle = new Symbol { Id = Guid.NewGuid(), Name = "SingleMethodClass", QualifiedName = "SingleMethodClass", FilePath = "Single.cs", Kind = SymbolKind.Class };
        var mth = new Symbol { Id = Guid.NewGuid(), Name = "Execute", QualifiedName = "SingleMethodClass.Execute", FilePath = "Single.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { clsEmpty, clsSingle, mth });
        graph.AddContains(clsSingle, mth);

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.AverageLcom4.Should().BeGreaterOrEqualTo(0.0);
    }

    #endregion

    #region SocialCode & BusFactor: All Authors Inactive and Empty History

    /// <summary>
    /// Verifica che quando tutti gli autori del repository non sono più attivi,
    /// tutti i file vengano contrassegnati come a rischio orfano (Orphan).
    /// </summary>
    [Fact]
    public void SocialCode_ShouldFlagAllFilesAsOrphan_WhenNoAuthorsAreActive()
    {
        // Arrange
        var graph = new CodeGraph();
        var fileA = new Symbol { Id = Guid.NewGuid(), Name = "ModuleA.bas", QualifiedName = "ModuleA.bas", FilePath = "src/ModuleA.bas", Kind = SymbolKind.Module };
        var fileB = new Symbol { Id = Guid.NewGuid(), Name = "ModuleB.bas", QualifiedName = "ModuleB.bas", FilePath = "src/ModuleB.bas", Kind = SymbolKind.Module };
        graph.Symbols.AddRange(new[] { fileA, fileB });

        var history = new List<FileCommitRecord>
        {
            new() { Author = "OldDev1", FilePath = "src/ModuleA.bas", LinesAdded = 100, LinesDeleted = 0, Timestamp = DateTime.UtcNow.AddYears(-2) },
            new() { Author = "OldDev2", FilePath = "src/ModuleB.bas", LinesAdded = 200, LinesDeleted = 0, Timestamp = DateTime.UtcNow.AddYears(-2) }
        };

        var activeAuthors = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NewDevWhoNeverCommitted" };

        // Act
        var dataset = SocialCodeAnalyzer.Analyze(graph, history, activeAuthors);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Files.Should().HaveCount(2);
        dataset.Files.Should().OnlyContain(f => f.Risk == BusFactorRisk.Orphan);
    }

    /// <summary>
    /// Verifica che una cronologia vuota di commit generi un dataset privo di errori.
    /// </summary>
    [Fact]
    public void SocialCode_ShouldHandleEmptyCommitHistory_WithoutThrowing()
    {
        // Arrange
        var graph = new CodeGraph();
        var file = new Symbol { Id = Guid.NewGuid(), Name = "File.cs", QualifiedName = "File.cs", FilePath = "File.cs", Kind = SymbolKind.Class };
        graph.Symbols.Add(file);

        var history = new List<FileCommitRecord>();
        var activeAuthors = new HashSet<string> { "Alice" };

        // Act
        var dataset = SocialCodeAnalyzer.Analyze(graph, history, activeAuthors);

        // Assert
        dataset.Should().NotBeNull();
        dataset.Files.Should().BeEmpty();
    }

    #endregion

    #region DeadCode: Deep Transitive Orphan Trees

    /// <summary>
    /// Verifica che catene di chiamate private orfane profonde (m1 -> m2 -> m3 -> m4)
    /// vengano identificate tutte integralmente come Dead Code.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldDetectDeepTransitiveDeadChains()
    {
        // Arrange
        var graph = new CodeGraph();
        var mod = new Symbol { Id = Guid.NewGuid(), Name = "UtilityMod", QualifiedName = "UtilityMod", FilePath = "UtilityMod.bas", Kind = SymbolKind.Module };

        var m1 = new Symbol { Id = Guid.NewGuid(), Name = "DeadRoot", QualifiedName = "UtilityMod.DeadRoot", FilePath = "UtilityMod.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Private, LineStart = 10, LineEnd = 20 };
        var m2 = new Symbol { Id = Guid.NewGuid(), Name = "DeadChild1", QualifiedName = "UtilityMod.DeadChild1", FilePath = "UtilityMod.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Private, LineStart = 25, LineEnd = 35 };
        var m3 = new Symbol { Id = Guid.NewGuid(), Name = "DeadChild2", QualifiedName = "UtilityMod.DeadChild2", FilePath = "UtilityMod.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Private, LineStart = 40, LineEnd = 50 };
        var m4 = new Symbol { Id = Guid.NewGuid(), Name = "DeadLeaf", QualifiedName = "UtilityMod.DeadLeaf", FilePath = "UtilityMod.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Private, LineStart = 55, LineEnd = 65 };

        var activePublic = new Symbol { Id = Guid.NewGuid(), Name = "ActivePublicApi", QualifiedName = "UtilityMod.ActivePublicApi", FilePath = "UtilityMod.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Public, LineStart = 70, LineEnd = 80 };

        graph.Symbols.AddRange(new[] { mod, m1, m2, m3, m4, activePublic });
        graph.AddContains(mod, m1);
        graph.AddContains(mod, m2);
        graph.AddContains(mod, m3);
        graph.AddContains(mod, m4);
        graph.AddContains(mod, activePublic);

        // Chain of calls in dead island
        graph.AddCalls(m1, m2);
        graph.AddCalls(m2, m3);
        graph.AddCalls(m3, m4);

        // Act
        var result = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        result.Should().NotBeNull();
        result.Items.Should().Contain(i => i.Id == m1.Id);
        result.Items.Should().Contain(i => i.Id == m2.Id);
        result.Items.Should().Contain(i => i.Id == m3.Id);
        result.Items.Should().Contain(i => i.Id == m4.Id);
        result.Items.Should().NotContain(i => i.Id == activePublic.Id);
    }

    #endregion
}
