using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ControlFlow;

/// <summary>
/// Type of basic block within the Control Flow Graph (CFG).
/// </summary>
public enum ControlFlowBlockKind
{
    /// <summary>Procedure entry point (Header / Arguments check).</summary>
    Entry,

    /// <summary>Linear sequential block of instructions (Basic Block without intermediate jumps).</summary>
    BasicBlock,

    /// <summary>Binary conditional decision point (If ... Then ... Else).</summary>
    DecisionIf,

    /// <summary>Multiple decision point (Select Case / Switch).</summary>
    DecisionSelect,

    /// <summary>Loop header (For / While / Do Loop / Foreach).</summary>
    LoopHeader,

    /// <summary>Loop body.</summary>
    LoopBody,

    /// <summary>Exception / Error Handler (On Error GoTo / catch / except).</summary>
    ExceptionHandler,

    /// <summary>Exit or return point (Exit Sub / Exit Function / Return / End).</summary>
    Exit
}

/// <summary>
/// Type of branch or jump between blocks of the CFG.
/// </summary>
public enum ControlFlowBranchKind
{
    Unconditional,
    TrueBranch,
    FalseBranch,
    CaseBranch,
    ExceptionJump,
    LoopBack,
    Fallthrough
}

/// <summary>
/// Control Flow Graph single node/basic block.
/// </summary>
public sealed class ControlFlowNode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ControlFlowBlockKind BlockKind { get; init; } = ControlFlowBlockKind.BasicBlock;
    public string Title { get; init; } = string.Empty;
    public List<string> Statements { get; init; } = new();
    public bool IsUnreachable { get; set; }
    public int LineNumber { get; set; }

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 180.0;
    public double Height { get; set; } = 60.0;
}

/// <summary>
/// Arc or branch oriented between two basic blocks of the CFG.
/// </summary>
public sealed class ControlFlowEdge
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SourceBlockId { get; init; }
    public Guid TargetBlockId { get; init; }
    public ControlFlowBranchKind BranchKind { get; init; } = ControlFlowBranchKind.Unconditional;
    public string? ConditionLabel { get; init; }
}

/// <summary>
/// Complete control flow graph model for a single procedure or method.
/// </summary>
public sealed class ProcedureControlFlowGraph
{
    public Guid ProcedureId { get; init; }
    public string ProcedureName { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public int CyclomaticComplexity { get; set; } = 1;
    public int UnreachableBlocksCount { get; set; }

    public List<ControlFlowNode> Blocks { get; init; } = new();
    public List<ControlFlowEdge> Edges { get; init; } = new();

    public ControlFlowNode? FindBlock(Guid id) => Blocks.FirstOrDefault(b => b.Id == id);
}
