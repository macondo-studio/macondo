using FluentAssertions;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.CSharpSpecific;

public class CSharpAdvancedCallGraphTests
{
    /// <summary>
    /// Verify internal method calls to Lambda expressions and LINQ extension methods
    /// (e.g. .Select(x => TransformItem(x)).Where(x => FilterItem(x))) are assigned correctly
    /// to the enclosing C# routine.
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldDetectCallsInsideLambdasAndLinqExpressions()
    {
        // Arrange
        var code = """
            using System.Linq;
            using System.Collections.Generic;

            namespace MyApp.Linq
            {
                public class Pipeline
                {
                    public void RunPipeline(List<string> inputs)
                    {
                        var processed = inputs.Select(x => TransformItem(x)).Where(x => FilterItem(x)).ToList();
                    }

                    public string TransformItem(string input) => input.Trim();
                    public bool FilterItem(string input) => !string.IsNullOrEmpty(input);
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Pipeline.cs", code);

        // Assert
        var runMethod = graph.Symbols.First(s => s.Name == "RunPipeline");
        var transformMethod = graph.Symbols.First(s => s.Name == "TransformItem");
        var filterMethod = graph.Symbols.First(s => s.Name == "FilterItem");

        var callRels = graph.Relationships.Where(r => r.Kind == RelationshipKind.Calls && r.SourceId == runMethod.Id).ToList();

        callRels.Should().Contain(r => r.TargetId == transformMethod.Id, "Lambdas inside LINQ Select should attribute calls to enclosing method");
        callRels.Should().Contain(r => r.TargetId == filterMethod.Id, "Lambdas inside LINQ Where should attribute calls to enclosing method");
    }

    /// <summary>
    /// Tests detection and logging of static method calls on utility classes (e.g. MathUtility.DoubleValue(x)).
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldDetectStaticMethodInvocations()
    {
        // Arrange
        var code = """
            namespace MyApp.Utils
            {
                public static class MathUtility
                {
                    public static int DoubleValue(int v) => v * 2;
                }

                public class CalculationService
                {
                    public int Compute(int x)
                    {
                        return MathUtility.DoubleValue(x);
                    }
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeCSharpSource("MathUtil.cs", code);

        // Assert
        var computeMethod = graph.Symbols.First(s => s.Name == "Compute");
        var doubleValMethod = graph.Symbols.First(s => s.Name == "DoubleValue");

        var callRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Calls && r.SourceId == computeMethod.Id && r.TargetId == doubleValMethod.Id);
        callRel.Should().NotBeNull("Static method calls on utility classes should be recorded in Call Graph");
    }

    /// <summary>
    /// Verify that the Cycle Detection (3-state DFS) algorithm correctly detects both direct recursion
    /// that mutual cyclic recursion between two or more C# methods.
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldDetectDirectAndMutualRecursion_InCSharpCode()
    {
        // Arrange
        var code = """
            namespace MyApp.Recursion
            {
                public class RecursiveWorker
                {
                    public void SelfRecursive(int n)
                    {
                        if (n > 0)
                            SelfRecursive(n - 1);
                    }

                    public void Ping(int count)
                    {
                        if (count > 0)
                            Pong(count - 1);
                    }

                    public void Pong(int count)
                    {
                        if (count > 0)
                            Ping(count - 1);
                    }
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeCSharpSource("Recursion.cs", code);
        var queries = new CallGraphQueries(graph);
        var cycles = queries.DetectCycles();

        // Assert
        cycles.Should().NotBeEmpty();

        var cycleNames = cycles.Select(c => c.ToString()).ToList();
        cycleNames.Should().Contain(c => c.Contains("SelfRecursive"));
        cycleNames.Should().Contain(c => c.Contains("Ping") && c.Contains("Pong"));
    }

    /// <summary>
    /// Verify that the extractor correctly identifies the instantiated target type even when
    /// enclosed within generic collections (e.g. 'new List<DataItem>()').
    /// </summary>
    [Fact]
    public void CallGraphBuilder_ShouldExtractGenericInstantiations()
    {
        // Arrange
        var code = """
            using System.Collections.Generic;

            namespace MyApp.Factory
            {
                public class DataItem { }

                public class ItemStore
                {
                    private List<DataItem> _items = new List<DataItem>();

                    public void AddItem()
                    {
                        var localList = new List<DataItem>();
                        var single = new DataItem();
                    }
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeCSharpSource("ItemStore.cs", code);

        // Assert
        var dataItemClass = graph.Symbols.First(s => s.Kind == SymbolKind.Class && s.Name == "DataItem");
        var addItemMethod = graph.Symbols.First(s => s.Name == "AddItem");

        var instantiatesRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Instantiates && r.SourceId == addItemMethod.Id && r.TargetId == dataItemClass.Id);
        instantiatesRel.Should().NotBeNull("Instantiation of DataItem should be extracted even when surrounded by generic collections");
    }

    /// <summary>
    /// Verify multi-hop execution path search (A -> B -> C -> D -> E) and calculation
    /// of the maximum impact depth in the Blast Radius for complex C# chains.
    /// </summary>
    [Fact]
    public void CallGraphQueries_ShouldTrackDeepCallChains_AndCalculateMultiHopImpact()
    {
        // Arrange: Chain of 5 methods A -> B -> C -> D -> E
        var code = """
            namespace MyApp.Chain
            {
                public class ServiceChain
                {
                    public void StepA() => StepB();
                    public void StepB() => StepC();
                    public void StepC() => StepD();
                    public void StepD() => StepE();
                    public void StepE() { }
                }
            }
            """;

        // Act
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeCSharpSource("ServiceChain.cs", code);

        var stepA = graph.Symbols.First(s => s.Name == "StepA");
        var stepE = graph.Symbols.First(s => s.Name == "StepE");

        var queries = new CallGraphQueries(graph);
        var paths = queries.FindExecutionPaths(stepA, stepE, maxDepth: 10);

        // Assert
        paths.Should().ContainSingle();
        paths[0].Steps.Select(s => s.Name).Should().Equal("StepA", "StepB", "StepC", "StepD", "StepE");

        var impact = queries.GetImpactReport(stepE, maxDepth: 10);
        impact.DirectCallers.Should().ContainSingle(c => c.Name == "StepD");
        impact.IndirectCallers.Should().HaveCount(3); // StepC, StepB, StepA
        impact.IndirectCallers.Select(c => c.Symbol.Name).Should().Contain(new[] { "StepC", "StepB", "StepA" });
        impact.MaxDependencyDepth.Should().Be(4);
    }
}
