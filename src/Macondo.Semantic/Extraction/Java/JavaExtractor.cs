using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Estrattore semantico per file sorgente Java (.java) con parser AST ricorsivo resiliente a tolleranza d'errore.
/// 
/// CARATTERISTICHE E CAPACITÀ SUPPORTATE:
/// 1. Package &amp; Imports: tracciamento del package gerarchico per generare QualifiedName precisi (es. 'com.acme.service.OrderService').
/// 2. Annotazioni: supporto ad annotazioni su classi, metodi e campi (@Override, @Entity, @Autowired, @RestController, ecc.).
/// 3. Dichiarazioni di Tipi: Classi (standard, astratte, finali, statiche, inner), Interfacce, Enum (con costanti), Record (Java 16+) e @interface.
/// 4. Ereditarietà e Interfacce: clausole 'extends', 'implements' e 'permits' con supporto ai parametri generici (&lt;T, K&gt;).
/// 5. Routine &amp; Costruttori: metodi statici/istanza, costruttori, parametri tipizzati, tipi di ritorno, clausole throws.
/// 6. Campi &amp; Costanti: variabili di istanza, campi statici, costanti ('static final' ed enum values).
/// 7. Analisi Invocazioni (CALLS): rilevamento invocazioni di metodi con ricevitore esplicito (obj.method()), implicito o super/this.
/// 8. Analisi Creazioni Oggetti (INSTANTIATES): espressioni 'new TypeName(...)'.
/// </summary>
public static class JavaExtractor
{
    /// <summary>
    /// Esegue il parsing e l'estrazione semantica di un file Java inserendo direttamente i simboli nel <see cref="CodeGraph"/>.
    /// </summary>
    /// <param name="graph">Il CodeGraph condiviso in cui registrare i simboli e le relazioni CONTAINS.</param>
    /// <param name="filePath">Percorso o nome del file sorgente Java analizzato.</param>
    /// <param name="sourceText">Il testo del codice sorgente Java completo del file.</param>
    /// <returns>Il risultato strutturato contenente simboli, call-site, istanziazioni ed ereditarietà.</returns>
    public static JavaExtractionResult ExtractJavaFile(CodeGraph graph, string filePath, string sourceText)
    {
        var parser = new JavaFileParser(graph, filePath, sourceText);
        return parser.Parse();
    }
}
