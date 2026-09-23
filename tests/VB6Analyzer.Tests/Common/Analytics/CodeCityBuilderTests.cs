using FluentAssertions;
using Macondo.Semantic.Analytics.CodeCity;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CodeCityBuilderTests
{
    /// <summary>
    /// Verifica la costruzione della mappa urbana 3D CodeCity sul set di fixture di progetto,
    /// accertando la corretta generazione di distretti, edifici e calcolo delle coordinate spaziali.
    /// </summary>
    [Fact]
    public void CodeCityBuilder_ShouldBuildDistrictsAndBuildings_FromCodeGraph()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var dataset = CodeCityBuilder.Build(graph);

        // Assert
        dataset.Should().NotBeNull();
        dataset.TotalDistrictsCount.Should().BeGreaterThan(0);
        dataset.TotalBuildingsCount.Should().BeGreaterThan(0);
        dataset.TotalLoc.Should().BeGreaterThan(0);
        dataset.MaxHeight.Should().BeGreaterThan(0.0);

        foreach (var district in dataset.Districts)
        {
            district.Name.Should().NotBeNullOrWhiteSpace();
            district.Width.Should().BeGreaterThan(0);
            district.Depth.Should().BeGreaterThan(0);
            district.Buildings.Should().NotBeEmpty();

            foreach (var building in district.Buildings)
            {
                building.Width.Should().BeGreaterThan(0);
                building.Depth.Should().BeGreaterThan(0);
                building.Height.Should().BeGreaterThan(0);
                building.ColorHex.Should().StartWith("#");
                building.RoofColorHex.Should().StartWith("#");
                building.SideColorHex.Should().StartWith("#");
            }
        }
    }

    /// <summary>
    /// Verifica che le dimensioni degli edifici scalino coerentemente:
    /// - Le linee di codice (LOC) determinano l'impronta a terra (Width e Depth).
    /// - La complessità ciclomatica determina l'altezza 3D (Height).
    /// </summary>
    [Fact]
    public void CodeCityBuilder_ShouldCalculateBuildingDimensions_BasedOnLocAndComplexity()
    {
        // Arrange: Un piccolo modulo semplice (10 LOC, 0 chiamate) e un grande modulo complesso (500 LOC, 10 chiamate)
        var graph = new CodeGraph();

        var smallMod = new Symbol { Id = Guid.NewGuid(), Name = "SmallHelper", QualifiedName = "SmallHelper", FilePath = "SmallHelper.bas", Kind = SymbolKind.Module, LineStart = 1, LineEnd = 10 };
        graph.Symbols.Add(smallMod);

        var complexMod = new Symbol { Id = Guid.NewGuid(), Name = "ComplexEngine", QualifiedName = "ComplexEngine", FilePath = "ComplexEngine.cls", Kind = SymbolKind.Class, LineStart = 1, LineEnd = 500 };
        graph.Symbols.Add(complexMod);

        for (int i = 0; i < 10; i++)
        {
            var method = new Symbol { Id = Guid.NewGuid(), Name = $"Method_{i}", QualifiedName = $"ComplexEngine.Method_{i}", FilePath = "ComplexEngine.cls", Kind = SymbolKind.Method, LineStart = i * 40, LineEnd = (i + 1) * 40 };
            graph.Symbols.Add(method);
            graph.AddContains(complexMod, method);

            // Aggiungi chiamate per elevare la complessità ciclomatica
            var target = new Symbol { Id = Guid.NewGuid(), Name = $"Target_{i}", QualifiedName = $"Target_{i}", FilePath = "Target.bas", Kind = SymbolKind.Method };
            graph.Symbols.Add(target);
            graph.AddCalls(method, target);
        }

        // Act
        var dataset = CodeCityBuilder.Build(graph);
        var smallBuilding = dataset.AllBuildings.First(b => b.Id == smallMod.Id);
        var complexBuilding = dataset.AllBuildings.First(b => b.Id == complexMod.Id);

        // Assert
        complexBuilding.Width.Should().BeGreaterThan(smallBuilding.Width);
        complexBuilding.Depth.Should().BeGreaterThan(smallBuilding.Depth);
        complexBuilding.Height.Should().BeGreaterThan(smallBuilding.Height);
    }

    /// <summary>
    /// Verifica che ad alta complessità ciclomatica venga assegnata una palette di colore calda/critica (Rosso/Arancio),
    /// mentre a bassa complessità venga assegnata una colorazione neutra/fredda (Blu/Verde).
    /// </summary>
    [Fact]
    public void CodeCityBuilder_ShouldAssignColorPalette_BasedOnCyclomaticComplexity()
    {
        // Arrange
        var graph = new CodeGraph();

        var simpleMod = new Symbol { Id = Guid.NewGuid(), Name = "SimpleMod", QualifiedName = "SimpleMod", FilePath = "SimpleMod.bas", Kind = SymbolKind.Module, LineStart = 1, LineEnd = 20 };
        graph.Symbols.Add(simpleMod);

        var extremeMod = new Symbol { Id = Guid.NewGuid(), Name = "ExtremeMod", QualifiedName = "ExtremeMod", FilePath = "ExtremeMod.cls", Kind = SymbolKind.Class, LineStart = 1, LineEnd = 200 };
        graph.Symbols.Add(extremeMod);

        // Aggiungi 25 chiamate uscenti per generare una complessità estrema (CC > 20)
        var callerMethod = new Symbol { Id = Guid.NewGuid(), Name = "SuperComplexRoutine", QualifiedName = "ExtremeMod.SuperComplexRoutine", FilePath = "ExtremeMod.cls", Kind = SymbolKind.Method };
        graph.Symbols.Add(callerMethod);
        graph.AddContains(extremeMod, callerMethod);

        for (int i = 0; i < 25; i++)
        {
            var tgt = new Symbol { Id = Guid.NewGuid(), Name = $"T_{i}", QualifiedName = $"T_{i}", FilePath = "T.bas", Kind = SymbolKind.Method };
            graph.Symbols.Add(tgt);
            graph.AddCalls(callerMethod, tgt);
        }

        // Act
        var dataset = CodeCityBuilder.Build(graph);
        var simpleB = dataset.AllBuildings.First(b => b.Id == simpleMod.Id);
        var extremeB = dataset.AllBuildings.First(b => b.Id == extremeMod.Id);

        // Assert
        simpleB.ColorHex.Should().Be("#2563EB"); // Blu moderno
        extremeB.ColorHex.Should().Be("#DC2626"); // Rosso intenso
    }
}
