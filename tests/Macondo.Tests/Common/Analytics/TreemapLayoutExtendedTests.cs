using FluentAssertions;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class TreemapLayoutExtendedTests
{
    /// <summary>
    /// Verify that a multilevel hierarchy (Project -> Modules -> Methods)
    /// is placed with all child rectangles contained strictly within the bounds of the parent.
    /// </summary>
    [Fact]
    public void TreemapLayout_ShouldPositionHierarchicalNodes_WithinParentBounds()
    {
        // Arrange
        var root = new TreemapItem
        {
            Id = Guid.NewGuid(),
            Name = "RootProject",
            Value = 1000
        };

        for (int i = 0; i < 4; i++)
        {
            var child = new TreemapItem
            {
                Id = Guid.NewGuid(),
                Name = $"Module_{i}",
                Value = 250
            };

            for (int j = 0; j < 3; j++)
            {
                child.Children.Add(new TreemapItem
                {
                    Id = Guid.NewGuid(),
                    Name = $"Method_{i}_{j}",
                    Value = 80
                });
            }

            root.Children.Add(child);
        }

        // Act
        TreemapLayoutEngine.Layout(root, new TreemapRect(0, 0, 800, 600));

        // Assert
        root.Bounds.Width.Should().Be(800);
        root.Bounds.Height.Should().Be(600);

        foreach (var child in root.Children)
        {
            child.Bounds.X.Should().BeInRange(0, 800);
            child.Bounds.Y.Should().BeInRange(0, 600);
            (child.Bounds.X + child.Bounds.Width).Should().BeLessOrEqualTo(800.01);
            (child.Bounds.Y + child.Bounds.Height).Should().BeLessOrEqualTo(600.01);

            foreach (var grandchild in child.Children)
            {
                grandchild.Bounds.Width.Should().BeGreaterThanOrEqualTo(0);
                grandchild.Bounds.Height.Should().BeGreaterThanOrEqualTo(0);
            }
        }
    }

    /// <summary>
    /// Verify that viewports with zero dimensions (0x0) do not throw exceptions or NaN values.
    /// </summary>
    [Fact]
    public void TreemapLayout_ShouldHandleZeroDimensions_Safely()
    {
        // Arrange
        var root = new TreemapItem
        {
            Id = Guid.NewGuid(),
            Name = "EmptyRoot",
            Value = 100
        };
        root.Children.Add(new TreemapItem { Id = Guid.NewGuid(), Name = "ChildA", Value = 50 });
        root.Children.Add(new TreemapItem { Id = Guid.NewGuid(), Name = "ChildB", Value = 50 });

        // Act & Assert
        var act = () => TreemapLayoutEngine.Layout(root, new TreemapRect(0, 0, 0, 0));
        act.Should().NotThrow();

        foreach (var child in root.Children)
        {
            child.Bounds.X.Should().NotBe(double.NaN);
            child.Bounds.Y.Should().NotBe(double.NaN);
        }
    }

    /// <summary>
    /// Verify that building the Treemap model from a synthetic CodeGraph extracts
    /// modules and their methods as child nodes.
    /// </summary>
    [Fact]
    public void TreemapModelBuilder_ShouldBuildFromSemanticGraph()
    {
        // Arrange
        var graph = new CodeGraph();
        var mod = new Symbol { Id = Guid.NewGuid(), Name = "MyModule", QualifiedName = "MyModule", FilePath = "MyModule.bas", Kind = SymbolKind.Module, LineStart = 1, LineEnd = 100 };
        var mth = new Symbol { Id = Guid.NewGuid(), Name = "MyMethod", QualifiedName = "MyModule.MyMethod", FilePath = "MyModule.bas", Kind = SymbolKind.Method, LineStart = 10, LineEnd = 30 };

        graph.Symbols.AddRange(new[] { mod, mth });
        graph.AddContains(mod, mth);

        // Act
        var treemap = TreemapModelBuilder.Build(graph);

        // Assert
        treemap.Should().NotBeNull();
        treemap.Children.Should().NotBeEmpty();
    }
}
