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
    // INDICI INTERNI AD ALTE PRESTAZIONI (O(1)) E THREAD-SAFE
    // =========================================================================

    /// <summary>
    /// Oggetto di sincronizzazione dedicato per l'accesso e la ricostruzione degli indici interni.
    /// Utilizzato per proteggere le mutazioni concorrenti su collezioni non thread-safe durante
    /// le elaborazioni parallele di analisi (Blast Radius, Treemap, Dead Code, Temporal Coupling, ecc.).
    /// </summary>
    private readonly object _syncLock = new();

    /// <summary>
    /// Insieme di tuple (Sorgente, Destinazione, TipoRelazione) per la deduplicazione O(1) rapida delle relazioni.
    /// </summary>
    private HashSet<(Guid SourceId, Guid TargetId, RelationshipKind Kind)> _relationshipKeys = new();

    /// <summary>
    /// Mappa indicizzata ID Simbolo -> Oggetto Simbolo per ricerche O(1) immediate.
    /// </summary>
    private Dictionary<Guid, Symbol> _symbolsById = new();

    /// <summary>
    /// Mappa indicizzata ID Padre -> Lista dei Simboli Figli (relazione CONTAINS) per navigazione gerarchica O(1).
    /// </summary>
    private Dictionary<Guid, List<Symbol>> _childrenByParentId = new();

    /// <summary>
    /// Mappa indicizzata ID Sorgente -> Lista delle relazioni uscenti per query O(1).
    /// </summary>
    private Dictionary<Guid, List<Relationship>> _outgoingBySourceId = new();

    /// <summary>
    /// Mappa indicizzata ID Destinazione -> Lista delle relazioni entranti per query O(1).
    /// </summary>
    private Dictionary<Guid, List<Relationship>> _incomingByTargetId = new();

    /// <summary>
    /// Conteggio dei simboli all'ultima sincronizzazione dell'indice (marcato volatile per visibilità cross-thread).
    /// </summary>
    private volatile int _lastIndexedSymbolCount = -1;

    /// <summary>
    /// Conteggio delle relazioni all'ultima sincronizzazione dell'indice (marcato volatile per visibilità cross-thread).
    /// </summary>
    private volatile int _lastIndexedRelCount = -1;

    /// <summary>
    /// Conteggio dei simboli all'ultima sincronizzazione dell'indice di contenimento.
    /// </summary>
    private volatile int _lastIndexedContainsSymbolCount = -1;

    /// <summary>
    /// Sincronizza gli indici interni dei simboli se la lista <see cref="Symbols"/> è stata modificata o popolata.
    /// 
    /// STRATEGIA THREAD-SAFE:
    /// 1. Fast-path lock-free: se il conteggio corrente coincide con l'ultimo indicizzato, ritorna istantaneamente.
    /// 2. Double-checked locking: se un aggiornamento è richiesto, acquisisce <see cref="_syncLock"/> e ricontrolla.
    /// 3. Sostituzione atomica: crea un nuovo dizionario locale isolato e sostituisce il riferimento di <see cref="_symbolsById"/>
    ///    in modo atomico, garantendo che i thread lettori concorrenti non subiscano blocchi né leggano collezioni in corso di mutazione.
    /// </summary>
    private void EnsureSymbolsIndexed()
    {
        if (_lastIndexedSymbolCount == Symbols.Count)
            return;

        lock (_syncLock)
        {
            if (_lastIndexedSymbolCount == Symbols.Count)
                return;

            var newSymbolsById = new Dictionary<Guid, Symbol>(Symbols.Count);
            foreach (var sym in Symbols)
            {
                newSymbolsById[sym.Id] = sym;
            }

            _symbolsById = newSymbolsById;
            _lastIndexedSymbolCount = Symbols.Count;
        }
    }

    /// <summary>
    /// Sincronizza l'indice gerarchico di contenimento (relazioni CONTAINS), le liste di adiacenza uscenti/entranti e le chiavi di deduplicazione.
    /// 
    /// STRATEGIA THREAD-SAFE:
    /// Costruisce le nuove mappe di adiacenza e il nuovo set di deduplicazione in isolamento locale,
    /// scambiando i riferimenti atomicamente.
    /// </summary>
    private void EnsureContainsIndexed()
    {
        EnsureSymbolsIndexed();

        if (_lastIndexedRelCount == Relationships.Count && _lastIndexedContainsSymbolCount == Symbols.Count)
            return;

        lock (_syncLock)
        {
            if (_lastIndexedRelCount == Relationships.Count && _lastIndexedContainsSymbolCount == Symbols.Count)
                return;

            var newKeys = new HashSet<(Guid SourceId, Guid TargetId, RelationshipKind Kind)>(Relationships.Count);
            var newChildren = new Dictionary<Guid, List<Symbol>>();
            var newOutgoing = new Dictionary<Guid, List<Relationship>>();
            var newIncoming = new Dictionary<Guid, List<Relationship>>();
            var currentSymbols = _symbolsById;

            foreach (var rel in Relationships)
            {
                newKeys.Add((rel.SourceId, rel.TargetId, rel.Kind));

                // Adiacenza Uscente (Source -> Relationships)
                if (!newOutgoing.TryGetValue(rel.SourceId, out var outList))
                {
                    outList = new List<Relationship>();
                    newOutgoing[rel.SourceId] = outList;
                }
                outList.Add(rel);

                // Adiacenza Entrante (Target -> Relationships)
                if (!newIncoming.TryGetValue(rel.TargetId, out var inList))
                {
                    inList = new List<Relationship>();
                    newIncoming[rel.TargetId] = inList;
                }
                inList.Add(rel);

                if (rel.Kind == RelationshipKind.Contains)
                {
                    if (!newChildren.TryGetValue(rel.SourceId, out var list))
                    {
                        list = new List<Symbol>();
                        newChildren[rel.SourceId] = list;
                    }

                    if (currentSymbols.TryGetValue(rel.TargetId, out var childSym))
                        list.Add(childSym);
                }
            }

            _relationshipKeys = newKeys;
            _childrenByParentId = newChildren;
            _outgoingBySourceId = newOutgoing;
            _incomingByTargetId = newIncoming;
            _lastIndexedRelCount = Relationships.Count;
            _lastIndexedContainsSymbolCount = Symbols.Count;
        }
    }

    /// <summary>
    /// Sincronizza esplicitamente e completamente gli indici interni del grafo in un unico passaggio atomico.
    /// Utile al termine di caricamenti o merge massivi per garantire che tutte le future query O(1) siano calcolate.
    /// </summary>
    public void BuildIndices()
    {
        EnsureContainsIndexed();
    }

    /// <summary>
    /// Unisce in blocco i simboli e le relazioni di un grafo secondario (es. grafo locale generato da un worker thread)
    /// all'interno del grafo corrente, senza innescare ricalcoli ripetuti O(N^2) degli indici di contenimento.
    /// </summary>
    /// <param name="other">Il grafo locale da unire nel grafo primario.</param>
    public void MergeFrom(CodeGraph other)
    {
        if (other == null || (other.Symbols.Count == 0 && other.Relationships.Count == 0))
            return;

        lock (_syncLock)
        {
            Symbols.AddRange(other.Symbols);
            Relationships.AddRange(other.Relationships);
        }
    }

    /// <summary>
    /// Registra internamente una relazione mantenendo sincronizzato l'indice O(1) delle chiavi e del contenimento.
    /// Thread-safe: serializza l'aggiunta e l'aggiornamento incrementale delle strutture.
    /// </summary>
    private void RegisterRelationship(Relationship rel)
    {
        lock (_syncLock)
        {
            // Se le liste sottostanti sono state popolate o alterate esternamente (es. AddRange),
            // rinfresca gli indici completi prima di procedere con l'aggiornamento incrementale.
            if (_lastIndexedRelCount != Relationships.Count || _lastIndexedContainsSymbolCount != Symbols.Count)
                EnsureContainsIndexed();

            if (!_relationshipKeys.Add((rel.SourceId, rel.TargetId, rel.Kind)))
                return;

            Relationships.Add(rel);
            _lastIndexedRelCount = Relationships.Count;

            // Mantiene l'adiacenza uscente ed entrante O(1) in tempo reale
            if (!_outgoingBySourceId.TryGetValue(rel.SourceId, out var outList))
            {
                outList = new List<Relationship>();
                _outgoingBySourceId[rel.SourceId] = outList;
            }
            outList.Add(rel);

            if (!_incomingByTargetId.TryGetValue(rel.TargetId, out var inList))
            {
                inList = new List<Relationship>();
                _incomingByTargetId[rel.TargetId] = inList;
            }
            inList.Add(rel);

            if (rel.Kind == RelationshipKind.Contains)
            {
                EnsureSymbolsIndexed();
                if (!_childrenByParentId.TryGetValue(rel.SourceId, out var list))
                {
                    list = new List<Symbol>();
                    _childrenByParentId[rel.SourceId] = list;
                }

                if (_symbolsById.TryGetValue(rel.TargetId, out var childSym))
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
    /// Deduplicazione istantanea O(1) e thread-safe.
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
    /// Deduplicazione istantanea O(1) e thread-safe.
    /// </summary>
    public void AddInherits(Symbol derivedType, Symbol baseType, int? line = null)
    {
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
    /// Deduplicazione istantanea O(1) e thread-safe.
    /// </summary>
    public void AddImplements(Symbol implementingType, Symbol interfaceType, int? line = null)
    {
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
    /// Deduplicazione istantanea O(1) e thread-safe.
    /// </summary>
    /// <param name="source">Il simbolo che dichiara o usa il tipo (variabile, routine, modulo).</param>
    /// <param name="targetType">Il simbolo del tipo utilizzato (Class, UDT, Enum, Form).</param>
    /// <param name="line">Numero di riga nel sorgente.</param>
    public void AddUsesType(Symbol source, Symbol targetType, int? line = null)
    {
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
    /// Deduplicazione istantanea O(1) e thread-safe.
    /// </summary>
    public void AddReads(Symbol source, Symbol targetField, int? line = null)
    {
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
    /// Deduplicazione istantanea O(1) e thread-safe.
    /// </summary>
    public void AddWrites(Symbol source, Symbol targetField, int? line = null)
    {
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
    /// Deduplicazione istantanea O(1) e thread-safe.
    /// </summary>
    public void AddReferences(Symbol source, Symbol target, int? line = null)
    {
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
    /// Esecuzione istantanea O(1) tramite indice di adiacenza gerarchico. Thread-safe.
    /// </summary>
    /// <param name="parentId">ID del simbolo padre.</param>
    public IEnumerable<Symbol> GetChildren(Guid parentId)
    {
        EnsureContainsIndexed();
        var map = _childrenByParentId;
        return map.TryGetValue(parentId, out var children) ? children : Enumerable.Empty<Symbol>();
    }

    /// <summary>
    /// Recupera un simbolo dal grafo tramite il suo ID univoco.
    /// Esecuzione istantanea O(1) tramite dizionario indicizzato. Thread-safe.
    /// </summary>
    public Symbol? FindSymbolById(Guid id)
    {
        EnsureSymbolsIndexed();
        var map = _symbolsById;
        return map.TryGetValue(id, out var symbol) ? symbol : null;
    }

    /// <summary>
    /// Restituisce tutte le relazioni uscenti con origine nel simbolo specificato (SourceId).
    /// Esecuzione istantanea O(1) tramite lista di adiacenza indicizzata. Thread-safe.
    /// </summary>
    public IReadOnlyList<Relationship> GetOutgoingRelationships(Guid sourceId)
    {
        EnsureContainsIndexed();
        var map = _outgoingBySourceId;
        return map.TryGetValue(sourceId, out var rels) ? rels : Array.Empty<Relationship>();
    }

    /// <summary>
    /// Restituisce tutte le relazioni entranti con destinazione nel simbolo specificato (TargetId).
    /// Esecuzione istantanea O(1) tramite lista di adiacenza indicizzata. Thread-safe.
    /// </summary>
    public IReadOnlyList<Relationship> GetIncomingRelationships(Guid targetId)
    {
        EnsureContainsIndexed();
        var map = _incomingByTargetId;
        return map.TryGetValue(targetId, out var rels) ? rels : Array.Empty<Relationship>();
    }
}
