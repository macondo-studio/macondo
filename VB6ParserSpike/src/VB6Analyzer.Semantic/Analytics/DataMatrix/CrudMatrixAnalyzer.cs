using Macondo.Semantic.Model;

namespace Macondo.Semantic.Analytics.DataMatrix;

/// <summary>
/// Analizzatore poliglotto di Data Access e generatore di Matrice CRUD (Create, Read, Update, Delete).
/// Riconosce query SQL, pattern di repository, chiamate a database (ADO, EF Core, Dapper) e relazioni con entità dati.
/// </summary>
public static class CrudMatrixAnalyzer
{
    private static readonly HashSet<SymbolKind> RoutineKinds = new()
    {
        SymbolKind.Method,
        SymbolKind.Property
    };

    /// <summary>
    /// Esegue l'analisi e genera la matrice CRUD a partire dai simboli e relazioni del CodeGraph.
    /// </summary>
    public static CrudMatrixDataset Analyze(CodeGraph graph)
    {
        var dataset = new CrudMatrixDataset();
        if (graph.Symbols.Count == 0)
            return dataset;

        var symbolById = graph.Symbols.ToDictionary(s => s.Id);

        // Mappa ogni simbolo al suo container
        var memberToContainer = new Dictionary<Guid, Symbol>();
        foreach (var sym in graph.Symbols)
        {
            memberToContainer[sym.Id] = sym;
            foreach (var child in graph.GetChildren(sym.Id))
            {
                memberToContainer[child.Id] = sym;
            }
        }

        // Raccoglie le entità dati potenziali (classi di modello/record/struct o tabelle referenziate)
        var entityCandidates = graph.Symbols
            .Where(s => s.Kind is SymbolKind.Record or SymbolKind.Struct or SymbolKind.Type ||
                        (s.Kind == SymbolKind.Class && (s.Name.EndsWith("Entity", StringComparison.OrdinalIgnoreCase) ||
                                                       s.Name.EndsWith("Model", StringComparison.OrdinalIgnoreCase) ||
                                                       s.Name.EndsWith("Dto", StringComparison.OrdinalIgnoreCase))))
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Se non ci sono entità formali esplicite, estrae entità e tabelle dai nomi delle routine e delle classi repository
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

    private static (string? Entity, CrudOperation Operations) InferCrudOperations(Symbol routine, HashSet<string> knownEntities)
    {
        var name = routine.Name;
        var qName = routine.QualifiedName;
        CrudOperation ops = CrudOperation.None;
        string? matchedEntity = null;

        // Cerca matching con entità note
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

        // Inferenza operazioni CRUD dal nome o pattern
        if (name.StartsWith("Create", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Insert", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Add", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("SaveNew", StringComparison.OrdinalIgnoreCase))
        {
            ops |= CrudOperation.Create;
        }

        if (name.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Find", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Load", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Read", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Fetch", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Select", StringComparison.OrdinalIgnoreCase))
        {
            ops |= CrudOperation.Read;
        }

        if (name.StartsWith("Update", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Modify", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Save", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Edit", StringComparison.OrdinalIgnoreCase))
        {
            ops |= CrudOperation.Update;
        }

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
