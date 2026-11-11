using FluentAssertions;
using Macondo.Semantic.Analytics.CodeCity;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class CodeCityBuilderTests
{
    /// <summary>
    /// Check the construction of the 3D CodeCity urban map on the set of project fixtures,
    /// ensuring the correct generation of districts, buildings and calculation of spatial coordinates.
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
    /// Check that building sizes scale consistently:
    /// - The lines of code (LOC) determine the footprint (Width and Depth).
    /// - Cyclomatic complexity determines the 3D height (Height).
    /// </summary>
    [Fact]
    public void CodeCityBuilder_ShouldCalculateBuildingDimensions_BasedOnLocAndComplexity()
    {
        // Arrange: One small simple module (10 LOC, 0 calls) and one large complex module (500 LOC, 10 calls)
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

            // Add calls to raise cyclomatic complexity
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
    /// Verify that high cyclomatic complexity is assigned a warm/critical color palette (Red/Orange),
    /// while low complexity is assigned a neutral/cold color (Blue/Green).
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

        // Add 25 outgoing calls to generate extreme complexity (CC > 20)
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
