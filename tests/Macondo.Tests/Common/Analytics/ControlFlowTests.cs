using FluentAssertions;
using Macondo.Semantic.Analytics.ControlFlow;
using Macondo.Semantic.Model;
using Macondo.Tests.Helpers;
using Xunit;

namespace Macondo.Tests.Common.Analytics;

public class ControlFlowTests
{
    /// <summary>
    /// Verify that ControlFlowBuilder constructs the control flow graph (CFG) and computes
    /// the cyclomatic complexity for a routine extracted from the source code.
    /// </summary>
    [Fact]
    public void ControlFlowBuilder_ShouldBuildCFGAndCalculateComplexity()
    {
        // Arrange
        var (graph, _) = TestGraphFactory.CreateAndAnalyzeAllFixtures();
        var sampleRoutine = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Method);
        sampleRoutine.Should().NotBeNull();

        // Act
        var cfg = ControlFlowBuilder.BuildForSymbol(sampleRoutine!, graph);

        // Assert
        cfg.Should().NotBeNull();
        cfg.ProcedureName.Should().Be(sampleRoutine!.Name);
        cfg.Blocks.Should().NotBeEmpty();
        cfg.CyclomaticComplexity.Should().BeGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// Verifies that decision blocks (DecisionIf) are generated for complex routines with parameters and multiple calls,
    /// exception paths (ExceptionHandler), True/False/ExceptionJump branches, and valid layout coordinates.
    /// </summary>
    [Fact]
    public void ControlFlowBuilder_ShouldGenerateBranchingAndExceptionHandler_ForComplexRoutine()
    {
        // Arrange: Method with multiple parameters and >=2 outgoing calls
        var graph = new CodeGraph();
        var routine = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ProcessPayment",
            QualifiedName = "Billing.ProcessPayment",
            FilePath = "Billing.cls",
            Kind = SymbolKind.Method
        };
        routine.Parameters.Add(new ParameterSymbol { Name = "amount", DeclaredTypeName = "Double" });
        routine.Parameters.Add(new ParameterSymbol { Name = "customerId", DeclaredTypeName = "Long" });

        var callee1 = new Symbol { Id = Guid.NewGuid(), Name = "ValidateCard", QualifiedName = "Payment.ValidateCard", FilePath = "Payment.cls", Kind = SymbolKind.Method };
        var callee2 = new Symbol { Id = Guid.NewGuid(), Name = "ChargeCard", QualifiedName = "Payment.ChargeCard", FilePath = "Payment.cls", Kind = SymbolKind.Method };

        graph.Symbols.AddRange(new[] { routine, callee1, callee2 });
        graph.AddCalls(routine, callee1, 10);
        graph.AddCalls(routine, callee2, 20);

        // Act
        var cfg = ControlFlowBuilder.BuildForSymbol(routine, graph);

        // Assert
        cfg.Blocks.Should().Contain(b => b.BlockKind == ControlFlowBlockKind.Entry);
        cfg.Blocks.Should().Contain(b => b.BlockKind == ControlFlowBlockKind.DecisionIf);
        cfg.Blocks.Should().Contain(b => b.BlockKind == ControlFlowBlockKind.ExceptionHandler);
        cfg.Blocks.Should().Contain(b => b.BlockKind == ControlFlowBlockKind.Exit);

        cfg.Edges.Should().Contain(e => e.BranchKind == ControlFlowBranchKind.TrueBranch);
        cfg.Edges.Should().Contain(e => e.BranchKind == ControlFlowBranchKind.FalseBranch);
        cfg.Edges.Should().Contain(e => e.BranchKind == ControlFlowBranchKind.ExceptionJump);

        cfg.CyclomaticComplexity.Should().BeGreaterThan(1);

        foreach (var block in cfg.Blocks)
        {
            block.X.Should().NotBe(double.NaN);
            block.Y.Should().NotBe(double.NaN);
        }
    }

    /// <summary>
    /// Verifies that a minimal CFG is generated for linear routines without conditional branches or parameters
    /// (Entry -> BasicBlock -> Exit) with cyclomatic complexity equal to 1.
    /// </summary>
    [Fact]
    public void ControlFlowBuilder_ShouldGenerateLinearFlow_ForSimpleRoutine()
    {
        // Arrange: Routine without parameters and without outgoing calls
        var graph = new CodeGraph();
        var simpleSub = new Symbol
        {
            Id = Guid.NewGuid(),
            Name = "ResetCounters",
            QualifiedName = "Module1.ResetCounters",
            FilePath = "Module1.bas",
            Kind = SymbolKind.Method
        };
        graph.Symbols.Add(simpleSub);

        // Act
        var cfg = ControlFlowBuilder.BuildForSymbol(simpleSub, graph);

        // Assert
        cfg.CyclomaticComplexity.Should().Be(1);
        cfg.Blocks.Should().HaveCount(3); // Entry -> BasicBlock -> Exit
        cfg.Blocks.Select(b => b.BlockKind).Should().Contain(new[] { ControlFlowBlockKind.Entry, ControlFlowBlockKind.BasicBlock, ControlFlowBlockKind.Exit });
    }
}
