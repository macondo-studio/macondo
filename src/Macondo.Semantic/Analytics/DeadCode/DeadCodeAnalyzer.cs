using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DeadCode;

/// <summary>
/// Agnostic reachability and dead code analyzer for VB6 and C# projects.
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
    /// Performs the reachability analysis starting from all Entry Points (Sub Main, Event Handlers, etc.)
    /// and recursively expanding the call tree to identify orphaned elements or dead code.
    /// </summary>
    /// <param name="graph">The <see cref="CodeGraph"/> containing project symbols and relationships.</param>
    /// <returns>The <see cref="DeadCodeDataset"/> dataset with the summary of reachable and unreachable symbols.</returns>
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
        // STEP 1: Identifying the application's natural entry points (Root Seeds)
        // Includes Sub Main, startup forms, event handler UI, constructors and main classes.
        // =========================================================================
        foreach (var sym in graph.Symbols)
        {
            if (IsNaturalEntryPoint(sym, graph))
            {
                if (reachableIds.Add(sym.Id))

                    queue.Enqueue(sym.Id);
            }
        }

        // If your application is a class library without explicit Main/Forms,
        // we treat all public symbols (exported external APIs) and interfaces as roots.
        if (reachableIds.Count == 0)
        {
            foreach (var sym in graph.Symbols)
            {
                if (sym.Accessibility == Accessibility.Public || sym.Kind == SymbolKind.Interface)
                {
                    if (reachableIds.Add(sym.Id))

                        queue.Enqueue(sym.Id);
                }
            }
        }

        // =========================================================================
        // STEP 2: Breadth-wide scanning (BFS Traversal) to mark all reachable symbols
        // =========================================================================
        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();

            // 2a. If the node reached is a container, expand the lifecycle members automatically invoked by the runtime
            // and WithEvents variables/controls that handle events
            foreach (var child in graph.GetChildren(currentId))
            {
                if (IsAutoInvokedMember(child.Name))
                {
                    if (reachableIds.Add(child.Id))

                        queue.Enqueue(child.Id);
                }

                // If the child is a WithEvents control or variable with registered handlers, we make it reachable
                if (graph.Relationships.Any(r => r.Kind == RelationshipKind.HandlesEvent && r.SourceId == child.Id))
                {
                    if (reachableIds.Add(child.Id))

                        queue.Enqueue(child.Id);
                }
            }

            // 2b. Expand along outgoing semantic relations (method calls, instantiations, type uses, inheritance, events, reads/writes)
            foreach (var rel in graph.Relationships.Where(r => r.SourceId == currentId))
            {
                if (rel.Kind is RelationshipKind.Calls or RelationshipKind.Instantiates or RelationshipKind.UsesType or RelationshipKind.Inherits or RelationshipKind.Implements or RelationshipKind.HandlesEvent or RelationshipKind.Reads or RelationshipKind.Writes)
                {
                    if (reachableIds.Add(rel.TargetId))

                        queue.Enqueue(rel.TargetId);

                    // If the target is an internal member (e.g. method), make its parent module/class reachable as well
                    var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == rel.TargetId);
                    if (parentRel != null && reachableIds.Add(parentRel.SourceId))

                        queue.Enqueue(parentRel.SourceId);
                }
            }
        }

        dataset.ReachableSymbolsCount = reachableIds.Count;

        // =========================================================================
        // STEP 3: Identification and categorization of the code not reached (Dead Code)
        // =========================================================================
        foreach (var sym in graph.Symbols)
        {
            if (sym.Kind == SymbolKind.Project)
                continue;

            // If the symbol has been marked as reachable, it is not dead code
            if (reachableIds.Contains(sym.Id))
                continue;

            var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == sym.Id);
            var parent = parentRel != null ? graph.FindSymbolById(parentRel.SourceId) : null;
            var containerName = parent?.Name ?? Path.GetFileName(sym.FilePath);
            int estimatedLoc = Math.Max(1, sym.LineEnd - sym.LineStart + 1);

            DeadCodeCategory category;
            string reason;

            // Specific categorization by symbol type
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
        // STEP 4: Sort by volume of estimated lines of code (potential refactoring savings)
        // =========================================================================
        dataset.Items.Sort((a, b) => b.EstimatedLoc.CompareTo(a.EstimatedLoc));
        return dataset;
    }

    /// <summary>
    /// Tests whether a symbol represents a natural entry point for launching or handling application events.
    /// </summary>
    private static bool IsNaturalEntryPoint(Symbol sym, CodeGraph graph)
    {
        // 1. Sub Main or Main() procedure (canonical entry point)
        if (string.Equals(sym.Name, "Main", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sym.Name, "Sub Main", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sym.Name, "Program.Main", StringComparison.OrdinalIgnoreCase))

            return true;

        // 2. UI or runtime event handlers (e.g. cmdSalva_Click, Form_Load)
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

            // Event handlers registered via HandlesEvent relationship (e.g. WithEvents variables or form controls)
            var incomingEvent = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.HandlesEvent && r.TargetId == sym.Id);
            if (incomingEvent != null)
            {
                var parentRel = graph.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Contains && r.TargetId == incomingEvent.SourceId);
                var container = parentRel != null ? graph.FindSymbolById(parentRel.SourceId) : null;
                if (container == null || container.Kind is SymbolKind.Form or SymbolKind.UserControl)
                    return true;
            }
        }

        // 3. Forms, Windows or UserControls (loaded from runtime or project config)
        if (sym.Kind is SymbolKind.Form or SymbolKind.UserControl)

            return true;

        return false;
    }

    /// <summary>
    /// Tests whether a method corresponds to a lifecycle routine automatically invoked by the VB6/COM infrastructure
    /// (e.g. Class_Initialize constructors, Class_Terminate destructors, Form_Load handlers).
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
