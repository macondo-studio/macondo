using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Rappresenta una riga di riepilogo del Blast Radius per il DataGrid della Vista Griglia.
/// Include conteggi di chiamanti diretti e indiretti, profondità massima, livello di rischio e container.
/// </summary>
public sealed class BlastRadiusGridRow
{
    /// <summary>Il simbolo semantico della routine o tipo analizzato.</summary>
    public required Symbol TargetSymbol { get; init; }

    /// <summary>ID univoco del nodo corrispondente nel grafo visuale.</summary>
    public required Guid NodeId { get; init; }

    /// <summary>Nome semplice del simbolo (es. "Execute", "Save").</summary>
    public required string Name { get; init; }

    /// <summary>Nome qualificato completo (es. "DatabaseHelper.Execute").</summary>
    public required string QualifiedName { get; init; }

    /// <summary>Nome del file o container di appartenenza (es. "DatabaseHelper.bas").</summary>
    public required string ContainerName { get; init; }

    /// <summary>Tipo di simbolo (es. Method, Property, Class).</summary>
    public required string Kind { get; init; }

    /// <summary>Numero di chiamanti diretti (profondità = 1).</summary>
    public required int DirectCallersCount { get; init; }

    /// <summary>Numero di chiamanti indiretti / transitivi (profondità >= 2).</summary>
    public required int IndirectCallersCount { get; init; }

    /// <summary>Profondità massima della catena di chiamate verso questo simbolo.</summary>
    public required int MaxDepth { get; init; }

    /// <summary>Livello di rischio di regressione calcolato dall'Impact Analyzer.</summary>
    public required ImpactRiskLevel RiskLevel { get; init; }

    /// <summary>Testo visualizzato per il rischio (Basso, Medio, Alto, Critico / Low, Medium, High, Critical).</summary>
    public string RiskText => Services.LocalizationService.Instance.GetRiskLevel(RiskLevel);

    /// <summary>Colore di sfondo per il badge del rischio.</summary>
    public string RiskBadgeBackground => RiskLevel switch
    {
        ImpactRiskLevel.Critical => "#B81D2D",
        ImpactRiskLevel.High => "#C86400",
        ImpactRiskLevel.Medium => "#C49000",
        _ => "#1A8A56"
    };

    /// <summary>Colore di testo per il badge del rischio.</summary>
    public string RiskBadgeForeground => "#FFFFFF";

    /// <summary>Numero di Form UI impattate dalla modifica.</summary>
    public required int AffectedFormsCount { get; init; }

    /// <summary>Totale complessivo delle entità impattate.</summary>
    public required int TotalImpactedCount { get; init; }

    /// <summary>Nomi delle form impattate separati da virgola.</summary>
    public required string AffectedFormsSummary { get; init; }

    /// <summary>Testo localizzato per il pulsante d'azione rapida.</summary>
    public string ActionButtonText => Services.LocalizationService.Instance["Btn_ShowInGraph"];

    /// <summary>Tooltip localizzato per il pulsante d'azione rapida.</summary>
    public string ActionButtonTooltip => Services.LocalizationService.Instance["Tooltip_ShowInGraph"];

    public override string ToString() => $"{QualifiedName} [{RiskLevel}]";
}
