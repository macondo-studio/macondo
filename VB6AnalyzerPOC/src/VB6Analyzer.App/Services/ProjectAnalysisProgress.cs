namespace Macondo.App.Services;

/// <summary>
/// Rappresenta lo stato di avanzamento durante l'analisi di un progetto o cartella VB6.
/// </summary>
public sealed class ProjectAnalysisProgress
{
    /// <summary>
    /// Numero di file analizzati finora (0..TotalFiles).
    /// </summary>
    public int CurrentFileIndex { get; set; }

    /// <summary>
    /// Numero totale di file sorgente VB6 da analizzare.
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Nome del file attualmente in fase di elaborazione (es. "frmMain.frm").
    /// </summary>
    public string CurrentFileName { get; set; } = string.Empty;

    /// <summary>
    /// Numero cumulativo di nodi/simboli estratti finora nell'AST/grafo.
    /// </summary>
    public int ExtractedNodesCount { get; set; }

    /// <summary>
    /// Messaggio descrittivo della fase o operazione corrente.
    /// </summary>
    public string PhaseMessage { get; set; } = string.Empty;

    /// <summary>
    /// Percentuale di completamento da 0 a 100.
    /// </summary>
    public double Percentage => TotalFiles > 0 ? Math.Clamp((double)CurrentFileIndex / TotalFiles * 100.0, 0.0, 100.0) : 0.0;
}
