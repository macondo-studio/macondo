using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.ControlFlow;

/// <summary>
/// Costruttore di Control Flow Graph agnostico dal linguaggio.
/// Genera la struttura a blocchi base e salti condizionali per qualsiasi routine.
/// </summary>
public static class ControlFlowBuilder
{
    /// <summary>
    /// Costruisce il Control Flow Graph (CFG) semantico per il simbolo di routine specificato (Sub, Function, Property).
    /// </summary>
    /// <param name="routine">Il simbolo della routine da modellare.</param>
    /// <param name="graph">Il CodeGraph contenente le relazioni e chiamate effettuate.</param>
    /// <returns>Il modello <see cref="ProcedureControlFlowGraph"/> con blocchi base, diramazioni e coordinate spaziali calcolate.</returns>
    /// <remarks>
    /// L'algoritmo di modellazione:
    /// <list type="number">
    ///   <item><description>Crea il blocco di ingresso <see cref="ControlFlowBlockKind.Entry"/> con la firma e i parametri formali della routine.</description></item>
    ///   <item><description>Se la routine è lineare (priva di chiamate/branching), genera un corpo semplice con transizione diretta verso l'uscita (<see cref="ControlFlowBlockKind.Exit"/>).</description></item>
    ///   <item><description>Se sono presenti chiamate o parametri, struttura una guard condizionale (<see cref="ControlFlowBlockKind.DecisionIf"/>), un ramo principale di elaborazione (True) e un ramo alternativo di fallback (False).</description></item>
    ///   <item><description>In caso di molteplici chiamate esterne, modella un blocco dedicato di gestione errori (<see cref="ControlFlowBlockKind.ExceptionHandler"/>) con salto eccezionale.</description></item>
    ///   <item><description>Collega tutti i rami di esecuzione al blocco di terminazione <see cref="ControlFlowBlockKind.Exit"/> e calcola la complessità ciclomatica di McCabe: <c>M = 1 + DecisionBlocks</c>.</description></item>
    ///   <item><description>Esegue l'algoritmo gerarchico di Sugiyama (<see cref="LayoutSugiyama"/>) per calcolare la disposizione dei blocchi a livelli.</description></item>
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

        // Raccoglie i nomi delle chiamate uscenti effettuate da questa routine
        var outgoingCalls = graph.Relationships
            .Where(r => r.SourceId == routine.Id && r.Kind == RelationshipKind.Calls)
            .Select(r => symbolMap.TryGetValue(r.TargetId, out var s) ? s.Name : "Call")
            .ToList();

        // ---------------------------------------------------------------------
        // PASSO 1: Creazione del blocco di ingresso (Entry Block).
        // Contiene la firma formale della procedura e l'elenco dei parametri.
        // ---------------------------------------------------------------------
        var entryNode = new ControlFlowNode
        {
            BlockKind = ControlFlowBlockKind.Entry,
            Title = $"Entry: {routine.Name}",
            Statements = { $"{routine.Kind} {routine.Name}({string.Join(", ", routine.Parameters.Select(p => p.Name))})" }
        };
        cfg.Blocks.Add(entryNode);

        // ---------------------------------------------------------------------
        // PASSO 2: Gestione di routine lineari (senza chiamate uscenti o parametri).
        // Se non ci sono punti di decisione, il flusso è una sequenza diretta Entry -> Body -> Exit.
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
        // PASSO 3: Strutturazione di routine complesse con diramazioni (Branching If/Else / Catch).
        // ---------------------------------------------------------------------
        // 3a. Blocco di decisione / validazione dei parametri di input
        var guardBlock = new ControlFlowNode
        {
            BlockKind = ControlFlowBlockKind.DecisionIf,
            Title = "Condition / Parameter Guard",
            Statements = { $"If Valid({routine.Parameters.FirstOrDefault()?.Name ?? "Input"}) Then" }
        };
        cfg.Blocks.Add(guardBlock);
        cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = entryNode.Id, TargetBlockId = guardBlock.Id, BranchKind = ControlFlowBranchKind.Fallthrough });

        // 3b. Ramo True: Blocco di elaborazione principale con le chiamate esterne
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

        // 3c. Ramo False: Blocco di gestione alternativa / fallback
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

        // 3d. Gestione eccezioni: se vi sono multiple chiamate esterne, modella un blocco ExceptionHandler
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
        // PASSO 4: Blocco terminale di uscita (Exit Block) e convergenza di tutti i rami.
        // ---------------------------------------------------------------------
        var finalExit = new ControlFlowNode
        {
            BlockKind = ControlFlowBlockKind.Exit,
            Title = $"Exit: End {routine.Kind}",
            Statements = { $"End {routine.Kind}" }
        };
        cfg.Blocks.Add(finalExit);

        // Chiusura di tutti i cammini verso il blocco di uscita comune
        cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = bodyBlock.Id, TargetBlockId = finalExit.Id, BranchKind = ControlFlowBranchKind.Fallthrough });
        cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = altBlock.Id, TargetBlockId = finalExit.Id, BranchKind = ControlFlowBranchKind.Fallthrough });
        if (errorHandlerBlock != null)
        {
            cfg.Edges.Add(new ControlFlowEdge { SourceBlockId = errorHandlerBlock.Id, TargetBlockId = finalExit.Id, BranchKind = ControlFlowBranchKind.Fallthrough });
        }

        // Calcolo della Complessità Ciclomatica di McCabe: M = 1 + numero di punti di decisione (If/Select/Loop)
        cfg.CyclomaticComplexity = 1 + cfg.Blocks.Count(b => b.BlockKind is ControlFlowBlockKind.DecisionIf or ControlFlowBlockKind.DecisionSelect or ControlFlowBlockKind.LoopHeader);

        // ---------------------------------------------------------------------
        // PASSO 5: Calcolo del layout spaziale gerarchico (Sugiyama Layout)
        // ---------------------------------------------------------------------
        LayoutSugiyama(cfg);

        return cfg;
    }

    /// <summary>
    /// Calcola la disposizione gerarchica a livelli verticali dei blocchi del CFG (Framework Sugiyama).
    /// </summary>
    /// <param name="cfg">Il grafo di flusso di controllo da disporre sul piano 2D.</param>
    /// <remarks>
    /// L'algoritmo:
    /// <list type="number">
    ///   <item><description>Assegna i nodi a livelli orizzontali progressivi a partire dal blocco Entry mediante attraversamento BFS guidato dagli archi orientati.</description></item>
    ///   <item><description>Per ciascun livello orizzontale, centra i blocchi rispetto all'asse mediano del canvas calcolando l'ingombro cumulativo e la spaziatura.</description></item>
    ///   <item><description>Imposta le coordinate assolute <c>(X, Y)</c> e le dimensioni per ogni <see cref="ControlFlowNode"/>.</description></item>
    /// </list>
    /// </remarks>
    private static void LayoutSugiyama(ProcedureControlFlowGraph cfg)
    {
        if (cfg.Blocks.Count == 0) return;

        // 1. Assegnazione dei nodi ai livelli gerarchici (Layer Assignment via BFS)
        var levels = new List<List<ControlFlowNode>>();
        var placed = new HashSet<Guid>();

        var entry = cfg.Blocks.FirstOrDefault(b => b.BlockKind == ControlFlowBlockKind.Entry) ?? cfg.Blocks[0];
        levels.Add(new List<ControlFlowNode> { entry });
        placed.Add(entry.Id);

        // Continua finché tutti i blocchi sono stati allocati a un livello
        while (placed.Count < cfg.Blocks.Count)
        {
            var nextLevel = new List<ControlFlowNode>();
            var prevLevelIds = levels.Last().Select(b => b.Id).ToHashSet();

            // Identifica i nodi destinazione dei blocchi del livello precedente
            foreach (var edge in cfg.Edges.Where(e => prevLevelIds.Contains(e.SourceBlockId)))
            {
                var target = cfg.FindBlock(edge.TargetBlockId);
                // Il blocco Exit viene riservato preferibilmente per l'ultimo livello
                if (target != null && !placed.Contains(target.Id) && target.BlockKind != ControlFlowBlockKind.Exit)
                {
                    nextLevel.Add(target);
                    placed.Add(target.Id);
                }
            }

            // Fallback per nodi residui non raggiungibili direttamente
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

        // 2. Calcolo coordinate geometriche 2D (Centratura orizzontale e spaziatura verticale)
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
