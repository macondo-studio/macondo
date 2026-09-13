using System;
using System.Collections.Generic;
using Macondo.Semantic.Impact;
using Macondo.Semantic.Model;

namespace Macondo.App.Models;

/// <summary>
/// Snapshot completo del progetto analizzato salvato nella cache locale (%APPDATA%\Macondo\cache\).
/// Permette l'avvio a freddo istantaneo (sub-50ms) ripristinando il CodeGraph semantico,
/// il layout geometrico del grafo visuale e l'intera matrice del Blast Radius.
/// </summary>
public sealed class ProjectCacheSnapshot
{
    /// <summary>Versione dello schema dello snapshot per invalidazione in caso di evoluzione del modello.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Percorso assoluto della cartella analizzata.</summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Nome visualizzato del progetto.</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Data e ora UTC di creazione dell'analisi.</summary>
    public DateTime AnalyzedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Timestamp UTC del file sorgente VB6 modificato più di recente al momento dell'analisi.</summary>
    public DateTime LastFileWriteTimeUtc { get; set; }

    /// <summary>Numero complessivo di file sorgente VB6 (.cls, .bas, .frm, .ctl) inclusi.</summary>
    public int FileCount { get; set; }

    /// <summary>Elenco completo dei simboli semantici estratti dal CodeGraph.</summary>
    public List<Symbol> Symbols { get; set; } = new();

    /// <summary>Elenco completo delle relazioni semantiche tra i simboli.</summary>
    public List<Relationship> Relationships { get; set; } = new();

    /// <summary>Coordinate geometriche (X, Y) e stato di espansione per ogni nodo del grafo visuale.</summary>
    public List<NodeLayoutSnapshot> LayoutNodes { get; set; } = new();

    /// <summary>Righe precalcolate per la tabella del Blast Radius.</summary>
    public List<BlastRadiusRowSnapshot> BlastRadiusRows { get; set; } = new();
}

/// <summary>
/// Snapshot delle coordinate di rendering e stato per un singolo nodo del grafo visuale.
/// </summary>
public sealed class NodeLayoutSnapshot
{
    public Guid Id { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public bool IsExpanded { get; set; } = true;
}

/// <summary>
/// Snapshot serializzabile di una riga della tabella Blast Radius.
/// </summary>
public sealed class BlastRadiusRowSnapshot
{
    public Guid NodeId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string QualifiedName { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int DirectCallersCount { get; set; }
    public int IndirectCallersCount { get; set; }
    public int MaxDepth { get; set; }
    public ImpactRiskLevel RiskLevel { get; set; }
    public int AffectedFormsCount { get; set; }
    public int TotalImpactedCount { get; set; }
    public string AffectedFormsSummary { get; set; } = string.Empty;
}
