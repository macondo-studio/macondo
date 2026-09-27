using Macondo.App.Models;
using Macondo.Semantic.Model;

namespace Macondo.App.Layout;

/// <summary>
/// Motore di layout Force-Directed basato su modello fisico (Fruchterman-Reingold).
/// Applica forze repulsive tra tutti i container per evitare sovrapposizioni e forze attrattive elastiche
/// lungo le chiamate e relazioni semantiche, disponendo organicamente i nodi con convergenza rapida.
/// </summary>
public sealed class ForceDirectedLayoutEngine : IGraphLayoutEngine
{
    public static readonly ForceDirectedLayoutEngine Instance = new();

    void IGraphLayoutEngine.Apply(VisualCodeGraph graph) => Apply(graph);

    /// <summary>
    /// Calcola e applica le coordinate (X, Y) a tutti i nodi del grafo visuale mediante simulazione fisica di tipo Force-Directed (Fruchterman-Reingold).
    /// </summary>
    /// <param name="graph">Il grafo visuale da posizionare.</param>
    /// <remarks>
    /// Il processo di simulazione:
    /// <list type="number">
    ///   <item><description>Inizializza i nodi container su un cerchio preliminare per garantire stabilità.</description></item>
    ///   <item><description>Ad ogni iterazione calcola forze repulsive elettrostatiche <c>k^2 / d</c> tra tutti i nodi per prevenire sovrapposizioni.</description></item>
    ///   <item><description>Calcola forze attrattive elastiche <c>(d^2 / k) * peso</c> lungo gli archi di chiamata, istanziazione e dipendenza di tipo.</description></item>
    ///   <item><description>Applica una forza di gravità centripeta verso l'origine <c>(0, 0)</c> per mantenere il grafo compatto.</description></item>
    ///   <item><description>Sposta i nodi limitando l'escursione massima in base a un fattore di temperatura con raffreddamento esponenziale progressivo (simulated annealing).</description></item>
    /// </list>
    /// </remarks>
    public static void Apply(VisualCodeGraph graph)
    {
        // 1. Recupero di tutti i nodi container (classi, moduli, form, ecc.)
        var containers = graph.Nodes.Where(n => n.IsContainer).ToList();
        if (containers.Count == 0) return;

        var projects = graph.Nodes.Where(n => n.Kind == SymbolKind.Project).ToList();
        var n = containers.Count;

        // =========================================================================
        // PASSO 1: Mappa di appartenenza nodo -> container
        // Mappa ciascun nodo membro (metodo, proprietà, campo) al proprio container genitore.
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
        // PASSO 2: Posizionamento circolare iniziale (Seed Stabile)
        // Disporre i container su un anello preliminare evita singolarità o minimi locali
        // caotici prima dell'avvio della simulazione fisica Fruchterman-Reingold.
        // =========================================================================
        var initialRadius = Math.Max(350.0, n * 30.0);
        for (var i = 0; i < n; i++)
        {
            var angle = 2 * Math.PI * i / n;
            containers[i].X = Math.Cos(angle) * initialRadius;
            containers[i].Y = Math.Sin(angle) * initialRadius;
        }

        // =========================================================================
        // PASSO 3: Inizializzazione dei parametri del modello fisico (Fruchterman-Reingold)
        // - k: distanza ideale di equilibrio tra due nodi (scala con l'area virtuale del canvas)
        // - iterations: numero di iterazioni di simulazione dinamica
        // - temp: temperatura iniziale (escursione massima consentita per passo)
        // - coolingFactor: tasso geometrico di raffreddamento (Simulated Annealing)
        // =========================================================================
        var optimalDistance = Math.Max(220.0, Math.Sqrt((1200.0 * 1200.0) / Math.Max(n, 1)));
        var k = optimalDistance;
        var kSquared = k * k;

        var iterations = Math.Min(100, Math.Max(40, n * 2));
        var initialTemp = initialRadius * 0.4;
        var temp = initialTemp;
        var coolingFactor = 0.95;

        // Buffer riutilizzabili per gli spostamenti netti calcolati ad ogni iterazione
        var dispX = new double[n];
        var dispY = new double[n];

        // =========================================================================
        // PASSO 4: Mappatura degli archi semantici tra container con pesi differenziati
        // Proietta le relazioni tra membri su archi equivalenti tra container genitore:
        // - Calls (3.0): attrazione forte per moduli che si chiamano reciprocamente
        // - Instantiates (2.5): attrazione per modulo factory/creatore
        // - UsesType (1.5): attrazione per dipendenza di tipo
        // =========================================================================
        var containerEdges = new List<(int SrcIdx, int TgtIdx, double Weight)>();
        var containerIndexById = containers.Select((c, idx) => (c.Id, idx)).ToDictionary(x => x.Id, x => x.idx);

        foreach (var edge in graph.Edges)
        {
            if (nodeToContainerMap.TryGetValue(edge.SourceId, out var c1) &&
                nodeToContainerMap.TryGetValue(edge.TargetId, out var c2) &&
                c1 != c2 &&
                containerIndexById.TryGetValue(c1, out var idx1) &&
                containerIndexById.TryGetValue(c2, out var idx2))
            {
                var w = edge.Kind switch
                {
                    RelationshipKind.Calls => 3.0,
                    RelationshipKind.Instantiates => 2.5,
                    RelationshipKind.UsesType => 1.5,
                    _ => 1.0
                };
                containerEdges.Add((idx1, idx2, w));
            }
        }

        // =========================================================================
        // PASSO 5: Ciclo principale di simulazione dinamica (Physics Loop)
        // =========================================================================
        for (var iter = 0; iter < iterations; iter++)
        {
            // Reset dei vettori di spostamento accumulati
            Array.Clear(dispX, 0, n);
            Array.Clear(dispY, 0, n);

            // 5a. Forze repulsive elettrostatiche tra tutte le coppie di nodi (O(N^2))
            // Formula di Coulomb: F_rep = k^2 / dist
            for (var i = 0; i < n; i++)
            {
                var c1 = containers[i];
                for (var j = i + 1; j < n; j++)
                {
                    var c2 = containers[j];
                    var dx = c1.X - c2.X;
                    var dy = c1.Y - c2.Y;
                    var dist = Math.Sqrt(dx * dx + dy * dy);

                    // Protezione contro singolarità (sovrapposizione esatta di due nodi)
                    if (dist < 0.001)
                    {
                        dx = (i + 1) * 0.1;
                        dy = (j + 1) * 0.1;
                        dist = Math.Sqrt(dx * dx + dy * dy);
                    }

                    var repForce = kSquared / dist;
                    var fx = (dx / dist) * repForce;
                    var fy = (dy / dist) * repForce;

                    // I nodi si respingono a vicenda lungo la congiungente
                    dispX[i] += fx;
                    dispY[i] += fy;
                    dispX[j] -= fx;
                    dispY[j] -= fy;
                }
            }

            // 5b. Forze attrattive elastiche lungo gli archi semantici attivi (Legge di Hooke)
            // Formula di Hooke: F_att = (dist^2 / k) * peso
            foreach (var (srcIdx, tgtIdx, weight) in containerEdges)
            {
                var c1 = containers[srcIdx];
                var c2 = containers[tgtIdx];
                var dx = c1.X - c2.X;
                var dy = c1.Y - c2.Y;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist < 0.001) continue;

                var attForce = (dist * dist / k) * weight;
                var fx = (dx / dist) * attForce;
                var fy = (dy / dist) * attForce;

                // I nodi collegati si attraggono reciprocamente
                dispX[srcIdx] -= fx;
                dispY[srcIdx] -= fy;
                dispX[tgtIdx] += fx;
                dispY[tgtIdx] += fy;
            }

            // 5c. Forza di gravità centripeta verso l'origine (0, 0)
            // Evita che nodi disconnessi o cluster periferici si allontanino all'infinito
            for (var i = 0; i < n; i++)
            {
                var c = containers[i];
                var dist = Math.Sqrt(c.X * c.X + c.Y * c.Y);
                if (dist > 1.0)
                {
                    dispX[i] -= (c.X / dist) * (dist * 0.05);
                    dispY[i] -= (c.Y / dist) * (dist * 0.05);
                }
            }

            // 5d. Applicazione degli spostamenti limitati dalla soglia di temperatura (Annealing)
            for (var i = 0; i < n; i++)
            {
                var dLen = Math.Sqrt(dispX[i] * dispX[i] + dispY[i] * dispY[i]);
                if (dLen > 0.001)
                {
                    var step = Math.Min(dLen, temp);
                    containers[i].X += (dispX[i] / dLen) * step;
                    containers[i].Y += (dispY[i] / dLen) * step;
                }
            }

            // 5e. Raffreddamento progressivo del sistema per convergenza dolce
            temp *= coolingFactor;
        }

        // =========================================================================
        // PASSO 6: Posizionamento dei membri satelliti attorno ai rispettivi container
        // =========================================================================
        foreach (var container in containers)
        {
            var members = graph.GetChildren(container.Id).ToList();
            var memberCount = Math.Max(members.Count, 1);
            var memberRingRadius = Math.Max(80.0, memberCount * 13.0);

            for (var j = 0; j < memberCount; j++)
            {
                var memberAngle = 2 * Math.PI * j / memberCount;
                var member = members[j];
                member.X = container.X + Math.Cos(memberAngle) * memberRingRadius;
                member.Y = container.Y + Math.Sin(memberAngle) * memberRingRadius;
            }
        }

        // =========================================================================
        // PASSO 7: Posizionamento dei nodi Progetto al centroide geometrico dei moduli
        // =========================================================================
        foreach (var proj in projects)
        {
            var projContainers = graph.GetChildren(proj.Id).ToList();
            if (projContainers.Count > 0)
            {
                proj.X = projContainers.Average(c => c.X);
                proj.Y = projContainers.Average(c => c.Y);
            }
            else
            {
                proj.X = 0;
                proj.Y = 0;
            }
        }
    }
}
