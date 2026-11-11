using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ControlFlow;

/// <summary>
/// Language agnostic Control Flow Graph builder.
/// Generate the basic block structure and conditional jumps for any routine.
/// </summary>
public static class ControlFlowBuilder
{
    /// <summary>
    /// Constructs the semantic Control Flow Graph (CFG) for the specified procedure symbol (Sub, Function, Property).
    /// </summary>
    /// <param name="routine">The symbol of the routine to model.</param>
    /// <param name="graph">The CodeGraph containing the reports and calls made.</param>
    /// <returns>The <see cref="ProcedureControlFlowGraph"/> model with basic blocks, branches and calculated spatial coordinates.</returns>
    /// <remarks>
    /// The modeling algorithm:
    /// <list type="number">
    /// <item><description>Creates the input block <see cref="ControlFlowBlockKind.Entry"/> with the signature and formal parameters of the routine.</description></item>
    /// <item><description>If the routine is linear (no calls/branching), generates a simple body with direct transition to the exit (<see cref="ControlFlowBlockKind.Exit"/>).</description></item>
    /// <item><description>If there are calls or parameters, structures a conditional guard (<see cref="ControlFlowBlockKind.DecisionIf"/>), a main processing branch (True), and an alternative fallback branch (False).</description></item>
    /// <item><description>In case of multiple external calls, models a dedicated error handling block (<see cref="ControlFlowBlockKind.ExceptionHandler"/>) with exceptional skip.</description></item>
    /// <item><description>Connect all execution branches to the termination block <see cref="ControlFlowBlockKind.Exit"/> and calculate the McCabe cyclomatic complexity: <c>M = 1 + DecisionBlocks</c>.</description></item>
    /// <item><description>Executes Sugiyama's hierarchical algorithm (<see cref="LayoutSugiyama"/>) to calculate the tiered block arrangement.</description></item>
    /// </list>
    /// </remarks>
    public static ProcedureControlFlowGraph BuildForSymbol(Symbol routine, CodeGraph graph)
    {
        var symbolMap = graph.Symbols.ToDictionary(s => s.Id);

        var cfg = new ProcedureControlFlowGraph
        {
            ProcedureId = routine.Id,
            ProcedureName = routine.Name,
            ContainerName = routine.QualifiedName.Contains('.') ? routine.QualifiedName.Split('.')[0] : "Module"
        };

        // Collects the names of outgoing calls made by this routine
        var outgoingCalls = graph.Relationships
            .Where(r => r.SourceId == routine.Id && r.Kind == RelationshipKind.Calls)
            .Select(r => symbolMap.TryGetValue(r.TargetId, out var s) ? s.Name : "Call")
            .ToList();

        // ---------------------------------------------------------------------
        // STEP 1: Creation of the Entry Block.
        // Contains the formal signature of the procedure and the list of parameters.
        // ---------------------------------------------------------------------
        var entryNode = new ControlFlowNode
        {
            BlockKind = ControlFlowBlockKind.Entry,
            Title = $"Entry: {routine.Name}",
            Statements = { $"{routine.Kind} {routine.Name}({string.Join(", ", routine.Parameters.Select(p => p.Name))})" }
        };
        cfg.Blocks.Add(entryNode);

        // ---------------------------------------------------------------------
        // STEP 2: Management of linear routines (without outgoing calls or parameters).
        // If there are no decision points, the flow is a direct sequence Entry -> Body -> Exit.
        // ---------------------------------------------------------------------
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
            // Complessità ciclomatica base = 1
            cfg.CyclomaticComplexity = 1;

            LayoutSugiyama(cfg);
            return cfg;
        }

        // ---------------------------------------------------------------------
        // STEP 3: Structuring complex routines with branches (Branching If/Else / Catch).
        // ---------------------------------------------------------------------
        // 3a. Decision block / validation of input parameters
        var guardBlock = new ControlFlowNode
        {
            BlockKind = ControlFlowBlockKind.DecisionIf,
            Title = "Condition / Parameter Guard",
            Statements = { $"If Valid({routine.Parameters.FirstOrDefault()?.Name ?? "Input"}) Then" }
        };
        cfg.Blocks.Add(guardBlock);
        cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = entryNode.Id, TargetBlockId = guardBlock.Id, BranchKind = ControlFlowBranchKind.Fallthrough });

        // 3b. Branch True: Main processing block with external calls
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

        // 3c. False branch: Alternate/fallback management block
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

        // 3d. Exception Handling: If there are multiple external calls, model an ExceptionHandler block
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

        // ---------------------------------------------------------------------
        // STEP 4: Exit Block and convergence of all branches.
        // ---------------------------------------------------------------------
        var finalExit = new ControlFlowNode
        {
            BlockKind = ControlFlowBlockKind.Exit,
            Title = $"Exit: End {routine.Kind}",
            Statements = { $"End {routine.Kind}" }
        };
        cfg.Blocks.Add(finalExit);

        // Closure of all paths to the common exit block
        cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = bodyBlock.Id, TargetBlockId = finalExit.Id, BranchKind = ControlFlowBranchKind.Fallthrough });
        cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = altBlock.Id, TargetBlockId = finalExit.Id, BranchKind = ControlFlowBranchKind.Fallthrough });
        if (errorHandlerBlock != null)

            cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = errorHandlerBlock.Id, TargetBlockId = finalExit.Id, BranchKind = ControlFlowBranchKind.Fallthrough });

        // McCabe Cyclomatic Complexity Calculation: M = 1 + number of decision points (If/Select/Loop)
        cfg.CyclomaticComplexity = 1 + cfg.Blocks.Count(b => b.BlockKind is ControlFlowBlockKind.DecisionIf or ControlFlowBlockKind.DecisionSelect or ControlFlowBlockKind.LoopHeader);

        // ---------------------------------------------------------------------
        // STEP 5: Calculating the hierarchical spatial layout (Sugiyama Layout)
        // ---------------------------------------------------------------------
        LayoutSugiyama(cfg);

        return cfg;
    }

    /// <summary>
    /// Calculate the hierarchical arrangement at vertical levels of the blocks of the CFG (Sugiyama Framework).
    /// </summary>
    /// <param name="cfg">The control flow graph to be placed on the 2D plane.</param>
    /// <remarks>
    /// L'algoritmo:
    /// <list type="number">
    /// <item><description>Assigns nodes to progressive horizontal levels starting from the Entry block via BFS traversal guided by directed arcs.</description></item>
    /// <item><description>For each horizontal level, center the blocks with respect to the median axis of the canvas, calculating the cumulative size and spacing.</description></item>
    /// <item><description>Set the absolute coordinates <c>(X, Y)</c> and dimensions for each <see cref="ControlFlowNode"/>.</description></item>
    /// </list>
    /// </remarks>
    private static void LayoutSugiyama(ProcedureControlFlowGraph cfg)
    {
        if (cfg.Blocks.Count == 0) return;

        // 1. Assignment of nodes to hierarchical levels (Layer Assignment via BFS)
        var levels = new List<List<ControlFlowNode>>();
        var placed = new HashSet<Guid>();

        var entry = cfg.Blocks.FirstOrDefault(b => b.BlockKind == ControlFlowBlockKind.Entry) ?? cfg.Blocks[0];
        levels.Add(new List<ControlFlowNode> { entry });
        placed.Add(entry.Id);

        // Continue until all blocks have been allocated to a level
        while (placed.Count < cfg.Blocks.Count)
        {
            var nextLevel = new List<ControlFlowNode>();
            var prevLevelIds = levels.Last().Select(b => b.Id).ToHashSet();

            // Identifies the destination nodes of the blocks of the previous level
            foreach (var edge in cfg.Edges.Where(e => prevLevelIds.Contains(e.SourceBlockId)))
            {
                var target = cfg.FindBlock(edge.TargetBlockId);
                // The Exit block is preferably reserved for the last level
                if (target != null && !placed.Contains(target.Id) && target.BlockKind != ControlFlowBlockKind.Exit)
                {
                    nextLevel.Add(target);
                    placed.Add(target.Id);
                }
            }

            // Fallback for residual nodes that cannot be reached directly
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

                levels.Add(nextLevel);
        }

        // 2. Calculating 2D geometric coordinates (Horizontal centering and vertical spacing)
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
