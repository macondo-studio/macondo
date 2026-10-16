using Macondo.Semantic.Model;

namespace Macondo.Semantic.Extraction;

/// <summary>
/// Estrattore semantico per file sorgente Kotlin (.kt / .kts) con lexer dedicato e parser AST ricorsivo a tolleranza d'errore.
/// </summary>
public static class KotlinExtractor
{
    /// <summary>
    /// Esegue il parsing e l'estrazione semantica di un file Kotlin inserendo direttamente i simboli nel <see cref="CodeGraph"/>.
    /// </summary>
    /// <param name="graph">Il CodeGraph condiviso in cui registrare i simboli e le relazioni CONTAINS.</param>
    /// <param name="filePath">Percorso o nome del file sorgente Kotlin analizzato.</param>
    /// <param name="sourceText">Il testo del codice sorgente Kotlin completo del file.</param>
    /// <returns>Il risultato strutturato contenente simboli, call-site, istanziazioni ed ereditarietà.</returns>
    public static KotlinExtractionResult ExtractKotlinFile(CodeGraph graph, string filePath, string sourceText)
    {
        var parser = new KotlinFileParser(graph, filePath, sourceText);
        return parser.Parse();
    }
}
