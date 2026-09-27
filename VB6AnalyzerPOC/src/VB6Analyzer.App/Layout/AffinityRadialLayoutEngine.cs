using Macondo.App.Models;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Motore di layout radiale per affinità semantica.
/// Mantiene la forma circolare pulita, ma ordina angolarmente i container lungo la circonferenza
/// in base al peso delle interazioni reciproche (Calls, Instantiates, UsesType) per minimizzare
/// i cavi di connessione che attraversano il centro del grafo.
/// </summary>
public sealed class AffinityRadialLayoutEngine : IGraphLayoutEngine
{
    public static readonly AffinityRadialLayoutEngine Instance = new();

    void IGraphLayoutEngine.Apply(VisualCodeGraph graph) => Apply(graph);

    /// <summary>
    /// Calcola e applica le coordinate (X, Y) a tutti i nodi del grafo visuale disponendo i moduli su una circonferenza
    /// ordinata per affinità di interazione reciproca (minimizzazione degli incroci centrali).
    /// </summary>
    /// <param name="graph">Il grafo visuale da posizionare.</param>
    /// <remarks>
    /// L'algoritmo:
    /// <list type="number">
    ///   <item><description>Calcola la matrice di affinità pesata tra coppie di container basandosi su chiamate (<c>3.0</c>), istanziazioni (<c>2.5</c>) e tipi usati (<c>1.5</c>).</description></item>
    ///   <item><description>Determina l'ordine angolare ottimale risolvendo un cammino greedy (Nearest Neighbor / TSP euristico) per posizionare moduli fortemente accoppiati in posizioni angolari contigue.</description></item>
    ///   <item><description>Posiziona i container lungo la circonferenza principale e distribuisce i membri interni su orbite satellitari attorno a ciascun modulo.</description></item>
    /// </list>
    /// </remarks>
    public static void Apply(VisualCodeGraph graph)
    {
        // 1. Recupero dei progetti presenti nel grafo visuale
        var projects = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        if (projects.Count == 0) return;

        var isMultiProject = projects.Count > 1;
        var projectMacroRadius = Math.Max(1200.0, projects.Count * 700.0);

        // 2. Elaborazione di ciascun progetto in modo indipendente
        for (var pIdx = 0; pIdx < projects.Count; pIdx++)
        {
            var project = projects[pIdx];
            if (isMultiProject)
            {
                // Dispone i diversi progetti su una macro-circonferenza
                var projAngle = 2 * Math.PI * pIdx / projects.Count;
                project.X = Math.Cos(projAngle) * projectMacroRadius;
                project.Y = Math.Sin(projAngle) * projectMacroRadius;
            }
            else
            {
                // Progetto singolo posizionato al centro assoluto (0, 0)
                project.X = 0;
                project.Y = 0;
            }

            var containers = graph.GetChildren(project.Id).ToList();
            if (containers.Count == 0) continue;

            // =========================================================================
            // PASSO 1: Mappa di appartenenza nodo -> container genitore
            // Permette di ricondurre qualsiasi arco che parte/arriva da un membro interno
            // (es. un metodo o una proprietà) al modulo o classe container che lo racchiude.
            // =========================================================================
            var nodeToContainerMap = new Dictionary<Guid, Guid>();
            foreach (var c in containers)
            {
                nodeToContainerMap[c.Id] = c.Id;
                foreach (var m in graph.GetChildren(c.Id))
                {
                    nodeToContainerMap[m.Id] = c.Id;
                }
            }

            // =========================================================================
            // PASSO 2: Calcolo della matrice di affinità pesata tra coppie di container
            // Assegna pesi differenziati alla semantica delle relazioni per riflettere
            // l'intensità dell'accoppiamento strutturale:
            // - Calls (3.0): accoppiamento comportamentale diretto
            // - Instantiates (2.5): dipendenza di creazione
            // - UsesType (1.5): dipendenza da tipo di dato
            // =========================================================================
            var affinity = new Dictionary<(Guid, Guid), double>();
            void AddAffinity(Guid c1, Guid c2, double weight)
            {
                if (c1 == c2) return; // Ignora le auto-relazioni interne allo stesso modulo
                var key = c1.CompareTo(c2) < 0 ? (c1, c2) : (c2, c1); // Chiave canonica simmetrica
                affinity[key] = affinity.GetValueOrDefault(key) + weight;
            }

            foreach (var edge in graph.Edges)
            {
                if (nodeToContainerMap.TryGetValue(edge.SourceId, out var srcCont) &&
                    nodeToContainerMap.TryGetValue(edge.TargetId, out var tgtCont))
                {
                    var weight = edge.Kind switch
                    {
                        RelationshipKind.Calls => 3.0,
                        RelationshipKind.Instantiates => 2.5,
                        RelationshipKind.UsesType => 1.5,
                        _ => 1.0
                    };
                    AddAffinity(srcCont, tgtCont, weight);
                }
            }

            // Calcola il punteggio di affinità totale per ciascun container
            // sommando i pesi di tutte le sue connessioni con altri moduli
            var totalAffinityByContainer = containers.ToDictionary(
                c => c.Id,
                c => affinity.Where(kv => kv.Key.Item1 == c.Id || kv.Key.Item2 == c.Id).Sum(kv => kv.Value)
            );

            // =========================================================================
            // PASSO 3: Ordinamento greedy per contiguità di affinità (Nearest Neighbor / TSP euristico)
            // L'obiettivo è posizionare moduli che interagiscono frequentemente in slot angolari
            // adiacenti lungo la circonferenza, riducendo drasticamente i cavi che attraversano il centro.
            // =========================================================================
            var unvisited = new HashSet<Guid>(containers.Select(c => c.Id));
            var orderedContainers = new List<GraphNode>();
            var containerById = containers.ToDictionary(c => c.Id);

            while (unvisited.Count > 0)
            {
                // Inizia un nuovo cluster o catena dal container non visitato con la massima affinità complessiva
                var currentId = unvisited.OrderByDescending(id => totalAffinityByContainer.GetValueOrDefault(id)).First();
                unvisited.Remove(currentId);
                orderedContainers.Add(containerById[currentId]);

                // Percorre la catena collegando di volta in volta il vicino non visitato con la massima affinità
                while (unvisited.Count > 0)
                {
                    var nextNeighbor = unvisited
                        .Select(candId =>
                        {
                            var key = currentId.CompareTo(candId) < 0 ? (currentId, candId) : (candId, currentId);
                            var aff = affinity.GetValueOrDefault(key, 0.0);
                            return new { Id = candId, Affinity = aff };
                        })
                        .Where(x => x.Affinity > 0)
                        .OrderByDescending(x => x.Affinity)
                        .FirstOrDefault();

                    // Se nessun nodo rimasto ha affinità diretta con il nodo corrente, interrompe il sotto-cammino
                    if (nextNeighbor == null)
                        break;

                    currentId = nextNeighbor.Id;
                    unvisited.Remove(currentId);
                    orderedContainers.Add(containerById[currentId]);
                }
            }

            // =========================================================================
            // PASSO 4: Posizionamento geometrico circolare con la sequenza ottimizzata
            // =========================================================================
            var containerCount = orderedContainers.Count;
            var containerRingRadius = Math.Max(380.0, containerCount * 35.0);

            for (var i = 0; i < containerCount; i++)
            {
                var angle = 2 * Math.PI * i / containerCount;
                var container = orderedContainers[i];
                container.X = project.X + Math.Cos(angle) * containerRingRadius;
                container.Y = project.Y + Math.Sin(angle) * containerRingRadius;

                // Dispone i membri figli (routine, proprietà, campi) su orbite satellitari attorno al container
                var members = graph.GetChildren(container.Id).ToList();
                var memberCount = Math.Max(members.Count, 1);
                var memberRingRadius = Math.Max(85.0, memberCount * 14.0);

                for (var j = 0; j < members.Count; j++)
                {
                    var memberAngle = 2 * Math.PI * j / memberCount;
                    var member = members[j];
                    member.X = container.X + Math.Cos(memberAngle) * memberRingRadius;
                    member.Y = container.Y + Math.Sin(memberAngle) * memberRingRadius;
                }
            }
        }
    }
}
