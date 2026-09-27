using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DeadCode;

/// <summary>
/// Analizzatore agnostico di raggiungibilità e codice morto per progetti VB6 e C#.
/// </summary>
public static class DeadCodeAnalyzer
{
    private static readonly HashSet<string> StandardEventPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Form_", "UserControl_", "Class_", "MDIForm_", "btn", "cmd", "txt", "chk", "opt", "lst", "cbo", "tmr", "pic", "mnu"
    };

    private static readonly HashSet<string> StandardEventSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "_Click", "_DblClick", "_Change", "_KeyDown", "_KeyUp", "_KeyPress", "_MouseDown", "_MouseUp", "_MouseMove",
        "_Load", "_Unload", "_Initialize", "_Terminate", "_Resize", "_Paint", "_Activate", "_Deactivate", "_Timer",
        "_GotFocus", "_LostFocus", "_Validate", "_Scroll", "_QueryUnload"
    };

    /// <summary>
    /// Esegue l'analisi di raggiungibilità partendo da tutti gli Entry Points (Sub Main, Event Handlers, ecc.)
    /// ed espandendo ricorsivamente l'albero di chiamata per identificare elementi orfani o codice morto.
    /// </summary>
    /// <param name="graph">Il <see cref="CodeGraph"/> contenente simboli e relazioni del progetto.</param>
    /// <returns>Il dataset <see cref="DeadCodeDataset"/> con il riepilogo dei simboli raggiungibili e non raggiungibili.</returns>
    public static DeadCodeDataset Analyze(CodeGraph graph)
    {
        var dataset = new DeadCodeDataset
        {
            TotalSymbolsAnalyzed = graph.Symbols.Count(s => s.Kind != SymbolKind.Project)
        };

        if (graph.Symbols.Count == 0)
            return dataset;

        var reachableIds = new HashSet<Guid>();
        var queue = new Queue<Guid>();

        // =========================================================================
        // PASSO 1: Identificazione dei punti di ingresso naturali dell'applicazione (Root Seeds)
        // Include Sub Main, startup forms, event handler UI, costruttori e classi principali.
        // =========================================================================
        foreach (var sym in graph.Symbols)
        {
            if (IsNaturalEntryPoint(sym, graph))
            {
                if (reachableIds.Add(sym.Id))
                {
                    queue.Enqueue(sym.Id);
                }
            }
        }

        // Se l'applicazione è una libreria di classi senza Main/Form espliciti,
        // trattiamo come radici tutti i simboli pubblici (API esterne esportate) e le interfacce.
        if (reachableIds.Count == 0)
        {
            foreach (var sym in graph.Symbols)
            {
                if (sym.Accessibility == Accessibility.Public || sym.Kind == SymbolKind.Interface)
                {
                    if (reachableIds.Add(sym.Id))
                    {
                        queue.Enqueue(sym.Id);
                    }
                }
            }
        }

        // =========================================================================
        // PASSO 2: Visita in ampiezza (BFS Traversal) per marcare tutti i simboli raggiungibili
        // =========================================================================
        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();

            // 2a. Se il nodo raggiunto è un contenitore, espandi i membri del ciclo di vita invocati automaticamente dal runtime
            foreach (var child in graph.GetChildren(currentId))
            {
                if (IsAutoInvokedMember(child.Name))
                {
                    if (reachableIds.Add(child.Id))
                    {
                        queue.Enqueue(child.Id);
                    }
                }
            }

            // 2b. Espandi lungo le relazioni semantiche uscenti (chiamate a metodi, istanziazioni, usi di tipo, ereditarietà)
            foreach (var rel in graph.Relationships.Where(r => r.SourceId == currentId))
            {
                if (rel.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.UsesType or RelationshipKind.Inherits or RelationshipKind.Implements)
                {
                    if (reachableIds.Add(rel.TargetId))
                    {
                        queue.Enqueue(rel.TargetId);
                    }

                    // Se il target è un membro interno (es. metodo), rendi raggiungibile anche il suo modulo/classe padre
                    var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == rel.TargetId);
                    if (parentRel != null && reachableIds.Add(parentRel.SourceId))
                    {
                        queue.Enqueue(parentRel.SourceId);
                    }
                }
            }
        }

        dataset.ReachableSymbolsCount = reachableIds.Count;

        // =========================================================================
        // PASSO 3: Identificazione e categorizzazione del codice non raggiunto (Dead Code)
        // =========================================================================
        foreach (var sym in graph.Symbols)
        {
            if (sym.Kind == SymbolKind.Project)
                continue;

            // Se il simbolo è stato marcato come raggiungibile, non è codice morto
            if (reachableIds.Contains(sym.Id))
                continue;

            var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == sym.Id);
            var parent = parentRel != null ? graph.FindSymbolById(parentRel.SourceId) : null;
            var containerName = parent?.Name ?? Path.GetFileName(sym.FilePath);
            int estimatedLoc = Math.Max(1, sym.LineEnd - sym.LineStart + 1);

            DeadCodeCategory category;
            string reason;

            // Categorizzazione specifica per tipo di simbolo
            switch (sym.Kind)
            {
                case SymbolKind.Method:
                case SymbolKind.Property:
                    category = DeadCodeCategory.UnreachableRoutine;
                    reason = "No direct or indirect calls detected from any application entry point.";
                    break;

                case SymbolKind.Field:
                case SymbolKind.Constant:
                    category = DeadCodeCategory.UnusedFieldOrConstant;
                    reason = "Variable or constant declared but never referenced or read.";
                    break;

                case SymbolKind.Class:
                case SymbolKind.Module:
                case SymbolKind.Form:
                case SymbolKind.UserControl:
                case SymbolKind.Record:
                case SymbolKind.Struct:
                    category = DeadCodeCategory.OrphanContainer;
                    reason = "Component never instantiated, referenced, or loaded in the program flow.";
                    break;

                case SymbolKind.Type:
                case SymbolKind.Enum:
                    category = DeadCodeCategory.UnusedType;
                    reason = "Type or Enum defined but never used as a field, parameter, or variable type.";
                    break;

                default:
                    category = DeadCodeCategory.UnreachableRoutine;
                    reason = "Unreachable symbol.";
                    break;
            }

            dataset.Items.Add(new DeadCodeItem
            {
                Id = sym.Id,
                Name = sym.Name,
                QualifiedName = sym.QualifiedName,
                ContainerName = containerName,
                FilePath = sym.FilePath,
                LineStart = sym.LineStart,
                LineEnd = sym.LineEnd,
                Kind = sym.Kind,
                Category = category,
                EstimatedLoc = estimatedLoc,
                Reason = reason,
                IsPublic = sym.Accessibility == Accessibility.Public || sym.Accessibility == Accessibility.Global
            });
        }

        // =========================================================================
        // PASSO 4: Ordinamento per volume di righe di codice stimate (potenziale risparmio refactoring)
        // =========================================================================
        dataset.Items.Sort((a, b) => b.EstimatedLoc.CompareTo(a.EstimatedLoc));
        return dataset;
    }

    /// <summary>
    /// Verifica se un simbolo rappresenta un entry point naturale per l'avvio o la gestione degli eventi dell'applicazione.
    /// </summary>
    private static bool IsNaturalEntryPoint(Symbol sym, CodeGraph graph)
    {
        // 1. Procedura Sub Main o Main() (punto di ingresso canonico)
        if (string.Equals(sym.Name, "Main", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sym.Name, "Sub Main", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sym.Name, "Program.Main", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 2. Gestori di eventi UI o del runtime (es. cmdSalva_Click, Form_Load)
        if (sym.Kind is SymbolKind.Method or SymbolKind.Property)
        {
            var name = sym.Name;
            foreach (var suffix in StandardEventSuffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            foreach (var prefix in StandardEventPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        // 3. Form, Finestre o UserControl (caricati dal runtime o da config di progetto)
        if (sym.Kind is SymbolKind.Form or SymbolKind.UserControl)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Verifica se un metodo corrisponde a una routine del ciclo di vita invocata automaticamente dall'infrastruttura VB6/COM
    /// (es. costruttori Class_Initialize, distruttori Class_Terminate, handler Form_Load).
    /// </summary>
    private static bool IsAutoInvokedMember(string name)
    {
        return name.Equals("Class_Initialize", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Class_Terminate", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("UserControl_Initialize", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Form_Initialize", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Form_Load", StringComparison.OrdinalIgnoreCase);
    }
}
