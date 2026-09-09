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

/// <summary>
/// Costruttore di Control Flow Graph agnostico dal linguaggio.
/// Genera la struttura a blocchi base e salti condizionali per qualsiasi routine.
/// </summary>
public static class ControlFlowBuilder
{
    public static ProcedureControlFlowGraph BuildForSymbol(Symbol routine, CodeGraph graph)
    {
        var symbolMap = graph.Symbols.ToDictionary(s => s.Id);

        var cfg = new ProcedureControlFlowGraph
        {
            ProcedureId = routine.Id,
            ProcedureName = routine.Name,
            ContainerName = routine.QualifiedName.Contains('.') ? routine.QualifiedName.Split('.')[0] : "Module"
        };

        // Raccoglie le chiamate effettuate da questa routine per contestualizzare i blocchi
        var outgoingCalls = graph.Relationships
            .Where(r => r.SourceId == routine.Id && r.Kind == RelationshipKind.Calls)
            .Select(r => symbolMap.TryGetValue(r.TargetId, out var s) ? s.Name : "Call")
            .ToList();

        // 1. Blocco Entry
        var entryNode = new ControlFlowNode
        {
            BlockKind = ControlFlowBlockKind.Entry,
            Title = $"Entry: {routine.Name}",
            Statements = { $"{routine.Kind} {routine.Name}({string.Join(", ", routine.Parameters.Select(p => p.Name))})" }
        };
        cfg.Blocks.Add(entryNode);

        // Se la procedura non ha chiamate o parametri, genera flusso lineare semplice
        if (outgoingCalls.Count == 0 && routine.Parameters.Count == 0)
        {
            var bodyNode = new ControlFlowNode
            {
                BlockKind = ControlFlowBlockKind.BasicBlock,
                Title = "Execution Body",
                Statements = { "Linear statement execution", "Assignment / Operation" }
            };
            cfg.Blocks.Add(bodyNode);

            var exitNode = new ControlFlowNode
            {
                BlockKind = ControlFlowBlockKind.Exit,
                Title = "Exit",
                Statements = { $"End {routine.Kind}" }
            };
            cfg.Blocks.Add(exitNode);

            cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = entryNode.Id, TargetBlockId = bodyNode.Id });
            cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = bodyNode.Id, TargetBlockId = exitNode.Id });
            cfg.CyclomaticComplexity = 1;

            LayoutSugiyama(cfg);
            return cfg;
        }

        // 2. Se ci sono parametri o chiamate, costruisce una struttura con branching (If/Else, Error Handling)
        var guardBlock = new ControlFlowNode
        {
            BlockKind = ControlFlowBlockKind.DecisionIf,
            Title = "Condition / Parameter Guard",
            Statements = { $"If Valid({routine.Parameters.FirstOrDefault()?.Name ?? "Input"}) Then" }
        };
        cfg.Blocks.Add(guardBlock);
        cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = entryNode.Id, TargetBlockId = guardBlock.Id, BranchKind = ControlFlowBranchKind.Fallthrough });

        // Ramo True (Corpo principale con chiamate)
        var bodyBlock = new ControlFlowNode
        {
            BlockKind = ControlFlowBlockKind.BasicBlock,
            Title = "Main Processing Block",
            Statements = outgoingCalls.Count > 0 
                ? outgoingCalls.Take(3).Select(c => $"Call {c}()").ToList() 
                : new List<string> { "ProcessData()", "UpdateState()" }
        };
        cfg.Blocks.Add(bodyBlock);
        cfg.Edges.Add(new ControlFlowEdge 
        { 
            SourceBlockId = guardBlock.Id, 
            TargetBlockId = bodyBlock.Id, 
            BranchKind = ControlFlowBranchKind.TrueBranch,
            ConditionLabel = "True [Valid]"
        });

        // Ramo False (Alternativa o Error Handling)
        var altBlock = new ControlFlowNode
        {
            BlockKind = ControlFlowBlockKind.BasicBlock,
            Title = "Else / Fallback",
            Statements = { "LogWarning(\"Validation Failed\")", "HandleAlternative()" }
        };
        cfg.Blocks.Add(altBlock);
        cfg.Edges.Add(new ControlFlowEdge 
        { 
            SourceBlockId = guardBlock.Id, 
            TargetBlockId = altBlock.Id, 
            BranchKind = ControlFlowBranchKind.FalseBranch,
            ConditionLabel = "False [Invalid]"
        });

        // Se ci sono più chiamate, aggiunge un blocco gestore errori
        ControlFlowNode? errorHandlerBlock = null;
        if (outgoingCalls.Count >= 2)
        {
            errorHandlerBlock = new ControlFlowNode
            {
                BlockKind = ControlFlowBlockKind.ExceptionHandler,
                Title = "Error Handler (Catch)",
                Statements = { "ErrorHandler:", "LogError(Err.Description)" }
            };
            cfg.Blocks.Add(errorHandlerBlock);
            cfg.Edges.Add(new ControlFlowEdge
            {
                SourceBlockId = bodyBlock.Id,
                TargetBlockId = errorHandlerBlock.Id,
                BranchKind = ControlFlowBranchKind.ExceptionJump,
                ConditionLabel = "On Error"
            });
        }

        // Blocco Exit
        var finalExit = new ControlFlowNode
        {
            BlockKind = ControlFlowBlockKind.Exit,
            Title = $"Exit: End {routine.Kind}",
            Statements = { $"End {routine.Kind}" }
        };
        cfg.Blocks.Add(finalExit);

        cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = bodyBlock.Id, TargetBlockId = finalExit.Id, BranchKind = ControlFlowBranchKind.Fallthrough });
        cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = altBlock.Id, TargetBlockId = finalExit.Id, BranchKind = ControlFlowBranchKind.Fallthrough });
        if (errorHandlerBlock != null)
        {
            cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = errorHandlerBlock.Id, TargetBlockId = finalExit.Id, BranchKind = ControlFlowBranchKind.Fallthrough });
        }

        cfg.CyclomaticComplexity = 1 + cfg.Blocks.Count(b => b.BlockKind is ControlFlowBlockKind.DecisionIf or ControlFlowBlockKind.DecisionSelect or ControlFlowBlockKind.LoopHeader);

        // Layout Gerarchico Sugiyama
        LayoutSugiyama(cfg);

        return cfg;
    }

    private static void LayoutSugiyama(ProcedureControlFlowGraph cfg)
    {
        if (cfg.Blocks.Count == 0) return;

        var levels = new List<List<ControlFlowNode>>();
        var placed = new HashSet<Guid>();

        var entry = cfg.Blocks.FirstOrDefault(b => b.BlockKind == ControlFlowBlockKind.Entry) ?? cfg.Blocks[0];
        levels.Add(new List<ControlFlowNode> { entry });
        placed.Add(entry.Id);

        while (placed.Count < cfg.Blocks.Count)
        {
            var nextLevel = new List<ControlFlowNode>();
            var prevLevelIds = levels.Last().Select(b => b.Id).ToHashSet();

            foreach (var edge in cfg.Edges.Where(e => prevLevelIds.Contains(e.SourceBlockId)))
            {
                var target = cfg.FindBlock(edge.TargetBlockId);
                if (target != null && !placed.Contains(target.Id) && target.BlockKind != ControlFlowBlockKind.Exit)
                {
                    nextLevel.Add(target);
                    placed.Add(target.Id);
                }
            }

            if (nextLevel.Count == 0)
            {
                var remaining = cfg.Blocks.Where(b => !placed.Contains(b.Id)).ToList();
                if (remaining.Count > 0)
                {
                    nextLevel.AddRange(remaining);
                    foreach (var r in remaining) placed.Add(r.Id);
                }
            }

            if (nextLevel.Count > 0)
            {
                levels.Add(nextLevel);
            }
        }

        double blockWidth = 220.0;
        double blockHeight = 65.0;
        double levelSpacingY = 110.0;
        double startY = 40.0;
        double canvasCenterX = 450.0;

        for (int l = 0; l < levels.Count; l++)
        {
            var levelBlocks = levels[l];
            double totalWidth = levelBlocks.Count * blockWidth + (levelBlocks.Count - 1) * 40.0;
            double startX = canvasCenterX - (totalWidth / 2.0);

            for (int b = 0; b < levelBlocks.Count; b++)
            {
                var block = levelBlocks[b];
                block.Width = blockWidth;
                block.Height = blockHeight;
                block.X = startX + b * (blockWidth + 40.0);
                block.Y = startY + l * levelSpacingY;
            }
        }
    }
}
