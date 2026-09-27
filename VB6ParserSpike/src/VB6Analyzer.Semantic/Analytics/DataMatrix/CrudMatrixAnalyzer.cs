using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DataMatrix;

/// <summary>
/// Analizzatore poliglotto di Data Access e generatore della Matrice CRUD (Create, Read, Update, Delete).
/// 
/// RESPONSABILITÀ:
/// 1. Individuazione Entità Dati: individua modelli, record, struct, UDT e classi entità del dominio
///    (es. "Customer", "Order", "Product", "Invoice").
/// 2. Inferenza Operazioni CRUD: esamina routine e metodi che accedono a tali entità, deducendo le operazioni
///    (Create, Read, Update, Delete) tramite convenzioni di naming, verbi SQL e pattern di accesso dati.
/// 3. Generazione Matrice di Accesso: associa ciascuna entità ai componenti o moduli che la manipolano.
/// </summary>
public static class CrudMatrixAnalyzer
{
    private static readonly HashSet<SymbolKind> RoutineKinds = new()
    {
        SymbolKind.Method,
        SymbolKind.Property
    };

    /// <summary>
    /// Esegue l'analisi e genera la matrice CRUD a partire dai simboli e dalle relazioni del CodeGraph.
    /// </summary>
    /// <param name="graph">Il CodeGraph contenente simboli e relazioni del progetto.</param>
    /// <returns>Il dataset <see cref="CrudMatrixDataset"/> con tutte le celle e i riepiloghi per entità.</returns>
    public static CrudMatrixDataset Analyze(CodeGraph graph)
    {
        var dataset = new CrudMatrixDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        var symbolById = graph.Symbols.ToDictionary(s => s.Id);

        // Mappa ogni simbolo membro (metodo o proprietà) al suo rispettivo contenitore radice (Classe, Form o Modulo)
        var memberToContainer = new Dictionary<Guid, Symbol>();
        foreach (var sym in graph.Symbols)
        {
            memberToContainer[sym.Id] = sym;
            foreach (var child in graph.GetChildren(sym.Id))
            {
                memberToContainer[child.Id] = sym;
            }
        }

        // 1. Raccoglie le entità dati potenziali (classi di modello, record, struct, Type UDT)
        var entityCandidates = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Record or SymbolKind.Struct or SymbolKind.Type ||
                        (s.Kind == SymbolKind.Class && (s.Name.EndsWith("Entity", StringComparison.OrdinalIgnoreCase) ||
                                                       s.Name.EndsWith("Model", StringComparison.OrdinalIgnoreCase) ||
                                                       s.Name.EndsWith("Dto", StringComparison.OrdinalIgnoreCase))))
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Se non ci sono entità formali esplicite nel modello, applica euristica sui nomi di entità enterprise comuni
        if (entityCandidates.Count == 0)
        {
            foreach (var sym in graph.Symbols)
            {
                if (sym.Name.Contains("Customer", StringComparison.OrdinalIgnoreCase)) entityCandidates.Add("Customers");
                else if (sym.Name.Contains("Order", StringComparison.OrdinalIgnoreCase)) entityCandidates.Add("Orders");
                else if (sym.Name.Contains("Product", StringComparison.OrdinalIgnoreCase)) entityCandidates.Add("Products");
                else if (sym.Name.Contains("User", StringComparison.OrdinalIgnoreCase)) entityCandidates.Add("Users");
                else if (sym.Name.Contains("Invoice", StringComparison.OrdinalIgnoreCase)) entityCandidates.Add("Invoices");
            }
        }

        var entitySummaries = new Dictionary<string, EntityCrudSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entityCandidates)
        {
            entitySummaries[entity] = new EntityCrudSummary { EntityName = entity };
        }

        var routines = graph.Symbols.Where(s => RoutineKinds.Contains(s.Kind)).ToList();

        // 2. Ispeziona ciascuna routine per rilevare le operazioni CRUD effettuate sulle entità
        foreach (var routine in routines)
        {
            memberToContainer.TryGetValue(routine.Id, out var container);
            var (matchedEntity, ops) = InferCrudOperations(routine, entityCandidates);

            if (!string.IsNullOrEmpty(matchedEntity) && ops != CrudOperation.None)
            {
                var cell = new CrudCell
                {
                    EntityName = matchedEntity,
                    RoutineSymbol = routine,
                    ContainerSymbol = container,
                    Operations = ops
                };

                dataset.AllCells.Add(cell);

                if (!entitySummaries.TryGetValue(matchedEntity, out var summary))
                {
                    summary = new EntityCrudSummary { EntityName = matchedEntity };
                    entitySummaries[matchedEntity] = summary;
                }
                summary.Accessors.Add(cell);
            }
        }

        dataset.Entities = entitySummaries.Keys.OrderBy(k => k).ToList();
        dataset.EntitySummaries = entitySummaries.Values.OrderBy(v => v.EntityName).ToList();

        return dataset;
    }

    /// <summary>
    /// Deduce l'entità bersaglio e le operazioni CRUD (Create, Read, Update, Delete)
    /// eseguite da una routine tramite analisi lessicale e pattern matching sui verbi di azione.
    /// </summary>
    private static (string? Entity, CrudOperation Operations) InferCrudOperations(Symbol routine, HashSet<string> knownEntities)
    {
        var name = routine.Name;
        var qName = routine.QualifiedName;
        CrudOperation ops = CrudOperation.None;
        string? matchedEntity = null;

        // Cerca matching con entità note (sia in forma plurale che singolare)
        foreach (var entity in knownEntities)
        {
            var singular = entity.TrimEnd('s');
            if (name.Contains(entity, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(singular, StringComparison.OrdinalIgnoreCase) ||
                qName.Contains(entity, StringComparison.OrdinalIgnoreCase))
            {
                matchedEntity = entity;
                break;
            }
        }

        // Inferenza operazioni CRUD dai prefissi e verbi standard
        // CREATE: Create, Insert, Add, SaveNew
        if (name.StartsWith("Create", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Insert", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Add", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("SaveNew", StringComparison.OrdinalIgnoreCase))
        {
            ops |= CrudOperation.Create;
        }

        // READ: Get, Find, Load, Read, Fetch, Select
        if (name.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Find", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Load", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Read", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Fetch", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Select", StringComparison.OrdinalIgnoreCase))
        {
            ops |= CrudOperation.Read;
        }

        // UPDATE: Update, Modify, Save, Edit
        if (name.StartsWith("Update", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Modify", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Save", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Edit", StringComparison.OrdinalIgnoreCase))
        {
            ops |= CrudOperation.Update;
        }

        // DELETE: Delete, Remove, Purge, Drop
        if (name.StartsWith("Delete", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Remove", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Purge", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Drop", StringComparison.OrdinalIgnoreCase))
        {
            ops |= CrudOperation.Delete;
        }

        return (matchedEntity, ops);
    }
}

