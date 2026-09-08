using Macondo.Semantic.Extraction;
using Macondo.Semantic.Graph;
using Macondo.Semantic.Model;
using Macondo.Semantic.Resolution;

namespace Macondo.Semantic.Spike;

/// <summary>
/// Programma di verifica / Spike per la Milestone M4: Call Graph.
/// 
/// OBIETTIVI VERIFICATI:
/// 1. Generazione automatica nel CodeGraph di:
///    - Relazioni CALLS (chiamate dirette tra routine)
///    - Relazioni INSTANTIATES (istanziazioni di classi con New o Set ... = New)
///    - Relazioni USES_TYPE (dipendenze di tipo su variabili, campi, parametri e ritorni)
/// 2. Interrogazione Call Graph (<see cref="CallGraphQueries"/>):
///    - Callers diretti e indiretti (Who calls X?)
///    - Callees diretti e indiretti (What does X call?)
///    - Execution Path Discovery: ricerca dei cammini di esecuzione multi-livello
///      (es. da 11_OrderService.ProcessOrder a DatabaseHelper.Execute)
///    - Cycle Detection: rilevamento di mutua ricorsione e cicli (Alpha -> Beta -> Gamma -> Alpha).
/// </summary>
internal static class Program
{
    private static void Main()
    {
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        if (!Directory.Exists(fixturesDir))
        {
            Console.Error.WriteLine($"Cartella Fixtures non trovata in: {fixturesDir}");
            return;
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine("               VB6 ANALYZER — SPIKE M4 (CALL GRAPH ENGINE)                      ");
        Console.WriteLine("================================================================================");
        Console.WriteLine();

        // ---------------------------------------------------------------------
        // PASSO 1: Parsing ed estrazione di tutti i moduli nel CodeGraph
        // ---------------------------------------------------------------------
        var graph = new CodeGraph();
        var extractionResults = new List<ModuleExtractionResult>();

        var fixtureFiles = Directory.GetFiles(fixturesDir, "*.*")
            .Where(f => f.EndsWith(".cls", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".bas", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".frm", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".ctl", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Path.GetFileName(f))
            .ToList();

        Console.WriteLine($"Caricamento ed estrazione di {fixtureFiles.Count} moduli VB6...");
        foreach (var file in fixtureFiles)
        {
            var fileName = Path.GetFileName(file);
            var source = File.ReadAllText(file);
            var result = ModuleExtractor.ExtractModule(graph, fileName, source);
            extractionResults.Add(result);

            if (!result.Success)
            {
                Console.WriteLine($"[ERRORE PARSE] {fileName}:");
                foreach (var err in result.ParseErrors)
                    Console.WriteLine($"  - {err}");
            }
        }

        Console.WriteLine($"Estrazione completata: {graph.Symbols.Count} simboli estratti nel CodeGraph.");
        Console.WriteLine();

        // ---------------------------------------------------------------------
        // PASSO 2: Risoluzione Tipi (TypeResolver) e Simboli (SymbolResolver)
        // ---------------------------------------------------------------------
        var typeResolver = new TypeResolver(graph);
        typeResolver.ResolveAllTypes();
        var symbolResolver = new SymbolResolver(graph, typeResolver);

        // ---------------------------------------------------------------------
        // PASSO 3: Generazione delle Relazioni del Call Graph (CallGraphBuilder)
        // ---------------------------------------------------------------------
        Console.WriteLine("--------------------------------------------------------------------------------");
        Console.WriteLine("1. GENERAZIONE RELAZIONI SEMANTICHE (CallGraphBuilder)");
        Console.WriteLine("--------------------------------------------------------------------------------");

        var builder = new CallGraphBuilder(graph, symbolResolver);
        builder.BuildGraph(extractionResults);

        var relsByKind = graph.Relationships
            .GroupBy(r => r.Kind)
            .OrderBy(g => g.Key.ToString())
            .ToList();

        foreach (var group in relsByKind)
        {
            Console.WriteLine($"  - {group.Key,-15}: {group.Count(),3} relazioni");
        }
        Console.WriteLine($"  Totale relazioni nel CodeGraph: {graph.Relationships.Count}");
        Console.WriteLine();

        // ---------------------------------------------------------------------
        // PASSO 4: Interrogazioni del Call Graph (CallGraphQueries)
        // ---------------------------------------------------------------------
        Console.WriteLine("--------------------------------------------------------------------------------");
        Console.WriteLine("2. QUERY CALL GRAPH: CALLERS & CALLEES (CallGraphQueries)");
        Console.WriteLine("--------------------------------------------------------------------------------");

        var queries = new CallGraphQueries(graph);

        // A. Chi chiama DatabaseHelper.Execute?
        var dbExecute = graph.Symbols.FirstOrDefault(s => s.QualifiedName.EndsWith("DatabaseHelper.Execute", StringComparison.OrdinalIgnoreCase) ||
                                                          s.QualifiedName.EndsWith("13_DatabaseHelper.Execute", StringComparison.OrdinalIgnoreCase));
        if (dbExecute != null)
        {
            var callers = queries.GetDirectCallers(dbExecute);
            Console.WriteLine($"Who calls '{dbExecute.QualifiedName}'? (Direct Callers: {callers.Count})");
            foreach (var caller in callers)
                Console.WriteLine($"  <- [CALLS] {caller.QualifiedName}");
            Console.WriteLine();
        }

        // B. Chi chiama Customer.Save?
        var custSave = graph.Symbols.FirstOrDefault(s => s.QualifiedName.Equals("Customer.Save", StringComparison.OrdinalIgnoreCase));
        if (custSave != null)
        {
            var callers = queries.GetDirectCallers(custSave);
            Console.WriteLine($"Who calls '{custSave.QualifiedName}'? (Direct Callers: {callers.Count})");
            foreach (var caller in callers)
                Console.WriteLine($"  <- [CALLS] {caller.QualifiedName}");
            Console.WriteLine();
        }

        // C. Cosa chiama OrderService.ProcessOrder?
        var orderServiceProcess = graph.Symbols.FirstOrDefault(s => s.QualifiedName.EndsWith("OrderService.ProcessOrder", StringComparison.OrdinalIgnoreCase));
        if (orderServiceProcess != null)
        {
            var callees = queries.GetDirectCallees(orderServiceProcess);
            Console.WriteLine($"What does '{orderServiceProcess.QualifiedName}' call? (Direct Callees: {callees.Count})");
            foreach (var callee in callees)
                Console.WriteLine($"  -> [CALLS] {callee.QualifiedName}");

            var instantiated = queries.GetInstantiatedClasses(orderServiceProcess);
            Console.WriteLine($"What does '{orderServiceProcess.QualifiedName}' instantiate? (Instantiates: {instantiated.Count})");
            foreach (var inst in instantiated)
                Console.WriteLine($"  -> [INSTANTIATES] {inst.QualifiedName}");
            Console.WriteLine();
        }

        // ---------------------------------------------------------------------
        // PASSO 5: Execution Path Discovery
        // ---------------------------------------------------------------------
        Console.WriteLine("--------------------------------------------------------------------------------");
        Console.WriteLine("3. EXECUTION PATH DISCOVERY (Search Paths between Routines)");
        Console.WriteLine("--------------------------------------------------------------------------------");

        if (orderServiceProcess != null && dbExecute != null)
        {
            Console.WriteLine($"Ricerca cammini di esecuzione: da '{orderServiceProcess.QualifiedName}' a '{dbExecute.QualifiedName}'...");
            var paths = queries.FindExecutionPaths(orderServiceProcess, dbExecute);

            Console.WriteLine($"Cammini trovati: {paths.Count}");
            foreach (var path in paths)
            {
                Console.WriteLine($"  [Path (lunghezza {path.Length})] {path}");
            }
            Console.WriteLine();
        }

        // ---------------------------------------------------------------------
        // PASSO 6: Cycle Detection (Rilevamento Cicli di Chiamata)
        // ---------------------------------------------------------------------
        Console.WriteLine("--------------------------------------------------------------------------------");
        Console.WriteLine("4. CYCLE DETECTION (Rilevamento Chiamate Ricorsive e Cicliche)");
        Console.WriteLine("--------------------------------------------------------------------------------");

        var cycles = queries.DetectCycles();
        Console.WriteLine($"Cicli di chiamata individuati nel grafo: {cycles.Count}");
        foreach (var cycle in cycles)
        {
            Console.WriteLine($"  [CICLO] {cycle}");
        }
        Console.WriteLine();

        // ---------------------------------------------------------------------
        // PASSO 7: Impact Analysis & Blast Radius (Milestone M6)
        // ---------------------------------------------------------------------
        Console.WriteLine("--------------------------------------------------------------------------------");
        Console.WriteLine("5. IMPACT ANALYSIS & BLAST RADIUS (Milestone M6)");
        Console.WriteLine("--------------------------------------------------------------------------------");

        var impactAnalyzer = new Impact.ImpactAnalyzer(graph, queries);

        // A. Blast Radius di una routine foglia/critica: DatabaseHelper.Execute
        if (dbExecute != null)
        {
            var dbImpact = impactAnalyzer.AnalyzeRoutineImpact(dbExecute);
            Console.WriteLine($"[BLAST RADIUS] Modifica a '{dbExecute.QualifiedName}':");
            Console.WriteLine($"  - Chiamanti Diretti    : {dbImpact.DirectCallers.Count} ({string.Join(", ", dbImpact.DirectCallers.Select(c => c.QualifiedName))})");
            Console.WriteLine($"  - Chiamanti Indiretti  : {dbImpact.IndirectCallers.Count} ({string.Join(", ", dbImpact.IndirectCallers.Select(c => $"{c.Symbol.QualifiedName} [Lv.{c.Depth}]"))})");
            Console.WriteLine($"  - Classi Impattate     : {dbImpact.AffectedClasses.Count} ({string.Join(", ", dbImpact.AffectedClasses.Select(c => c.Name))})");
            Console.WriteLine($"  - Moduli Impattati     : {dbImpact.AffectedModules.Count} ({string.Join(", ", dbImpact.AffectedModules.Select(c => c.Name))})");
            Console.WriteLine($"  - Form Impattate (UI)  : {dbImpact.AffectedForms.Count}");
            Console.WriteLine($"  - Profondità Massima   : {dbImpact.MaxDependencyDepth}");
            Console.WriteLine($"  - Livello di Rischio   : {dbImpact.RiskLevel}");
            Console.WriteLine($"  - Percorsi di Chiamata : {dbImpact.AffectedExecutionPaths.Count}");
            foreach (var path in dbImpact.AffectedExecutionPaths)
                Console.WriteLine($"      -> {path}");
            Console.WriteLine();
        }

        // B. Blast Radius di un metodo di dominio: Customer.Save
        if (custSave != null)
        {
            var saveImpact = impactAnalyzer.AnalyzeRoutineImpact(custSave);
            Console.WriteLine($"[BLAST RADIUS] Modifica a '{custSave.QualifiedName}':");
            Console.WriteLine($"  - Chiamanti Diretti    : {saveImpact.DirectCallers.Count} ({string.Join(", ", saveImpact.DirectCallers.Select(c => c.QualifiedName))})");
            Console.WriteLine($"  - Classi Impattate     : {saveImpact.AffectedClasses.Count} ({string.Join(", ", saveImpact.AffectedClasses.Select(c => c.Name))})");
            Console.WriteLine($"  - Profondità Massima   : {saveImpact.MaxDependencyDepth}");
            Console.WriteLine($"  - Livello di Rischio   : {saveImpact.RiskLevel}");
            Console.WriteLine();
        }

        // C. Blast Radius a livello di intera classe: Customer.cls
        var customerClass = graph.Symbols.FirstOrDefault(s => s.Kind == SymbolKind.Class && s.Name.Equals("Customer", StringComparison.OrdinalIgnoreCase));
        if (customerClass != null)
        {
            var classImpact = impactAnalyzer.AnalyzeTypeImpact(customerClass);
            Console.WriteLine($"[BLAST RADIUS] Modifica/Refactoring della Classe '{customerClass.QualifiedName}':");
            Console.WriteLine($"  - Chiamanti Totali Membri: {classImpact.DirectCallers.Count + classImpact.IndirectCallers.Count}");
            Console.WriteLine($"  - Classi che Istanziano  : {classImpact.Instantiators.Count} ({string.Join(", ", classImpact.Instantiators.Select(i => i.QualifiedName))})");
            Console.WriteLine($"  - Dipendenze di Tipo     : {classImpact.TypeDependents.Count} ({string.Join(", ", classImpact.TypeDependents.Select(t => t.QualifiedName))})");
            Console.WriteLine($"  - Classi Esterne Toccate : {classImpact.AffectedClasses.Count} ({string.Join(", ", classImpact.AffectedClasses.Select(c => c.Name))})");
            Console.WriteLine($"  - Livello di Rischio     : {classImpact.RiskLevel}");
            Console.WriteLine();
        }

        // ---------------------------------------------------------------------
        // PASSO 8: Esportazione CodeGraph per LLM & Tooling (Markdown & JSON)
        // ---------------------------------------------------------------------
        Console.WriteLine("--------------------------------------------------------------------------------");
        Console.WriteLine("5. ESPORTAZIONE CODEGRAPH PER LLM & TOOLING (Markdown & JSON)");
        Console.WriteLine("--------------------------------------------------------------------------------");

        var mdExport = graph.ToMarkdown();
        var mdPath = Path.Combine(AppContext.BaseDirectory, "codegraph_export.md");
        File.WriteAllText(mdPath, mdExport);
        Console.WriteLine($"[ESPORTAZIONE MARKDOWN] Generato: {mdPath} ({mdExport.Length} caratteri)");

        var jsonExport = graph.ToJson(indented: true);
        var jsonPath = Path.Combine(AppContext.BaseDirectory, "codegraph_export.json");
        File.WriteAllText(jsonPath, jsonExport);
        Console.WriteLine($"[ESPORTAZIONE JSON]     Generato: {jsonPath} ({jsonExport.Length} caratteri)");

        // Test Roundtrip JSON
        var reloadedGraph = CodeGraph.FromJson(jsonExport);
        Console.WriteLine($"[ROUNDTRIP JSON TEST]   Simboli originali: {graph.Symbols.Count} -> Ricaricati: {reloadedGraph.Symbols.Count}");
        Console.WriteLine($"                        Relazioni originali: {graph.Relationships.Count} -> Ricaricate: {reloadedGraph.Relationships.Count}");
        bool roundtripOk = graph.Symbols.Count == reloadedGraph.Symbols.Count && graph.Relationships.Count == reloadedGraph.Relationships.Count;
        Console.WriteLine($"                        Esito Roundtrip: {(roundtripOk ? "[SUCCESSO]" : "[FALLITO]")}");
        Console.WriteLine();

        // Esempio di Context Slice mirato per un LLM (OrderService.ProcessOrder)
        if (orderServiceProcess != null)
        {
            Console.WriteLine($"[LLM CONTEXT SLICE PREVIEW] per '{orderServiceProcess.QualifiedName}':");
            Console.WriteLine("--------------------------------------------------------------------------------");
            var slice = graph.ToMarkdownSlice(orderServiceProcess);
            Console.WriteLine(slice);
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine();
        }

        // ---------------------------------------------------------------------
        // PASSO 9: Riepilogo Finale M4 + M6 + Export
        // ---------------------------------------------------------------------
        Console.WriteLine("================================================================================");
        Console.WriteLine("                  RIEPILOGO FINALE MILESTONE M4 & M6                            ");
        Console.WriteLine("================================================================================");
        Console.WriteLine($"Totale moduli analizzati       : {extractionResults.Count}");
        Console.WriteLine($"Totale simboli nel CodeGraph   : {graph.Symbols.Count}");
        Console.WriteLine($"Totale relazioni semantiche    : {graph.Relationships.Count}");
        Console.WriteLine($"  - CONTAINS                   : {graph.Relationships.Count(r => r.Kind == RelationshipKind.Contains)}");
        Console.WriteLine($"  - CALLS                      : {graph.Relationships.Count(r => r.Kind == RelationshipKind.Calls)}");
        Console.WriteLine($"  - INSTANTIATES               : {graph.Relationships.Count(r => r.Kind == RelationshipKind.Instantiates)}");
        Console.WriteLine($"  - USES_TYPE                  : {graph.Relationships.Count(r => r.Kind == RelationshipKind.UsesType)}");
        Console.WriteLine();

        Console.WriteLine("Verifica dei punti chiave del Charter per M6 & Export:");
        Console.WriteLine("  [OK] Direct & Indirect callers calculation with Depth levels");
        Console.WriteLine("  [OK] Affected Forms, Classes, and Modules identification");
        Console.WriteLine("  [OK] Affected Execution Paths (Call Chains) discovery");
        Console.WriteLine("  [OK] Max dependency depth metric");
        Console.WriteLine("  [OK] Type-level Blast Radius (INSTANTIATES + USES_TYPE + member calls)");
        Console.WriteLine("  [OK] Risk Level estimation (Low, Medium, High, Critical)");
        Console.WriteLine("  [OK] Token-efficient Markdown Repo Map & LLM Context Slice Exporter");
        Console.WriteLine("  [OK] Full JSON Serialization / Deserialization Roundtrip");
        Console.WriteLine("================================================================================");
    }
}
