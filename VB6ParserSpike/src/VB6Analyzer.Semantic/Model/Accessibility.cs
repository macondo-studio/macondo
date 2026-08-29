namespace VB6Analyzer.Semantic.Model;

/// <summary>
/// Livelli di visibilità / accessibilità dei simboli in VB6 (sezione 9 del charter).
/// </summary>
public enum Accessibility
{
    Default,
    Private,
    Public,
    Friend,
    Global
}
