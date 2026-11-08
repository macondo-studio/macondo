using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ControlFlow;

/// <summary>
/// Tipologia di blocco base all'interno del Control Flow Graph (CFG).
/// </summary>
public enum ControlFlowBlockKind
{
    /// <summary>Punto di ingresso della procedura (Header / Arguments check).</summary>
    Entry,

    /// <summary>Blocco sequenziale lineare di istruzioni (Basic Block senza salti intermedi).</summary>
    BasicBlock,

    /// <summary>Punto di decisione condizionale binaria (If ... Then ... Else).</summary>
    DecisionIf,

    /// <summary>Punto di decisione multipla (Select Case / Switch).</summary>
    DecisionSelect,

    /// <summary>Intestazione di ciclo (For / While / Do Loop / Foreach).</summary>
    LoopHeader,

    /// <summary>Corpo del ciclo.</summary>
    LoopBody,

    /// <summary>Gestore di eccezioni / errori (On Error GoTo / catch / except).</summary>
    ExceptionHandler,

    /// <summary>Punto di uscita o ritorno (Exit Sub / Exit Function / Return / End).</summary>
    Exit
}

/// <summary>
/// Tipologia di ramo o salto tra blocchi del CFG.
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
/// Singolo nodo / blocco base del Control Flow Graph.
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
/// Arco o ramo orientato tra due blocchi base del CFG.
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
/// Modello completo del grafo di flusso di controllo per una singola procedura o metodo.
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
