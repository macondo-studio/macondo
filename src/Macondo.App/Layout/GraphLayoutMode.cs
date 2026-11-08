namespace Macondo.App.Layout;

/// <summary>
/// Modalità di disposizione visiva e posizionamento spaziale dei nodi nel CodeGraph.
/// </summary>
public enum GraphLayoutMode
{
    /// <summary>
    /// Disposizione concentrica geometrica standard (Progetto al centro, moduli su anello uniforme).
    /// </summary>
    StandardRadial,

    /// <summary>
    /// Disposizione circolare con ordinamento angolare dei moduli basato sull'affinità delle chiamate
    /// per minimizzare gli incroci di archi attraverso il centro del canvas.
    /// </summary>
    AffinityRadial,

    /// <summary>
    /// Rileva i sottosistemi di business/moduli fortemente accoppiati e li raggruppa in isole/cluster separati sul piano.
    /// </summary>
    ClusteredIslands,

    /// <summary>
    /// Simulazione fisica a forze (Fruchterman-Reingold): repulsione elettrostatica tra nodi e attrazione a molla lungo le chiamate.
    /// </summary>
    ForceDirected
}
