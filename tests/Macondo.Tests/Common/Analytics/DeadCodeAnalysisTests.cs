using FluentAssertions;
using Macondo.Semantic.Analytics.DeadCode;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class DeadCodeAnalysisTests
{
    /// <summary>
    /// Verifica l'esecuzione dell'analisi del codice morto sul set completo delle fixture di test,
    /// confermando che il dataset contenga sia simboli raggiungibili che eventuali elementi non referenziati.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldExecuteOnFixtures_AndIdentifyReachableAndDeadSymbols()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TotalSymbolsAnalyzed.Should().BeGreaterThan(0);
        dataset.ReachableSymbolsCount.Should().BeGreaterThan(0);
        dataset.DeadCodeRatio.Should().BeInRange(0.0, 100.0);
    }

    /// <summary>
    /// Verifica che gli Entry Point naturali (Sub Main, eventi Form_Load, handler di controlli UI)
    /// non vengano contrassegnati come codice morto anche se nessun altro metodo del codice li chiama esplicitamente.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldPreserveNaturalEntryPoints_SuchAsSubMainAndFormEvents()
    {
        // Arrange
        var graph = new CodeGraph();

        var startupModule = new Symbol { Id = Guid.NewGuid(), Name = "StartupMod", QualifiedName = "StartupMod", FilePath = "StartupMod.bas", Kind = SymbolKind.Module };
        var subMain = new Symbol { Id = Guid.NewGuid(), Name = "Main", QualifiedName = "StartupMod.Main", FilePath = "StartupMod.bas", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { startupModule, subMain });
        graph.AddContains(startupModule, subMain);

        var form = new Symbol { Id = Guid.NewGuid(), Name = "frmMain", QualifiedName = "frmMain", FilePath = "frmMain.frm", Kind = SymbolKind.Form };
        var formLoad = new Symbol { Id = Guid.NewGuid(), Name = "Form_Load", QualifiedName = "frmMain.Form_Load", FilePath = "frmMain.frm", Kind = SymbolKind.Method };
        var btnClick = new Symbol { Id = Guid.NewGuid(), Name = "cmdSave_Click", QualifiedName = "frmMain.cmdSave_Click", FilePath = "frmMain.frm", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { form, formLoad, btnClick });
        graph.AddContains(form, formLoad);
        graph.AddContains(form, btnClick);

        // Sub Main invoca un worker
        var workerMethod = new Symbol { Id = Guid.NewGuid(), Name = "DoWork", QualifiedName = "StartupMod.DoWork", FilePath = "StartupMod.bas", Kind = SymbolKind.Method };
        graph.Symbols.Add(workerMethod);
        graph.AddContains(startupModule, workerMethod);
        graph.AddCalls(subMain, workerMethod);

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        dataset.Items.Should().NotContain(i => i.Id == subMain.Id);
        dataset.Items.Should().NotContain(i => i.Id == formLoad.Id);
        dataset.Items.Should().NotContain(i => i.Id == btnClick.Id);
        dataset.Items.Should().NotContain(i => i.Id == workerMethod.Id);
    }

    /// <summary>
    /// Verifica che routine private mai invocate da nessun flusso di ingresso
    /// vengano correttamente segnalate nella categoria UnreachableRoutine.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldIdentifyUnreachablePrivateRoutines()
    {
        // Arrange
        var graph = new CodeGraph();

        var mod = new Symbol { Id = Guid.NewGuid(), Name = "BusinessMod", QualifiedName = "BusinessMod", FilePath = "BusinessMod.bas", Kind = SymbolKind.Module };
        var subMain = new Symbol { Id = Guid.NewGuid(), Name = "Main", QualifiedName = "BusinessMod.Main", FilePath = "BusinessMod.bas", Kind = SymbolKind.Method };
        var deadRoutine = new Symbol { Id = Guid.NewGuid(), Name = "UnusedPrivateHelper", QualifiedName = "BusinessMod.UnusedPrivateHelper", FilePath = "BusinessMod.bas", Kind = SymbolKind.Method, Accessibility = Accessibility.Private, LineStart = 20, LineEnd = 35 };

        graph.Symbols.AddRange(new[] { mod, subMain, deadRoutine });
        graph.AddContains(mod, subMain);
        graph.AddContains(mod, deadRoutine);

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        var deadItem = dataset.Items.FirstOrDefault(i => i.Id == deadRoutine.Id);
        deadItem.Should().NotBeNull();
        deadItem!.Category.Should().Be(DeadCodeCategory.UnreachableRoutine);
        deadItem.EstimatedLoc.Should().Be(16);
        deadItem.IsPublic.Should().BeFalse();
    }

    /// <summary>
    /// Verifica che campi, variabili globali o costanti mai referenziate
    /// vengano categorizzate come UnusedFieldOrConstant.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldIdentifyUnusedFieldsAndConstants()
    {
        // Arrange
        var graph = new CodeGraph();

        var mod = new Symbol { Id = Guid.NewGuid(), Name = "DataMod", QualifiedName = "DataMod", FilePath = "DataMod.bas", Kind = SymbolKind.Module };
        var subMain = new Symbol { Id = Guid.NewGuid(), Name = "Main", QualifiedName = "DataMod.Main", FilePath = "DataMod.bas", Kind = SymbolKind.Method };
        var unusedField = new Symbol { Id = Guid.NewGuid(), Name = "m_unusedCounter", QualifiedName = "DataMod.m_unusedCounter", FilePath = "DataMod.bas", Kind = SymbolKind.Field, Accessibility = Accessibility.Private };
        var unusedConst = new Symbol { Id = Guid.NewGuid(), Name = "MAX_BUFFER_LEGACY", QualifiedName = "DataMod.MAX_BUFFER_LEGACY", FilePath = "DataMod.bas", Kind = SymbolKind.Constant, Accessibility = Accessibility.Private };

        graph.Symbols.AddRange(new[] { mod, subMain, unusedField, unusedConst });
        graph.AddContains(mod, subMain);
        graph.AddContains(mod, unusedField);
        graph.AddContains(mod, unusedConst);

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        dataset.Items.Should().Contain(i => i.Id == unusedField.Id && i.Category == DeadCodeCategory.UnusedFieldOrConstant);
        dataset.Items.Should().Contain(i => i.Id == unusedConst.Id && i.Category == DeadCodeCategory.UnusedFieldOrConstant);
    }

    /// <summary>
    /// Verifica che classi o moduli isolati senza istanziazioni o chiamate
    /// vengano individuati come OrphanContainer.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldIdentifyOrphanContainers()
    {
        // Arrange
        var graph = new CodeGraph();

        var mainMod = new Symbol { Id = Guid.NewGuid(), Name = "MainMod", QualifiedName = "MainMod", FilePath = "MainMod.bas", Kind = SymbolKind.Module };
        var subMain = new Symbol { Id = Guid.NewGuid(), Name = "Main", QualifiedName = "MainMod.Main", FilePath = "MainMod.bas", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { mainMod, subMain });
        graph.AddContains(mainMod, subMain);

        var orphanClass = new Symbol { Id = Guid.NewGuid(), Name = "OldLegacyClass", QualifiedName = "OldLegacyClass", FilePath = "OldLegacyClass.cls", Kind = SymbolKind.Class, LineStart = 1, LineEnd = 100 };
        var orphanMethod = new Symbol { Id = Guid.NewGuid(), Name = "OldProcess", QualifiedName = "OldLegacyClass.OldProcess", FilePath = "OldLegacyClass.cls", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { orphanClass, orphanMethod });
        graph.AddContains(orphanClass, orphanMethod);

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        // Assert
        dataset.Items.Should().Contain(i => i.Id == orphanClass.Id && i.Category == DeadCodeCategory.OrphanContainer);
    }

    /// <summary>
    /// Verifica che in assenza di Entry Point espliciti (es. progetto di tipo DLL / Libreria pura),
    /// l'algoritmo consideri come radici di raggiungibilità tutti i simboli con accessibilità Public o Interface.
    /// </summary>
    [Fact]
    public void DeadCodeAnalysis_ShouldFallbackToPublicSymbols_WhenNoExplicitEntryPointExists()
    {
        // Arrange: Libreria pura senza Main né Form
        var graph = new CodeGraph();

        var pubClass = new Symbol { Id = Guid.NewGuid(), Name = "PublicApi", QualifiedName = "PublicApi", FilePath = "PublicApi.cls", Kind = SymbolKind.Class, Accessibility = Accessibility.Public };
        var pubMethod = new Symbol { Id = Guid.NewGuid(), Name = "Calculate", QualifiedName = "PublicApi.Calculate", FilePath = "PublicApi.cls", Kind = SymbolKind.Method, Accessibility = Accessibility.Public };
        var privInternal = new Symbol { Id = Guid.NewGuid(), Name = "InternalHelper", QualifiedName = "PublicApi.InternalHelper", FilePath = "PublicApi.cls", Kind = SymbolKind.Method, Accessibility = Accessibility.Private };

        graph.Symbols.AddRange(new[] { pubClass, pubMethod, privInternal });
        graph.AddContains(pubClass, pubMethod);
        graph.AddContains(pubClass, privInternal);
        graph.AddCalls(pubMethod, privInternal);

        // Act
        var dataset = DeadCodeAnalyzer.Analyze(graph);

        // Assert: pubClass, pubMethod e privInternal (chiamato da pubMethod) devono essere raggiungibili
        dataset.Items.Should().NotContain(i => i.Id == pubClass.Id);
        dataset.Items.Should().NotContain(i => i.Id == pubMethod.Id);
        dataset.Items.Should().NotContain(i => i.Id == privInternal.Id);
    }
}
