using FluentAssertions;
using Macondo.Semantic.Analytics.Treemap;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class TreemapLayoutTests
{
    /// <summary>
    /// Verify that TreemapModelBuilder and TreemapLayoutEngine calculate spatial subdivision (Squarified Treemap)
    /// placing all rectangles within the specified viewport.
    /// </summary>
    [Fact]
    public void Treemap_ShouldBuildAndLayoutSquarifiedTreemap_FromGraph()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();

        // Act
        var treemap = TreemapModelBuilder.Build(graph);
        TreemapLayoutEngine.Layout(treemap, new TreemapRect(0, 0, 1000, 600));

        // Assert
        treemap.Should().NotBeNull();
        treemap.Value.Should().BeGreaterThan(0);
        treemap.Children.Should().NotBeEmpty();

        foreach (var child in treemap.Children)
        {
            child.Bounds.Width.Should().BeGreaterThanOrEqualTo(0);
            child.Bounds.Height.Should().BeGreaterThanOrEqualTo(0);
            child.Bounds.X.Should().BeInRange(0, 1000);
            child.Bounds.Y.Should().BeInRange(0, 600);
        }
    }

    /// <summary>
    /// Tests the robustness of the Treemap layout engine in case of a single node with no children.
    /// </summary>
    [Fact]
    public void TreemapLayoutEngine_ShouldHandleSingleNodeAndEmptyChildren_WithoutErrors()
    {
        // Arrange
        var root = new TreemapItem
        {
            Id = Guid.NewGuid(),
            Name = "SingleModule",
            Value = 100,
            MetricScore = 5.0
        };

        // Act & Assert
        var act = () => TreemapLayoutEngine.Layout(root, new TreemapRect(0, 0, 800, 600));
        act.Should().NotThrow();
        root.Bounds.Width.Should().Be(800);
        root.Bounds.Height.Should().Be(600);
    }

    /// <summary>
    /// Tests the TreemapRect.Contains hit-testing method to identify whether a point (X, Y) falls within the rectangle.
    /// </summary>
    [Fact]
    public void TreemapRect_Contains_ShouldCorrectlyIdentifyPointWithinBounds()
    {
        var rect = new TreemapRect(10, 20, 100, 200);

        rect.Contains(50, 50).Should().BeTrue();
        rect.Contains(10, 20).Should().BeTrue();
        rect.Contains(110, 220).Should().BeTrue();
        rect.Contains(5, 50).Should().BeFalse();
        rect.Contains(50, 250).Should().BeFalse();
    }
}
