using System;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;

namespace Macondo.App.Views;

/// <summary>
/// Controllo visuale per la scheda dedicata all'anteprima del codice sorgente del simbolo selezionato.
/// </summary>
public partial class SourceCodePreviewView : UserControl
{
    /// <summary>
    /// Evento generato quando l'utente richiede di aprire il file originale del simbolo.
    /// </summary>
    public event EventHandler? OpenFileRequested;

    /// <summary>
    /// Evento generato quando l'utente richiede di aprire la cartella contenitrice.
    /// </summary>
    public event EventHandler? OpenFolderRequested;

    /// <summary>
    /// Evento generato quando l'utente copia il frammento di codice negli appunti.
    /// </summary>
    public event EventHandler<string>? CopyCodeRequested;

    private string _currentSourceCode = string.Empty;
    private GraphNode? _currentNode;

    public SourceCodePreviewView()
    {
        InitializeComponent();

        SettingsService.Instance.SettingsChanged += (_, _) =>
        {
            if (_currentNode != null)
            {
                ShowSourceCode(_currentNode);
            }
        };
    }

    /// <summary>
    /// Mostra lo stato vuoto del visualizzatore di codice sorgente quando nessun simbolo è selezionato.
    /// </summary>
    public void ShowEmpty(bool hasProject = true)
    {
        _currentNode = null;
        _currentSourceCode = string.Empty;
        EmptyPanel.IsVisible = hasProject;
        ContentPanel.IsVisible = false;
        LanguageBadge.IsVisible = false;
        LocBadge.IsVisible = false;
        TruncatedNoticeText.IsVisible = false;
    }

    /// <summary>
    /// Visualizza il codice sorgente estratto dal file per il nodo specificato.
    /// </summary>
    public void ShowSourceCode(GraphNode node)
    {
        _currentNode = node;
        EmptyPanel.IsVisible = false;
        ContentPanel.IsVisible = true;

        var localizationService = LocalizationService.Instance;
        var sym = node.SemanticSymbol;

        // Badge Linguaggio
        var (langBadge, langBg, langFg, langTip) = ExplorerTreeItem.GetLanguageBadgeInfo(node.Language);
        if (!string.IsNullOrEmpty(langBadge))
        {
            LanguageBadge.IsVisible = true;
            LanguageBadgeText.Text = langBadge;
            LanguageBadge.Background = Brush.Parse(langBg ?? "#2563EB");
            LanguageBadgeText.Foreground = Brush.Parse(langFg ?? "#FFFFFF");
            ToolTip.SetTip(LanguageBadge, langTip);
        }
        else
        {
            LanguageBadge.IsVisible = false;
        }

        if (sym == null || string.IsNullOrEmpty(sym.FilePath))
        {
            LocBadge.IsVisible = false;
            FilePathText.Text = localizationService["CodePreview_NoFile"];
            LineNumbersText.Text = "";
            CodeText.Text = "";
            TruncatedNoticeText.IsVisible = false;
            _currentSourceCode = string.Empty;
            return;
        }

        var filePath = sym.FilePath;
        var fileName = Path.GetFileName(filePath);
        var startLine = sym.LineStart;
        var endLine = sym.LineEnd;
        var hasValidLines = startLine > 0;
        var loc = (hasValidLines && endLine >= startLine) ? (endLine - startLine + 1) : (hasValidLines ? 1 : 0);

        if (!File.Exists(filePath))
        {
            LocBadge.IsVisible = false;
            FilePathText.Text = $"{fileName} - {localizationService["CodePreview_FileNotFound"]}";
            LineNumbersText.Text = "";
            CodeText.Text = localizationService["CodePreview_FileNotFound"];
            TruncatedNoticeText.IsVisible = false;
            _currentSourceCode = string.Empty;
            return;
        }

        // Informazioni File e Conteggio Righe
        if (hasValidLines && endLine >= startLine)
        {
            FilePathText.Text = $"{fileName} • {string.Format(localizationService["CodePreview_Lines_Format"], startLine, endLine, loc)}";
            LocBadge.IsVisible = true;
            LocBadgeText.Text = $"{loc} {localizationService["Unit_Lines"]}";
        }
        else if (hasValidLines)
        {
            FilePathText.Text = $"{fileName} • {string.Format(localizationService["CodePreview_Lines_Single"], startLine)}";
            LocBadge.IsVisible = true;
            LocBadgeText.Text = $"1 {localizationService["Unit_Lines_Single"]}";
        }
        else
        {
            FilePathText.Text = fileName;
            LocBadge.IsVisible = false;
        }

        // Lettura righe dal file
        try
        {
            var lines = TryReadFileLines(filePath);
            if (lines == null || lines.Length == 0)
            {
                LineNumbersText.Text = "";
                CodeText.Text = "";
                TruncatedNoticeText.IsVisible = false;
                _currentSourceCode = string.Empty;
                return;
            }

            var startIdx = hasValidLines ? Math.Max(0, startLine - 1) : 0;
            var endIdx = (hasValidLines && endLine >= startLine)
                ? Math.Min(lines.Length - 1, endLine - 1)
                : (hasValidLines ? Math.Min(lines.Length - 1, startIdx) : lines.Length - 1);

            if (startIdx >= lines.Length)
            {
                LineNumbersText.Text = "";
                CodeText.Text = "";
                TruncatedNoticeText.IsVisible = false;
                _currentSourceCode = string.Empty;
                return;
            }

            var totalLinesToExtract = endIdx - startIdx + 1;
            var maxDisplayLines = Math.Max(50, SettingsService.Instance.Settings.MaxCodePreviewLines);
            var displayCount = Math.Min(totalLinesToExtract, maxDisplayLines);

            var lineNumbersBuilder = new StringBuilder();
            var codeBuilder = new StringBuilder();
            var rawSnippetBuilder = new StringBuilder();

            for (int i = 0; i < totalLinesToExtract; i++)
            {
                var lineIdx = startIdx + i;
                var currentLineNum = lineIdx + 1;
                var lineContent = lines[lineIdx];

                rawSnippetBuilder.AppendLine(lineContent);

                if (i < displayCount)
                {
                    lineNumbersBuilder.AppendLine(currentLineNum.ToString());
                    codeBuilder.AppendLine(lineContent);
                }
            }

            _currentSourceCode = rawSnippetBuilder.ToString();

            LineNumbersText.Text = lineNumbersBuilder.ToString().TrimEnd('\r', '\n');
            CodeText.Text = codeBuilder.ToString().TrimEnd('\r', '\n');

            if (totalLinesToExtract > maxDisplayLines)
            {
                TruncatedNoticeText.IsVisible = true;
                TruncatedNoticeText.Text = string.Format(
                    localizationService["CodePreview_Truncated"],
                    maxDisplayLines,
                    totalLinesToExtract);
            }
            else
            {
                TruncatedNoticeText.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            LineNumbersText.Text = "";
            CodeText.Text = $"Error: {ex.Message}";
            TruncatedNoticeText.IsVisible = false;
            _currentSourceCode = string.Empty;
        }
    }

    /// <summary>
    /// Aggiorna i testi localizzati del controllo.
    /// </summary>
    public void UpdateLocalizedUI()
    {
        var localizationService = LocalizationService.Instance;

        HeaderTitleText.Text = localizationService["CodePreview_Title"];
        EmptyTitleText.Text = localizationService["CodePreview_NoSelection"];
        EmptyHintText.Text = localizationService["CodePreview_EmptyHint"];
        ToolTip.SetTip(CopyCodeButton, localizationService["CodePreview_Tooltip_Copy"]);
        ToolTip.SetTip(OpenFileButton, localizationService["CodePreview_Tooltip_OpenFile"]);
        ToolTip.SetTip(OpenFolderButton, localizationService["CodePreview_Tooltip_OpenFolder"]);

        if (_currentNode != null)
        {
            ShowSourceCode(_currentNode);
        }
    }

    private static string[]? TryReadFileLines(string filePath)
    {
        try
        {
            return File.ReadAllLines(filePath);
        }
        catch
        {
            try
            {
                return File.ReadAllLines(filePath, Encoding.Latin1);
            }
            catch
            {
                return null;
            }
        }
    }

    private void OnOpenFileClicked(object? sender, RoutedEventArgs e)
    {
        OpenFileRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        OpenFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentSourceCode)) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(_currentSourceCode);
            }
        }
        catch
        {
            // Ignore clipboard errors
        }

        CopyCodeRequested?.Invoke(this, _currentSourceCode);
    }
}
