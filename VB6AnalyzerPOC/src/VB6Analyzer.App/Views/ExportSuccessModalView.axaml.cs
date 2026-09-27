using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Macondo.App.Services;

namespace Macondo.App.Views;

/// <summary>
/// Finestra modale di notifica a seguito di esportazioni di file su disco andate a buon fine.
/// </summary>
public partial class ExportSuccessModalView : UserControl
{
    private string? _fullPath;

    /// <summary>
    /// Evento scatenato quando l'utente richiede di aprire la cartella contenitrice del file esportato.
    /// </summary>
    public event EventHandler<string>? OpenFolderRequested;

    /// <summary>
    /// Evento scatenato quando la modale viene chiusa dall'utente.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Inizializza una nuova istanza di <see cref="ExportSuccessModalView"/>.
    /// </summary>
    public ExportSuccessModalView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Mostra la modale valorizzando il nome del file e il percorso completo.
    /// </summary>
    public void Show(string fileName, string fullPath)
    {
        _fullPath = fullPath;
        ExportSuccessFileName.Text = fileName;
        ExportSuccessFilePath.Text = fullPath;
        UpdateLocalizedUI();
        IsVisible = true;
    }

    /// <summary>
    /// Nasconde la modale.
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
        _fullPath = null;
    }

    /// <summary>
    /// Aggiorna i testi localizzati della modale.
    /// </summary>
    public void UpdateLocalizedUI()
    {
        var localizationService = LocalizationService.Instance;
        ExportSuccessModalTitle.Text = localizationService["Export_ModalTitle"];
        ExportSuccessModalSubtitle.Text = localizationService["Export_ModalSubtitle"];
        ExportSuccessFileLabel.Text = localizationService["Export_ModalFileLabel"];
        ExportSuccessPathLabel.Text = localizationService["Export_ModalPathLabel"];
        ExportSuccessOpenFolderButton.Content = localizationService["Export_ModalOpenFolder"];
        ExportSuccessCloseButton.Content = localizationService["Export_ModalOk"];
    }

    private void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_fullPath))
        {
            OpenFolderRequested?.Invoke(this, _fullPath);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Hide();
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
