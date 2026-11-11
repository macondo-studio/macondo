namespace Macondo.Semantic.Model;

/// <summary>
/// In-memory semantic code graph (CodeGraph).
/// 
/// It represents the entire model of the analyzed code: nodes (<see cref="Symbols"/>) and oriented arcs (<see cref="Relationships"/>).
/// It is designed to aggregate multiple modules, classes, and forms of an entire VB6/C# project into a single structure
/// in highly queryable RAM, independent of any database or user interface.
/// 
/// PERFORMANCE OPTIMIZATIONS:
/// - O(1) constant time relationship deduplication via HashSet of keys (Source, Target, Kind).
/// - Symbol search by ID in O(1) constant time via lazy/synchronized indexed dictionary.
/// - Retrieval of hierarchical children (CONTAINS) in O(1) constant time via parent-child adjacency index.
/// </summary>
public sealed class CodeGraph
{
    /// <summary>Complete list of all symbols extracted in the project.</summary>
    public List<Symbol> Symbols { get; } = new();

    /// <summary>Complete list of all semantic relationships between symbols.</summary>
    public List<Relationship> Relationships { get; } = new();

    // =========================================================================
    // HIGH PERFORMANCE (O(1)) AND THREAD-SAFE INTERNAL INDEXES
    // =========================================================================

    /// <summary>
    /// Dedicated synchronization object for accessing and rebuilding internal indexes.
    /// Used to protect concurrent mutations on non-threadsafe collections during
    /// parallel analysis processing (Blast Radius, Treemap, Dead Code, Temporal Coupling, etc.).
    /// </summary>
    private readonly object _syncLock = new();

    /// <summary>
    /// Set of tuples (Source, Destination, RelationshipType) for fast O(1) deduplication of relationships.
    /// </summary>
    private HashSet<(Guid SourceId, Guid TargetId, RelationshipKind Kind)> _relationshipKeys = new();

    /// <summary>
    /// Indexed map Symbol ID -> Symbol Object for immediate O(1) searches.
    /// </summary>
    private Dictionary<Guid, Symbol> _symbolsById = new();

    /// <summary>
    /// Indexed map Parent ID -> List of Child Symbols (CONTAINS relationship) for O(1) hierarchical navigation.
    /// </summary>
    private Dictionary<Guid, List<Symbol>> _childrenByParentId = new();

    /// <summary>
    /// Indexed map Source ID -> List of outgoing relations for O(1) query.
    /// </summary>
    private Dictionary<Guid, List<Relationship>> _outgoingBySourceId = new();

    /// <summary>
    /// Indexed map Destination ID -> List of incoming relations for O(1) query.
    /// </summary>
    private Dictionary<Guid, List<Relationship>> _incomingByTargetId = new();

    /// <summary>
    /// Symbol count at last index sync (marked volatile for cross-thread visibility).
    /// </summary>
    private volatile int _lastIndexedSymbolCount = -1;

    /// <summary>
    /// Relationship count as of last index sync (marked volatile for cross-thread visibility).
    /// </summary>
    private volatile int _lastIndexedRelCount = -1;

    /// <summary>
    /// Symbol count when the containment index was last synchronized.
    /// </summary>
    private volatile int _lastIndexedContainsSymbolCount = -1;

    /// <summary>
    /// Synchronize internal symbol indices if the <see cref="Symbols"/> list has been modified or populated.
    /// 
    /// STRATEGIA THREAD-SAFE:
    /// 1. Fast-path lock-free: if the current count matches the last indexed one, it returns instantly.
    /// 2. Double-checked locking: if an update is required, acquire <see cref="_syncLock"/> and check again.
    /// 3. Atomic Replacement: Creates a new isolated local dictionary and replaces the reference of <see cref="_symbolsById"/>
    /// atomically, ensuring that concurrent reader threads do not crash or read mutating collections.
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
    /// Synchronize the hierarchical containment index (CONTAINS relationships), outgoing/incoming adjacency lists and deduplication keys.
    /// 
    /// STRATEGIA THREAD-SAFE:
    /// Constructs the new adjacency maps and the new deduplication set in local isolation,
    /// exchanging references atomically.
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
    /// Explicitly and completely synchronize the graph's internal indices in a single atomic step.
    /// Useful at the end of massive loads or merges to ensure that all future O(1) queries are computed.
    /// </summary>
    public void BuildIndices()
    {
        EnsureContainsIndexed();
    }

    /// <summary>
    /// Combines the symbols and relations of a secondary graph in bulk (e.g. local graph generated by a worker thread)
    /// within the current graph, without triggering repeated O(N^2) recalculations of the containment indices.
    /// </summary>
    /// <param name="other">The local graph to be merged into the primary graph.</param>
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
    /// Internally records a relationship by keeping the O(1) index of keys and containment in sync.
    /// Thread-safe: Serialize incremental adding and updating of structures.
    /// </summary>
    private void RegisterRelationship(Relationship rel)
    {
        lock (_syncLock)
        {
            // If the underlying lists have been populated or altered externally (e.g. AddRange),
            // refresh complete indexes before proceeding with incremental refresh.
            if (_lastIndexedRelCount != Relationships.Count || _lastIndexedContainsSymbolCount != Symbols.Count)
                EnsureContainsIndexed();

            if (!_relationshipKeys.Add((rel.SourceId, rel.TargetId, rel.Kind)))
                return;

            Relationships.Add(rel);
            _lastIndexedRelCount = Relationships.Count;

            // Maintains outgoing and incoming O(1) adjacency in real time
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
    /// Adds a hierarchical structural containment relationship (CONTAINS) between a parent and a child.
    /// </summary>
    /// <param name="parent">The container symbol (e.g. Module or Method).</param>
    /// <param name="child">The contained symbol (e.g. Method or Local variable).</param>
    /// <param name="line">Line number in the source.</param>
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
    /// Adds a calling relationship (CALLS) from a calling routine to a called routine.
    /// </summary>
    /// <param name="caller">The calling routine (Method or Property).</param>
    /// <param name="callee">The called routine (Method or Property).</param>
    /// <param name="line">Line number in the source where the call originates.</param>
    /// <param name="confidence">Degree of resolution certainty (Exact, Inferred, Unknown).</param>
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
    /// Adds an instantiation relationship (INSTANTIATES) from a routine/module to the instantiated class.
    /// </summary>
    /// <param name="source">The routine or module that instantiates the object.</param>
    /// <param name="targetClass">The symbol of the instantiated class.</param>
    /// <param name="line">Line number in the source.</param>
    /// <param name="confidence">Degree of certainty of resolution.</param>
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
    /// Adds an event handler relationship (HANDLES_EVENT) between the event source (Form, Control, or Object) and the handler procedure.
    /// Instant O(1) deduplication and thread-safe.
    /// </summary>
    /// <param name="source">The Form, Control or Object that generates the event.</param>
    /// <param name="eventHandler">The Sub procedure that handles the event.</param>
    /// <param name="line">Line number in the source.</param>
    /// <param name="confidence">Degree of certainty of resolution.</param>
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
    /// Adds an inheritance relationship (INHERITS) from a derived class/type to the base class.
    /// Instant O(1) deduplication and thread-safe.
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
    /// Adds an interface implementation relationship (IMPLEMENTS) between a class/structure and the interface.
    /// Instant O(1) deduplication and thread-safe.
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
    /// Adds a type dependency relationship (USES_TYPE) from a symbol to the type used.
    /// Instant O(1) deduplication and thread-safe.
    /// </summary>
    /// <param name="source">The symbol that declares or uses the type (variable, routine, module).</param>
    /// <param name="targetType">The symbol of the type used (Class, UDT, Enum, Form).</param>
    /// <param name="line">Line number in the source.</param>
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
    /// Adds a read state (READS) relationship from a procedure or symbol to a field or property.
    /// Instant O(1) deduplication and thread-safe.
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
    /// Adds a write or status assignment (WRITES) relationship to a field or property.
    /// Instant O(1) deduplication and thread-safe.
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
    /// Adds a generic reference relationship (REFERENCES) between symbols.
    /// Instant O(1) deduplication and thread-safe.
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
    /// Returns all child symbols directly contained within the specified symbol.
    /// Instant O(1) execution via hierarchical adjacency index. Thread-safe.
    /// </summary>
    /// <param name="parentId">ID of parent symbol.</param>
    public IEnumerable<Symbol> GetChildren(Guid parentId)
    {
        EnsureContainsIndexed();
        var map = _childrenByParentId;
        return map.TryGetValue(parentId, out var children) ? children : Enumerable.Empty<Symbol>();
    }

    /// <summary>
    /// Retrieves a symbol from the graph by its unique ID.
    /// Instant O(1) execution via indexed dictionary. Thread-safe.
    /// </summary>
    public Symbol? FindSymbolById(Guid id)
    {
        EnsureSymbolsIndexed();
        var map = _symbolsById;
        return map.TryGetValue(id, out var symbol) ? symbol : null;
    }

    /// <summary>
    /// Returns all outgoing relations with origin in the specified symbol (SourceId).
    /// Instant O(1) execution via indexed adjacency list. Thread-safe.
    /// </summary>
    public IReadOnlyList<Relationship> GetOutgoingRelationships(Guid sourceId)
    {
        EnsureContainsIndexed();
        var map = _outgoingBySourceId;
        return map.TryGetValue(sourceId, out var rels) ? rels : Array.Empty<Relationship>();
    }

    /// <summary>
    /// Returns all incoming relations with target in the specified symbol (TargetId).
    /// Instant O(1) execution via indexed adjacency list. Thread-safe.
    /// </summary>
    public IReadOnlyList<Relationship> GetIncomingRelationships(Guid targetId)
    {
        EnsureContainsIndexed();
        var map = _incomingByTargetId;
        return map.TryGetValue(targetId, out var rels) ? rels : Array.Empty<Relationship>();
    }
}
