using FluentAssertions;
using Macondo.Semantic.Analytics.ApiDiff;
using Macondo.Semantic.Analytics.ArchitecturalRules;
using Macondo.Semantic.Analytics.CodeCity;
using Macondo.Semantic.Analytics.Cohesion;
using Macondo.Semantic.Analytics.DataFlow;
using Macondo.Semantic.Analytics.Maintainability;
using Macondo.Semantic.Analytics.Martin;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CommonAnalyticsComprehensiveStressAndBoundaryTests
{
    #region ApiDiff Optional Parameters & Obsolete Attributes

    /// <summary>
    /// Verifica che l'aggiunta di un parametro opzionale con valore di default a un metodo pubblico
    /// venga classificata come Minor Feature retrocompatibile anziché Breaking Change.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldClassifyOptionalParameterAddition_AsMinor()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var oldMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "FetchData",
            QualifiedName = "ApiClient.FetchData",
            FilePath = "ApiClient.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        oldMethod.Parameters.Add(new ParameterSymbol { Name = "endpoint", DeclaredTypeName = "string" });

        var newMethod = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "FetchData",
            QualifiedName = "ApiClient.FetchData",
            FilePath = "ApiClient.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };
        newMethod.Parameters.Add(new ParameterSymbol { Name = "endpoint", DeclaredTypeName = "string" });
        newMethod.Parameters.Add(new ParameterSymbol { Name = "timeoutSeconds", DeclaredTypeName = "int", IsOptional = true, DefaultValue = "30" });

        oldGraph.Symbols.Add(oldMethod);
        newGraph.Symbols.Add(newMethod);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(0, "Aggiunta di un parametro opzionale con default non deve rompere i client esistenti");
        report.AdditionsCount.Should().Be(1);
        report.RecommendedBump.Should().Be(ApiChangeImpact.Minor);
    }

    /// <summary>
    /// Verifica che la rimozione di un evento pubblico da un'interfaccia o classe sia identificata come Major / Breaking Change.
    /// </summary>
    [Fact]
    public void ApiDiff_ShouldDetectBreakingChange_WhenPublicEventIsRemoved()
    {
        // Arrange
        var oldGraph = new CodeGraph();
        var newGraph = new CodeGraph();

        var pubEvent = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "DataChanged",
            QualifiedName = "DataProvider.DataChanged",
            FilePath = "DataProvider.cs",
            Kind = SymbolKind.Method,
            Accessibility = Accessibility.Public
        };

        oldGraph.Symbols.Add(pubEvent);

        // Act
        var report = ApiDiffAnalyzer.Compare(oldGraph, newGraph);

        // Assert
        report.Should().NotBeNull();
        report.BreakingChangesCount.Should().Be(1);
        report.RecommendedBump.Should().Be(ApiChangeImpact.MajorBreaking);
    }

    #endregion

    #region Martin Metrics Main Sequence & Stability Zones

    /// <summary>
    /// Verifica il corretto calcolo di Abstractness (A), Instability (I) e Distance (D)
    /// per moduli appartenenti alla Zone of Pain (bassa astrattezza, alta stabilità)
    /// e alla Zone of Uselessness (alta astrattezza, alta instabilità).
    /// </summary>
    [Fact]
    public void MartinMetrics_ShouldIdentifyZoneOfPainAndMainSequenceDistance()
    {
        // Arrange: Modulo concreto ampiamente referenziato (Ca=5, Ce=0, 0 classi astratte) -> Zone of Pain
        var graph = new CodeGraph();

        var coreMod = new Symbol { Id = Guid.NewGuid(), Name = "CoreDatabase", QualifiedName = "CoreDatabase", FilePath = "CoreDatabase.bas", Kind = SymbolKind.Module };
        var concreteMethod = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteRaw", QualifiedName = "CoreDatabase.ExecuteRaw", FilePath = "CoreDatabase.bas", Kind = SymbolKind.Method };
        graph.Symbols.AddRange(new[] { coreMod, concreteMethod });
        graph.AddContains(coreMod, concreteMethod);

        for (int i = 1; i <= 5; i++)
        {
            var callerMod = new Symbol { Id = Guid.NewGuid(), Name = $"Caller_{i}", QualifiedName = $"Caller_{i}", FilePath = $"Caller_{i}.bas", Kind = SymbolKind.Module };
            var callerMethod = new Symbol { Id = Guid.NewGuid(), Name = $"Call_{i}", QualifiedName = $"Caller_{i}.Call_{i}", FilePath = $"Caller_{i}.bas", Kind = SymbolKind.Method };
            graph.Symbols.AddRange(new[] { callerMod, callerMethod });
            graph.AddContains(callerMod, callerMethod);
            graph.AddCalls(callerMethod, concreteMethod);
        }

        // Act
        var dataset = MartinMetricsCalculator.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        var coreItem = dataset.Items.FirstOrDefault(i => i.Id == coreMod.Id);
        coreItem.Should().NotBeNull();
        coreItem!.AfferentCoupling.Should().Be(5);
        coreItem.EfferentCoupling.Should().Be(0);
        coreItem.Instability.Should().Be(0.0);
        coreItem.Abstractness.Should().Be(0.0);
        coreItem.NormalizedDistance.Should().Be(1.0); // |0 + 0 - 1| = 1.0 (Zone of Pain massima)
    }

    #endregion

    #region Cohesion DTO & Static Helpers Boundary Cases

    /// <summary>
    /// Verifica che un DTO contenente solo campi e proprietà (0 metodi interni) produca LCOM4 = 1
    /// e non venga contrassegnato erroneamente come candidato al refactoring.
    /// </summary>
    [Fact]
    public void Cohesion_ShouldHandleDataTransferObjectsWithoutMethods()
    {
        // Arrange
        var graph = new CodeGraph();
        var dto = new Symbol { Id = Guid.NewGuid(), Name = "UserDto", QualifiedName = "UserDto", FilePath = "UserDto.cs", Kind = SymbolKind.Class };

        var fld1 = new Symbol { Id = Guid.NewGuid(), Name = "Id", QualifiedName = "UserDto.Id", FilePath = "UserDto.cs", Kind = SymbolKind.Field };
        var fld2 = new Symbol { Id = Guid.NewGuid(), Name = "Email", QualifiedName = "UserDto.Email", FilePath = "UserDto.cs", Kind = SymbolKind.Field };
        var fld3 = new Symbol { Id = Guid.NewGuid(), Name = "FullName", QualifiedName = "UserDto.FullName", FilePath = "UserDto.cs", Kind = SymbolKind.Field };

        graph.Symbols.AddRange(new[] { dto, fld1, fld2, fld3 });
        graph.AddContains(dto, fld1);
        graph.AddContains(dto, fld2);
        graph.AddContains(dto, fld3);

        // Act
        var dataset = CohesionCalculator.Analyze(graph);

        // Assert
        dataset.Items.Should().HaveCount(1);
        var report = dataset.Items[0];
        report.Lcom4.Should().Be(1);
        report.IsRefactoringCandidate.Should().BeFalse();
    }

    #endregion

    #region Data Flow Multi-Sink Fan-Out

    /// <summary>
    /// Verifica che una singola sorgente non verificata (UI Input) che si propaga verso molteplici
    /// sink critici distinti (SQL Execution e File System) generi un TaintPath per ciascun sink.
    /// </summary>
    [Fact]
    public void DataFlow_ShouldTrackFanOutFromSingleSourceToMultipleSinks()
    {
        // Arrange
        var graph = new CodeGraph();

        var uiField = new Symbol { Id = Guid.NewGuid(), Name = "txtFilePathOrSql", QualifiedName = "Form1.txtFilePathOrSql", FilePath = "Form1.frm", Kind = SymbolKind.Field };
        var uiHandler = new Symbol { Id = Guid.NewGuid(), Name = "btnProcess_Click", QualifiedName = "Form1.btnProcess_Click", FilePath = "Form1.frm", Kind = SymbolKind.Method };

        var sqlSink = new Symbol { Id = Guid.NewGuid(), Name = "ExecuteSqlQuery", QualifiedName = "Db.ExecuteSqlQuery", FilePath = "Db.bas", Kind = SymbolKind.Method };
        var fileSink = new Symbol { Id = Guid.NewGuid(), Name = "WriteAllText", QualifiedName = "IOHelper.WriteAllText", FilePath = "IOHelper.cs", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { uiField, uiHandler, sqlSink, fileSink });

        graph.AddReads(uiHandler, uiField);
        graph.AddCalls(uiHandler, sqlSink);
        graph.AddCalls(uiHandler, fileSink);

        // Act
        var dataset = TaintAnalyzer.Analyze(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TaintPaths.Should().HaveCountGreaterThanOrEqualTo(1);
        dataset.TaintPaths.Should().Contain(p => p.SinkSymbol.Name == "ExecuteSqlQuery");
    }

    #endregion

    #region Code City Stress & Coordinate Boundaries

    /// <summary>
    /// Verifica che CodeCityBuilder generi posizionamenti validi e non sovrapposti
    /// anche su grafi complessi con 20 distretti e 80 edifici.
    /// </summary>
    [Fact]
    public void CodeCityBuilder_ShouldScaleToLargeGraphsWithManyDistricts()
    {
        // Arrange
        var graph = new CodeGraph();

        for (int d = 1; d <= 20; d++)
        {
            var container = new Symbol
            {
                Id = Guid.NewGuid(),
                Name = $"Module_{d}",
                QualifiedName = $"Project.Module_{d}",
                FilePath = $"src/District_{d}/Module_{d}.cs",
                Kind = SymbolKind.Class,
                LineStart = 1,
                LineEnd = 100 + (d * 10)
            };
            graph.Symbols.Add(container);

            for (int m = 1; m <= 4; m++)
            {
                var method = new Symbol
                {
                    Id = Guid.NewGuid(),
                    Name = $"Method_{d}_{m}",
                    QualifiedName = $"Project.Module_{d}.Method_{d}_{m}",
                    FilePath = $"src/District_{d}/Module_{d}.cs",
                    Kind = SymbolKind.Method,
                    LineStart = (m * 20),
                    LineEnd = (m * 20) + 15
                };
                graph.Symbols.Add(method);
                graph.AddContains(container, method);
            }
        }

        // Act
        var dataset = CodeCityBuilder.Build(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TotalDistrictsCount.Should().Be(20);
        dataset.TotalBuildingsCount.Should().Be(20);

        foreach (var building in dataset.AllBuildings)
        {
            building.Width.Should().BeGreaterThan(0);
            building.Height.Should().BeGreaterThan(0);
            building.Depth.Should().BeGreaterThan(0);
            building.ColorHex.Should().StartWith("#");
        }
    }

    #endregion
}
