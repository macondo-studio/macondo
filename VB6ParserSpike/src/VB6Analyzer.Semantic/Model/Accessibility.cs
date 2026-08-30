namespace VB6Analyzer.Semantic.Model;

/// <summary>
/// Livelli di visibilità e modificatori di accesso dei simboli in VB6 (sezione 9 del charter).
/// </summary>
public enum Accessibility
{
    /// <summary>Accessibilità predefinita implicita (Private per membri di classe/form, Public per membri di moduli .bas).</summary>
    Default,

    /// <summary>Accessibile solo all'interno del modulo/classe corrente ('Private').</summary>
    Private,

    /// <summary>Accessibile pubblicamente all'intero progetto o dall'esterno ('Public').</summary>
    Public,

    /// <summary>Accessibile a tutti i moduli all'interno dello stesso progetto/componente, ma non all'esterno ('Friend').</summary>
    Friend,

    /// <summary>Dichiarazione a livello globale per moduli standard legacy ('Global').</summary>
    Global
}
