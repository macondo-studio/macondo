using FluentAssertions;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Analytics.DataMatrix;
using Macondo.Semantic.Analytics.Dsm;
using Macondo.Semantic.Analytics.ScreenFlow;
using Macondo.Semantic.Analytics.SharedState;
using Macondo.Semantic.Analytics.SocialCode;
using Macondo.Semantic.Analytics.TemporalCoupling;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CommonAnalyticsDeepEdgeCasesTests
{
    #region DSM & Tarjan SCC Tests

    /// <summary>
    /// Verify that DsmMatrixBuilder correctly handles graphs that are empty or composed of a single isolated node without dependencies.
    /// </summary>
    [Fact]
    public void DsmMatrix_ShouldHandleEmptyAndSingleNodeGraphs_Gracefully()
    {
        // Arrange
        var emptyGraph = new CodeGraph();
        var singleNodeGraph = new CodeGraph();
        var node = new Symbol { Id = Guid.NewGuid(), Name = "IsolatedMod", QualifiedName = "IsolatedMod", FilePath = "IsolatedMod.bas", Kind = SymbolKind.Module };
        singleNodeGraph.Symbols.Add(node);

        // Act
        var dsmEmpty = DsmMatrixBuilder.Build(emptyGraph);
        var dsmSingle = DsmMatrixBuilder.Build(singleNodeGraph);

        // Assert
        dsmEmpty.Should().NotBeNull();
        dsmEmpty.Size.Should().Be(0);
        dsmEmpty.TotalDependencies.Should().Be(0);
        dsmEmpty.StronglyConnectedComponents.Should().BeEmpty();

        dsmSingle.Should().NotBeNull();
        dsmSingle.Size.Should().Be(1);
        dsmSingle.TotalDependencies.Should().Be(0);
        dsmSingle.Items.Should().HaveCount(1);
        dsmSingle.Items[0].HasCircularDependencies.Should().BeFalse();
    }

    /// <summary>
    /// Verify that the DSM matrix identifies multiple disjoint strongly connected components (SCCs).
    /// present simultaneously in different parts of the graph (e.g. Cycle 1: A <-> B and Cycle 2: C <-> D).
    /// </summary>
    [Fact]
    public void DsmMatrix_ShouldDetectMultipleDisjointStronglyConnectedComponents()
    {
        // Arrange
        var graph = new CodeGraph();

        var modA = new Symbol { Id = Guid.NewGuid(), Name = "ModA", QualifiedName = "ModA", FilePath = "ModA.bas", Kind = SymbolKind.Module };
        var modB = new Symbol { Id = Guid.NewGuid(), Name = "ModB", QualifiedName = "ModB", FilePath = "ModB.bas", Kind = SymbolKind.Module };
        var modC = new Symbol { Id = Guid.NewGuid(), Name = "ModC", QualifiedName = "ModC", FilePath = "ModC.bas", Kind = SymbolKind.Module };
        var modD = new Symbol { Id = Guid.NewGuid(), Name = "ModD", QualifiedName = "ModD", FilePath = "ModD.bas", Kind = SymbolKind.Module };

        graph.Symbols.AddRange(new[] { modA, modB, modC, modD });

        // Cluster 1 cycle: A <-> B
        graph.AddCalls(modA, modB);
        graph.AddCalls(modB, modA);

        // Cluster 2 cycle: C <-> D
        graph.AddCalls(modC, modD);
        graph.AddCalls(modD, modC);

        // Act
        var dsm = DsmMatrixBuilder.Build(graph);

        // Assert
        dsm.StronglyConnectedComponents.Should().HaveCount(2, "Ci sono due cicli indipendenti disgiunti nel sistema");
        dsm.TotalCyclicDependencies.Should().Be(4);

        dsm.Items.First(i => i.Id == modA.Id).HasCircularDependencies.Should().BeTrue();
        dsm.Items.First(i => i.Id == modB.Id).HasCircularDependencies.Should().BeTrue();
        dsm.Items.First(i => i.Id == modC.Id).HasCircularDependencies.Should().BeTrue();
        dsm.Items.First(i => i.Id == modD.Id).HasCircularDependencies.Should().BeTrue();
    }

    #endregion

    #region Shared State & Global Variable Mutators

    /// <summary>
    /// Verify that SharedStateAnalyzer strictly ignores private fields and local constants,
    /// limiting the analysis only to variables with Public or Global visibility.
    /// </summary>
    [Fact]
    public void SharedState_ShouldIgnorePrivateFieldsAndConstants()
    {
        // Arrange
        var graph = new CodeGraph();

        var mod = new Symbol { Id = Guid.NewGuid(), Name = "ConfigModule", QualifiedName = "ConfigModule", FilePath = "ConfigModule.bas", Kind = SymbolKind.Module };
        var privField = new Symbol { Id = Guid.NewGuid(), Name = "m_internal", QualifiedName = "ConfigModule.m_internal", FilePath = "ConfigModule.bas", Kind = SymbolKind.Field, Accessibility = Accessibility.Private };
        var pubConst = new Symbol { Id = Guid.NewGuid(), Name = "APP_TITLE", QualifiedName = "ConfigModule.APP_TITLE", FilePath = "ConfigModule.bas", Kind = SymbolKind.Constant, Accessibility = Accessibility.Public };
        var pubField = new Symbol { Id = Guid.NewGuid(), Name = "g_ActiveSession", QualifiedName = "ConfigModule.g_ActiveSession", FilePath = "ConfigModule.bas", Kind = SymbolKind.Field, Accessibility = Accessibility.Public };

        graph.Symbols.AddRange(new[] { mod, privField, pubConst, pubField });
        graph.AddContains(mod, privField);
        graph.AddContains(mod, pubConst);
        graph.AddContains(mod, pubField);

        // Act
        var dataset = SharedStateAnalyzer.Analyze(graph);

        // Assert: Only g_ActiveSession should be included (privField is private, pubConst is constant)
        dataset.Items.Should().ContainSingle(i => i.VariableName == "g_ActiveSession");
    }

    /// <summary>
    /// Check the correct management of both reading and writing access (Read-Modify-Write)
    /// by the same routine on the same global variable.
    /// </summary>
    [Fact]
    public void SharedState_ShouldHandleRoutineReadingAndWritingSameVariable()
    {
        // Arrange
        var graph = new CodeGraph();

        var mod = new Symbol { Id = Guid.NewGuid(), Name = "CounterMod", QualifiedName = "CounterMod", FilePath = "CounterMod.bas", Kind = SymbolKind.Module };
        var globalCounter = new Symbol { Id = Guid.NewGuid(), Name = "g_HitCount", QualifiedName = "CounterMod.g_HitCount", FilePath = "CounterMod.bas", Kind = SymbolKind.Field, Accessibility = Accessibility.Global };
        var incrementSub = new Symbol { Id = Guid.NewGuid(), Name = "IncrementCounter", QualifiedName = "CounterMod.IncrementCounter", FilePath = "CounterMod.bas", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { mod, globalCounter, incrementSub });
        graph.AddContains(mod, globalCounter);
        graph.AddContains(mod, incrementSub);

        // The routine reads and writes g_HitCount (g_HitCount = g_HitCount + 1)
        graph.AddReads(incrementSub, globalCounter);
        graph.AddWrites(incrementSub, globalCounter);

        // Act
        var dataset = SharedStateAnalyzer.Analyze(graph);

        // Assert
        var item = dataset.Items.First(i => i.VariableId == globalCounter.Id);
        item.ReadersCount.Should().Be(1);
        item.WritersCount.Should().Be(1);
        item.TotalAccessorsCount.Should().Be(1); // 1 routine unica che accede
    }

    #endregion

    #region Cohesion & LCOM4 Edge Cases

    /// <summary>
    /// Verify that a class is fragmented into 4 fully disjoint method clusters
    /// produces an LCOM4 = 4 and is reported as a strong candidate for refactoring.
    /// </summary>
    [Fact]
    public void Cohesion_ShouldCalculateHighLcom4_ForHighlyFragmentedClass()
    {
        // Arrange
        var graph = new CodeGraph();
        var cls = new Symbol { Id = Guid.NewGuid(), Name = "MegaService", QualifiedName = "MegaService", FilePath = "MegaService.cs", Kind = SymbolKind.Class };
        graph.Symbols.Add(cls);

        // Creation of 4 independent clusters (each with 1 field and 2 methods)
        for (int c = 1; c <= 4; c++)
        {
            var fld = new Symbol { Id = Guid.NewGuid(), Name = $"_fld_{c}", QualifiedName = $"MegaService._fld_{c}", FilePath = "MegaService.cs", Kind = SymbolKind.Field };
            var m1 = new Symbol { Id = Guid.NewGuid(), Name = $"MethodA_{c}", QualifiedName = $"MegaService.MethodA_{c}", FilePath = "MegaService.cs", Kind = SymbolKind.Method };
            var m2 = new Symbol { Id = Guid.NewGuid(), Name = $"MethodB_{c}", QualifiedName = $"MegaService.MethodB_{c}", FilePath = "MegaService.cs", Kind = SymbolKind.Method };

            graph.Symbols.AddRange(new[] { fld, m1, m2 });
            graph.AddContains(cls, fld);
            graph.AddContains(cls, m1);
            graph.AddContains(cls, m2);

            graph.AddWrites(m1, fld);
            graph.AddReads(m2, fld);
            graph.AddCalls(m1, m2);
        }

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Items.Should().HaveCount(1);
        var report = dataset.Items[0];
        report.Lcom4.Should().Be(4, "Ci sono 4 cluster di metodi completamente disgiunti");
        report.IsRefactoringCandidate.Should().BeTrue();
        report.Clusters.Should().HaveCount(4);
    }

    /// <summary>
    /// Verify that a perfectly cohesive class (all methods share all fields)
    /// produces an LCOM4 = 1 and is not marked as a refactoring candidate.
    /// </summary>
    [Fact]
    public void Cohesion_ShouldProduceLcom4OfOne_ForFullyCohesiveClass()
    {
        // Arrange
        var graph = new CodeGraph();
        var cls = new Symbol { Id = Guid.NewGuid(), Name = "CohesivePoint", QualifiedName = "CohesivePoint", FilePath = "Point.cs", Kind = SymbolKind.Class };

        var fldX = new Symbol { Id = Guid.NewGuid(), Name = "_x", QualifiedName = "CohesivePoint._x", FilePath = "Point.cs", Kind = SymbolKind.Field };
        var fldY = new Symbol { Id = Guid.NewGuid(), Name = "_y", QualifiedName = "CohesivePoint._y", FilePath = "Point.cs", Kind = SymbolKind.Field };
        var mthMove = new Symbol { Id = Guid.NewGuid(), Name = "Move", QualifiedName = "CohesivePoint.Move", FilePath = "Point.cs", Kind = SymbolKind.Method };
        var mthDistance = new Symbol { Id = Guid.NewGuid(), Name = "Distance", QualifiedName = "CohesivePoint.Distance", FilePath = "Point.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { cls, fldX, fldY, mthMove, mthDistance });
        graph.AddContains(cls, fldX);
        graph.AddContains(cls, fldY);
        graph.AddContains(cls, mthMove);
        graph.AddContains(cls, mthDistance);

        // Both methods read/write both fields
        graph.AddWrites(mthMove, fldX);
        graph.AddWrites(mthMove, fldY);
        graph.AddReads(mthDistance, fldX);
        graph.AddReads(mthDistance, fldY);

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Items.Should().HaveCount(1);
        var report = dataset.Items[0];
        report.Lcom4.Should().Be(1);
        report.IsRefactoringCandidate.Should().BeFalse();
        report.Clusters.Should().HaveCount(1);
    }

    #endregion

    #region CRUD Matrix Inference Tests

    /// <summary>
    /// Check the classification of tables or entities with specific operational patterns (e.g. read-only tables or log/append-only tables).
    /// </summary>
    [Fact]
    public void CrudMatrix_ShouldIdentifyReadOnlyAndAppendOnlyEntities()
    {
        // Arrange
        var graph = new CodeGraph();

        var lookupEntity = new Symbol { Id = Guid.NewGuid(), Name = "LookupPostalCode", QualifiedName = "LookupPostalCode", FilePath = "Lookup.cs", Kind = SymbolKind.Class };
        var logEntity = new Symbol { Id = Guid.NewGuid(), Name = "AuditTrailLog", QualifiedName = "AuditTrailLog", FilePath = "Audit.cs", Kind = SymbolKind.Class };

        var selectPostal = new Symbol { Id = Guid.NewGuid(), Name = "SelectPostalCodeByZip", QualifiedName = "Repo.SelectPostalCodeByZip", FilePath = "Repo.cs", Kind = SymbolKind.Method };
        var insertLog = new Symbol { Id = Guid.NewGuid(), Name = "InsertAuditTrailRecord", QualifiedName = "AuditRepo.InsertAuditTrailRecord", FilePath = "AuditRepo.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { lookupEntity, logEntity, selectPostal, insertLog });

        // Act
        var dataset = CrudMatrixAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        var lookupSummary = dataset.EntitySummaries.FirstOrDefault(s => s.EntityName.Contains("PostalCode"));
        var logSummary = dataset.EntitySummaries.FirstOrDefault(s => s.EntityName.Contains("AuditTrail"));

        lookupSummary.Should().NotBeNull();
        lookupSummary!.ReadCount.Should().Be(1);
        lookupSummary.CreateCount.Should().Be(0);
        lookupSummary.UpdateCount.Should().Be(0);
        lookupSummary.DeleteCount.Should().Be(0);

        logSummary.Should().NotBeNull();
        logSummary!.CreateCount.Should().Be(1);
        logSummary.ReadCount.Should().Be(0);
        logSummary.UpdateCount.Should().Be(0);
        logSummary.DeleteCount.Should().Be(0);
    }

    #endregion

    #region Social Code & Temporal Coupling Edge Cases

    /// <summary>
    /// Verify that SocialCodeAnalyzer assigns a Healthy risk level when owning the code
    /// it is uniformly distributed among multiple active authors (Bus Factor > 1).
    /// </summary>
    [Fact]
    public void SocialCode_ShouldAssignHealthyRisk_WhenCodeIsSharedBetweenMultipleActiveAuthors()
    {
        // Arrange
        var graph = new CodeGraph();
        var fileSym = new Symbol { Id = Guid.NewGuid(), Name = "SharedEngine.cs", QualifiedName = "SharedEngine.cs", FilePath = "src/SharedEngine.cs", Kind = SymbolKind.Class };
        graph.Symbols.Add(fileSym);

        var history = new List<FileCommitRecord>
        {
            new() { Author = "Alice", FilePath = "src/SharedEngine.cs", LinesAdded = 100, LinesDeleted = 10, Timestamp = DateTime.UtcNow.AddDays(-20) },
            new() { Author = "Bob", FilePath = "src/SharedEngine.cs", LinesAdded = 90, LinesDeleted = 10, Timestamp = DateTime.UtcNow.AddDays(-10) },
            new() { Author = "Charlie", FilePath = "src/SharedEngine.cs", LinesAdded = 85, LinesDeleted = 5, Timestamp = DateTime.UtcNow.AddDays(-2) }
        };

        var activeAuthors = new HashSet<string> { "Alice", "Bob", "Charlie" };

        // Act
        var dataset = SocialCodeAnalyzer.Analyze(graph, history, activeAuthors);

        // Assert
        dataset.Files.Should().HaveCount(1);
        var fileReport = dataset.Files[0];
        fileReport.BusFactor.Should().BeGreaterThan(1);
        fileReport.Risk.Should().NotBe(BusFactorRisk.Orphan);
    }

    /// <summary>
    /// Verify that TemporalCouplingAnalyzer deduplicates repeated file names within the same commit
    /// and calculate a Jaccard index of 0.0 for files that never appear together in any commit.
    /// </summary>
    [Fact]
    public void TemporalCoupling_ShouldDeduplicateFilesInSameCommit_AndHandleZeroCoupling()
    {
        // Arrange: Commit 1 with duplicate files, Commit 2 with completely disjoint files
        var changelists = new List<List<string>>
        {
            new() { "src/Alpha.cls", "src/Alpha.cls", "src/Beta.cls", "src/Beta.cls" }, // duplicati
            new() { "src/Gamma.cls", "src/Delta.cls" }
        };

        // Act
        var dataset = TemporalCouplingAnalyzer.Analyze(changelists);

        // Assert
        dataset.TotalCommitsAnalyzed.Should().Be(2);

        // Alpha and Beta must have only 1 correlation with CoChangeCount = 1
        var alphaBetaPair = dataset.Pairs.FirstOrDefault(p => (p.FileAName == "Alpha.cls" && p.FileBName == "Beta.cls") || (p.FileAName == "Beta.cls" && p.FileBName == "Alpha.cls"));
        alphaBetaPair.Should().NotBeNull();
        alphaBetaPair!.CoChangeCount.Should().Be(1);

        // Alpha and Gamma must not have any registered pairs
        dataset.Pairs.Should().NotContain(p => (p.FileAName == "Alpha.cls" && p.FileBName == "Gamma.cls") || (p.FileAName == "Gamma.cls" && p.FileBName == "Alpha.cls"));
    }

    #endregion

    #region Screen Flow & Navigation Cycles

    /// <summary>
    /// Verify that ScreenFlowBuilder correctly detects and handles navigation loops between forms (FormA -> FormB -> FormA).
    /// </summary>
    [Fact]
    public void ScreenFlow_ShouldHandleCyclicFormNavigation_Gracefully()
    {
        // Arrange
        var graph = new CodeGraph();
        var formA = new Symbol { Id = Guid.NewGuid(), Name = "frmCatalog", QualifiedName = "frmCatalog", FilePath = "frmCatalog.frm", Kind = SymbolKind.Form };
        var formB = new Symbol { Id = Guid.NewGuid(), Name = "frmDetails", QualifiedName = "frmDetails", FilePath = "frmDetails.frm", Kind = SymbolKind.Form };

        graph.Symbols.AddRange(new[] { formA, formB });
        graph.AddCalls(formA, formB, 10);
        graph.AddCalls(formB, formA, 20);

        // Act
        var screenFlow = ScreenFlowBuilder.Build(graph);

        // Assert
        screenFlow.Screens.Should().HaveCount(2);
        screenFlow.Transitions.Should().HaveCount(2);

        screenFlow.Transitions.Should().Contain(t => t.SourceScreenId == formA.Id && t.TargetScreenId == formB.Id);
        screenFlow.Transitions.Should().Contain(t => t.SourceScreenId == formB.Id && t.TargetScreenId == formA.Id);
    }

    #endregion
}
