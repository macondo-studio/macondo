namespace Macondo.Semantic.Model;

/// <summary>
/// Grafo del codice semantico in memoria (CodeGraph).
/// 
/// Rappresenta l'intero modello del codice analizzato: nodi (<see cref="Symbols"/>) ed archi orientati (<see cref="Relationships"/>).
/// È progettato per aggregare molteplici moduli, classi e form di un intero progetto VB6/C# in un'unica struttura
/// in RAM altamente interrogabile, indipendente da qualsiasi database o interfaccia utente.
/// 
/// OTTIMIZZAZIONI DI PERFORMANCE:
/// - Deduplicazione delle relazioni in tempo costante O(1) tramite HashSet di chiavi (Source, Target, Kind).
/// - Ricerca dei simboli per ID in tempo costante O(1) tramite dizionario indicizzato lazy/sincronizzato.
/// - Recupero dei figli gerarchici (CONTAINS) in tempo costante O(1) tramite indice di adiacenza parent-child.
/// </summary>
public sealed class CodeGraph
{
    /// <summary>Elenco completo di tutti i simboli estratti nel progetto.</summary>
    public List<Symbol> Symbols { get; } = new();

    /// <summary>Elenco completo di tutte le relazioni semantiche tra i simboli.</summary>
    public List<Relationship> Relationships { get; } = new();

    // =========================================================================
    // INDICI INTERNI AD ALTE PRESTAZIONI (O(1))
    // =========================================================================

    private readonly HashSet<(Guid SourceId, Guid TargetId, RelationshipKind Kind)> _relationshipKeys = new();
    private readonly Dictionary<Guid, Symbol> _symbolsById = new();
    private readonly Dictionary<Guid, List<Symbol>> _childrenByParentId = new();
    private int _lastIndexedSymbolCount = -1;
    private int _lastIndexedRelCount = -1;

    /// <summary>
    /// Sincronizza gli indici interni dei simboli se la lista <see cref="Symbols"/> è stata modificata direttamente.
    /// </summary>
    private void EnsureSymbolsIndexed()
    {
        if (_symbolsById.Count == Symbols.Count && _lastIndexedSymbolCount == Symbols.Count)
            return;

        _symbolsById.Clear();
        foreach (var sym in Symbols)
        {
            _symbolsById[sym.Id] = sym;
        }
        _lastIndexedSymbolCount = Symbols.Count;
    }

    /// <summary>
    /// Sincronizza l'indice gerarchico di contenimento se le relazioni sono state modificate.
    /// </summary>
    private void EnsureContainsIndexed()
    {
        EnsureSymbolsIndexed();

        if (_lastIndexedRelCount == Relationships.Count)
            return;

        _childrenByParentId.Clear();
        _relationshipKeys.Clear();

        foreach (var rel in Relationships)
        {
            _relationshipKeys.Add((rel.SourceId, rel.TargetId, rel.Kind));

            if (rel.Kind == RelationshipKind.Contains)
            {
                if (!_childrenByParentId.TryGetValue(rel.SourceId, out var list))
                {
                    list = new List<Symbol>();
                    _childrenByParentId[rel.SourceId] = list;
                }

                if (_symbolsById.TryGetValue(rel.TargetId, out var childSym))
                {
                    list.Add(childSym);
                }
            }
        }

        _lastIndexedRelCount = Relationships.Count;
    }

    /// <summary>
    /// Registra internamente una relazione mantenendo sincronizzato l'indice O(1) delle chiavi e del contenimento.
    /// </summary>
    private void RegisterRelationship(Relationship rel)
    {
        Relationships.Add(rel);
        _relationshipKeys.Add((rel.SourceId, rel.TargetId, rel.Kind));
        _lastIndexedRelCount = Relationships.Count;

        if (rel.Kind == RelationshipKind.Contains)
        {
            EnsureSymbolsIndexed();
            if (!_childrenByParentId.TryGetValue(rel.SourceId, out var list))
            {
                list = new List<Symbol>();
                _childrenByParentId[rel.SourceId] = list;
            }

            if (_symbolsById.TryGetValue(rel.TargetId, out var childSym))
            {
                list.Add(childSym);
            }
        }
    }

    /// <summary>
    /// Aggiunge una relazione strutturale gerarchica di contenimento (CONTAINS) tra un parent e un child.
    /// </summary>
    /// <param name="parent">Il simbolo contenitore (es. Modulo o Metodo).</param>
    /// <param name="child">Il simbolo contenuto (es. Metodo o Variabile locale).</param>
    /// <param name="line">Numero di riga nel sorgente.</param>
    public void AddContains(Symbol parent, Symbol child, int? line = null)
    {
        RegisterRelationship(new Relationship
        {
            SourceId = parent.Id,
            TargetId = child.Id,
            Kind = RelationshipKind.Contains,
            Line = line,
            Confidence = ResolutionConfidence.Exact
        });
    }

    /// <summary>
    /// Aggiunge una relazione di chiamata (CALLS) da una routine chiamante a una routine chiamata.
    /// </summary>
    /// <param name="caller">La routine chiamante (Method o Property).</param>
    /// <param name="callee">La routine chiamata (Method o Property).</param>
    /// <param name="line">Numero di riga nel sorgente in cui si origina la chiamata.</param>
    /// <param name="confidence">Grado di certezza della risoluzione (Exact, Inferred, Unknown).</param>
    public void AddCalls(
        Symbol caller,
        Symbol callee,
        int? line = null,
        ResolutionConfidence confidence = ResolutionConfidence.Exact)
    {
        RegisterRelationship(new Relationship
        {
            SourceId = caller.Id,
            TargetId = callee.Id,
            Kind = RelationshipKind.Calls,
            Line = line,
            Confidence = confidence
        });
    }

    /// <summary>
    /// Aggiunge una relazione di istanziazione (INSTANTIATES) da una routine/modulo verso la classe istanziata.
    /// </summary>
    /// <param name="source">La routine o modulo che istanzia l'oggetto.</param>
    /// <param name="targetClass">Il simbolo della classe istanziata.</param>
    /// <param name="line">Numero di riga nel sorgente.</param>
    /// <param name="confidence">Grado di certezza della risoluzione.</param>
    public void AddInstantiates(
        Symbol source,
        Symbol targetClass,
        int? line = null,
        ResolutionConfidence confidence = ResolutionConfidence.Exact)
    {
        RegisterRelationship(new Relationship
        {
            SourceId = source.Id,
            TargetId = targetClass.Id,
            Kind = RelationshipKind.Instantiates,
            Line = line,
            Confidence = confidence
        });
    }

    /// <summary>
    /// Aggiunge una relazione di gestione evento (HANDLES_EVENT) tra la sorgente dell'evento (Form, Controllo o Oggetto) e la routine gestore.
    /// Deduplicazione istantanea O(1).
    /// </summary>
    /// <param name="source">La Form, Controllo o Oggetto che genera l'evento.</param>
    /// <param name="eventHandler">La procedura Sub che gestisce l'evento.</param>
    /// <param name="line">Numero di riga nel sorgente.</param>
    /// <param name="confidence">Grado di certezza della risoluzione.</param>
    public void AddHandlesEvent(
        Symbol source,
        Symbol eventHandler,
        int? line = null,
        ResolutionConfidence confidence = ResolutionConfidence.Exact)
    {
        if (_lastIndexedRelCount != Relationships.Count) EnsureContainsIndexed();

        if (_relationshipKeys.Contains((source.Id, eventHandler.Id, RelationshipKind.HandlesEvent)))
            return;

        RegisterRelationship(new Relationship
        {
            SourceId = source.Id,
            TargetId = eventHandler.Id,
            Kind = RelationshipKind.HandlesEvent,
            Line = line,
            Confidence = confidence
        });
    }

    /// <summary>
    /// Aggiunge una relazione di ereditarietà (INHERITS) da una classe/tipo derivato verso la classe base.
    /// Deduplicazione istantanea O(1).
    /// </summary>
    public void AddInherits(Symbol derivedType, Symbol baseType, int? line = null)
    {
        if (_lastIndexedRelCount != Relationships.Count) EnsureContainsIndexed();

        if (_relationshipKeys.Contains((derivedType.Id, baseType.Id, RelationshipKind.Inherits)))
            return;

        RegisterRelationship(new Relationship
        {
            SourceId = derivedType.Id,
            TargetId = baseType.Id,
            Kind = RelationshipKind.Inherits,
            Line = line,
            Confidence = ResolutionConfidence.Exact
        });
    }

    /// <summary>
    /// Aggiunge una relazione di implementazione di interfaccia (IMPLEMENTS) tra una classe/struttura e l'interfaccia.
    /// Deduplicazione istantanea O(1).
    /// </summary>
    public void AddImplements(Symbol implementingType, Symbol interfaceType, int? line = null)
    {
        if (_lastIndexedRelCount != Relationships.Count) EnsureContainsIndexed();

        if (_relationshipKeys.Contains((implementingType.Id, interfaceType.Id, RelationshipKind.Implements)))
            return;

        RegisterRelationship(new Relationship
        {
            SourceId = implementingType.Id,
            TargetId = interfaceType.Id,
            Kind = RelationshipKind.Implements,
            Line = line,
            Confidence = ResolutionConfidence.Exact
        });
    }

    /// <summary>
    /// Aggiunge una relazione di dipendenza di tipo (USES_TYPE) da un simbolo verso il tipo utilizzato.
    /// Deduplicazione istantanea O(1).
    /// </summary>
    /// <param name="source">Il simbolo che dichiara o usa il tipo (variabile, routine, modulo).</param>
    /// <param name="targetType">Il simbolo del tipo utilizzato (Class, UDT, Enum, Form).</param>
    /// <param name="line">Numero di riga nel sorgente.</param>
    public void AddUsesType(Symbol source, Symbol targetType, int? line = null)
    {
        if (_lastIndexedRelCount != Relationships.Count) EnsureContainsIndexed();

        if (_relationshipKeys.Contains((source.Id, targetType.Id, RelationshipKind.UsesType)))
            return;

        RegisterRelationship(new Relationship
        {
            SourceId = source.Id,
            TargetId = targetType.Id,
            Kind = RelationshipKind.UsesType,
            Line = line,
            Confidence = ResolutionConfidence.Exact
        });
    }

    /// <summary>
    /// Aggiunge una relazione di lettura di stato (READS) da una routine o simbolo verso un campo o proprietà.
    /// Deduplicazione istantanea O(1).
    /// </summary>
    public void AddReads(Symbol source, Symbol targetField, int? line = null)
    {
        if (_lastIndexedRelCount != Relationships.Count) EnsureContainsIndexed();

        if (_relationshipKeys.Contains((source.Id, targetField.Id, RelationshipKind.Reads)))
            return;

        RegisterRelationship(new Relationship
        {
            SourceId = source.Id,
            TargetId = targetField.Id,
            Kind = RelationshipKind.Reads,
            Line = line,
            Confidence = ResolutionConfidence.Exact
        });
    }

    /// <summary>
    /// Aggiunge una relazione di scrittura o assegnazione di stato (WRITES) verso un campo o proprietà.
    /// Deduplicazione istantanea O(1).
    /// </summary>
    public void AddWrites(Symbol source, Symbol targetField, int? line = null)
    {
        if (_lastIndexedRelCount != Relationships.Count) EnsureContainsIndexed();

        if (_relationshipKeys.Contains((source.Id, targetField.Id, RelationshipKind.Writes)))
            return;

        RegisterRelationship(new Relationship
        {
            SourceId = source.Id,
            TargetId = targetField.Id,
            Kind = RelationshipKind.Writes,
            Line = line,
            Confidence = ResolutionConfidence.Exact
        });
    }

    /// <summary>
    /// Aggiunge una relazione di riferimento generico (REFERENCES) tra simboli.
    /// Deduplicazione istantanea O(1).
    /// </summary>
    public void AddReferences(Symbol source, Symbol target, int? line = null)
    {
        if (_lastIndexedRelCount != Relationships.Count) EnsureContainsIndexed();

        if (_relationshipKeys.Contains((source.Id, target.Id, RelationshipKind.References)))
            return;

        RegisterRelationship(new Relationship
        {
            SourceId = source.Id,
            TargetId = target.Id,
            Kind = RelationshipKind.References,
            Line = line,
            Confidence = ResolutionConfidence.Exact
        });
    }

    /// <summary>
    /// Restituisce tutti i simboli figli direttamente contenuti all'interno del simbolo specificato.
    /// Esecuzione istantanea O(1) tramite indice di adiacenza gerarchico.
    /// </summary>
    /// <param name="parentId">ID del simbolo padre.</param>
    public IEnumerable<Symbol> GetChildren(Guid parentId)
    {
        EnsureContainsIndexed();
        return _childrenByParentId.TryGetValue(parentId, out var children) ? children : Enumerable.Empty<Symbol>();
    }

    /// <summary>
    /// Recupera un simbolo dal grafo tramite il suo ID univoco.
    /// Esecuzione istantanea O(1) tramite dizionario indicizzato.
    /// </summary>
    public Symbol? FindSymbolById(Guid id)
    {
        EnsureSymbolsIndexed();
        return _symbolsById.TryGetValue(id, out var symbol) ? symbol : null;
    }
}
