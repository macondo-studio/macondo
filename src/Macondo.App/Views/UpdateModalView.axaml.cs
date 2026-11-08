using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Macondo.App.Models;
using Macondo.App.Services;

namespace Macondo.App.Views;

/// <summary>
/// Finestra modale per la notifica e il download dei nuovi aggiornamenti software.
/// </summary>
public partial class UpdateModalView : UserControl
{
    private AppUpdateInfo? _availableUpdate;

    /// <summary>
    /// Evento scatenato quando l'utente sceglie di scaricare/installare l'aggiornamento.
    /// </summary>
    public event EventHandler<AppUpdateInfo>? UpdateNowRequested;

    /// <summary>
    /// Evento scatenato quando l'utente sceglie di posticipare l'aggiornamento.
    /// </summary>
    public event EventHandler? UpdateLaterRequested;

    /// <summary>
    /// Inizializza una nuova istanza di <see cref="UpdateModalView"/>.
    /// </summary>
    public UpdateModalView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Mostra la modale popolandola con le informazioni della release disponibile.
    /// </summary>
    public void Show(AppUpdateInfo updateInfo)
    {
        _availableUpdate = updateInfo;
        UpdateLocalizedUI();
        IsVisible = true;
    }

    /// <summary>
    /// Nasconde la modale di aggiornamento.
    /// </summary>
    public void Hide()
    {
        IsVisible = false;
        _availableUpdate = null;
    }

    /// <summary>
    /// Aggiorna i testi localizzati della modale.
    /// </summary>
    public void UpdateLocalizedUI()
    {
        if (_availableUpdate == null) return;

        var localizationService = LocalizationService.Instance;
        UpdateModalTitle.Text = localizationService["Update_AvailableTitle"];
        UpdateModalSubtitle.Text = localizationService.Get("Update_AvailableSubtitle", _availableUpdate.VersionString, UpdateService.CurrentVersionString);
        UpdateModalNotesLabel.Text = localizationService["Update_NotesLabel"];
        UpdateModalNotes.Text = _availableUpdate.GetReleaseNotes(localizationService.CurrentLanguage == AppLanguage.Italian ? "it" : "en");
        UpdateLaterButton.Content = localizationService["Update_ButtonLater"];
        UpdateNowButton.Content = localizationService["Update_ButtonNow"];
    }

    private void OnUpdateNowClicked(object? sender, RoutedEventArgs e)
    {
        if (_availableUpdate != null)

            UpdateNowRequested?.Invoke(this, _availableUpdate);
    }

    private void OnUpdateLaterClicked(object? sender, RoutedEventArgs e)
    {
        Hide();
        UpdateLaterRequested?.Invoke(this, EventArgs.Empty);
    }
}
