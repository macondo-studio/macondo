using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Macondo.App.Models;
using Macondo.App.Services;

namespace Macondo.App.Views;

/// <summary>
/// Controllo visuale per la schermata di benvenuto e stato iniziale (Welcome View / Empty State).
/// Gestisce l'avvio rapido, la card per il progetto recente in sospeso e la lista dei progetti recenti con badge di cache.
/// </summary>
public partial class WelcomeView : UserControl
{
    /// <summary>
    /// Evento generato quando l'utente richiede di aprire singoli file di progetto (.vbp, .frm, .bas, .cls).
    /// </summary>
    public event EventHandler? OpenProjectRequested;

    /// <summary>
    /// Evento generato quando l'utente richiede di aprire una cartella.
    /// </summary>
    public event EventHandler? OpenFolderRequested;

    /// <summary>
    /// Evento generato quando l'utente richiede di caricare le fixture demo.
    /// </summary>
    public event EventHandler? LoadFixturesRequested;

    /// <summary>
    /// Evento generato quando l'utente clicca su "Analizza Ora" nella card di progetto in sospeso.
    /// </summary>
    public event EventHandler<string>? PendingAnalyzeRequested;

    /// <summary>
    /// Evento generato quando l'utente seleziona un progetto recente dalla lista.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? RecentItemClicked;

    public WelcomeView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Aggiorna i contenuti dinamici della schermata di benvenuto (card di progetto in sospeso e lista recenti con badge cache).
    /// </summary>
    /// <param name="pendingFolder">Percorso opzionale (file o cartella) da mostrare nella card del progetto rilevato.</param>
    public void Refresh(string? pendingFolder = null)
    {
        var localizationService = LocalizationService.Instance;
        var settings = SettingsService.Instance.Settings;

        // --------------------------------------------------------------------------------------------
        // 1. Gestione card "Ultimo Progetto Rilevato" in sospeso (progetto senza cache pronta)
        // --------------------------------------------------------------------------------------------
        string? targetPending = pendingFolder;
        if (string.IsNullOrEmpty(targetPending))
        {
            // Cerca prioritariamente il primo elemento della cronologia RecentProjects
            if (settings.RecentProjects.Count > 0)
            {
                var topRecent = settings.RecentProjects[0];
                var firstPath = topRecent.Paths.FirstOrDefault();
                if (!string.IsNullOrEmpty(firstPath))
                {
                    // Calcola la cartella per verificare se esiste già una cache salvata su disco
                    var cacheFolder = Directory.Exists(firstPath)
                        ? firstPath
                        : (File.Exists(firstPath) ? Path.GetDirectoryName(firstPath) : null);

                    if (!string.IsNullOrEmpty(cacheFolder) && Directory.Exists(cacheFolder) && !ProjectCacheService.Instance.HasCache(cacheFolder))

                        targetPending = firstPath;
                }
            }

            // Se la cronologia non ha fornito un target senza cache, ricade sulla cartella salvata nelle impostazioni
            if (string.IsNullOrEmpty(targetPending) && !string.IsNullOrEmpty(settings.LastOpenedFolder) && Directory.Exists(settings.LastOpenedFolder) && !ProjectCacheService.Instance.HasCache(settings.LastOpenedFolder))

                targetPending = settings.LastOpenedFolder;
        }

        // Se è presente un progetto valido (cartella o file) privo di cache, visualizza la card con il pulsante di avvio analisi
        if (!string.IsNullOrEmpty(targetPending) && (Directory.Exists(targetPending) || File.Exists(targetPending)))
        {
            WelcomePendingCard.IsVisible = true;
            WelcomePendingPathText.Text = targetPending;
            WelcomePendingPathText.Tag = targetPending;
            WelcomePendingTitleText.Text = localizationService.Get("Welcome_PendingTitle");
            WelcomePendingBadgeText.Text = localizationService.Get("Welcome_CacheNeeded");
            WelcomePendingDescText.Text = localizationService.Get("Welcome_PendingNoCache");
            WelcomePendingAnalyzeButtonText.Text = localizationService.Get("Welcome_AnalyzeNow");
        }
        else
        {
            WelcomePendingCard.IsVisible = false;
        }

        // --------------------------------------------------------------------------------------------
        // 2. Popolamento lista progetti recenti con calcolo dinamico dello stato cache per ciascun elemento
        // --------------------------------------------------------------------------------------------
        var recentItems = new List<WelcomeRecentItem>();
        foreach (var recent in settings.RecentProjects)
        {
            var isFolder = recent.Paths.Count == 1 && Directory.Exists(recent.Paths[0]);
            var cacheFolder = isFolder ? recent.Paths[0] : (recent.Paths.Count == 1 && File.Exists(recent.Paths[0]) ? Path.GetDirectoryName(recent.Paths[0]) : null);
            var isCached = !string.IsNullOrEmpty(cacheFolder) && ProjectCacheService.Instance.HasCache(cacheFolder);

            var pathSummary = recent.Paths.Count == 1
                ? recent.Paths[0]
                : string.Join(", ", recent.Paths);

            var badgeText = isCached ? localizationService["Welcome_CacheReady"] : localizationService["Welcome_CacheNeeded"];
            var badgeBg = isCached ? "#1B4D3E" : "#3D2A10";
            var badgeFg = isCached ? "#10B981" : "#FFB74D";
            var badgeTooltip = isCached ? localizationService["Welcome_CacheReadyTooltip"] : localizationService["Welcome_CacheNeededTooltip"];

            recentItems.Add(new WelcomeRecentItem
            {
                DisplayName = recent.DisplayName,
                PathSummary = pathSummary,
                LastOpenedText = recent.LastOpenedUtc.ToLocalTime().ToString("g"),
                Paths = recent.Paths,
                IsFolder = isFolder,
                IsCached = isCached,
                CacheBadgeText = badgeText,
                CacheBadgeBackground = badgeBg,
                CacheBadgeForeground = badgeFg,
                CacheBadgeTooltip = badgeTooltip
            });
        }

        WelcomeRecentItemsList.ItemsSource = recentItems;
        WelcomeNoRecentText.IsVisible = recentItems.Count == 0;
    }

    /// <summary>
    /// Aggiorna tutte le stringhe localizzate dell'interfaccia utente di benvenuto.
    /// </summary>
    public void UpdateLocalizedUI()
    {
        var localizationService = LocalizationService.Instance;

        WelcomeHeaderTitle.Text = localizationService["Welcome_Title"];
        WelcomeHeaderSub.Text = localizationService["Welcome_Subtitle"];
        WelcomeQuickStartTitle.Text = localizationService["Welcome_QuickStart"];
        WelcomeOpenProjectTitle.Text = localizationService["Welcome_OpenProject"];
        WelcomeOpenProjectSub.Text = localizationService["Welcome_OpenProjectDesc"];
        WelcomeOpenFolderTitle.Text = localizationService["Welcome_OpenFolder"];
        WelcomeOpenFolderSub.Text = localizationService["Welcome_OpenFolderDesc"];
        WelcomeLoadFixturesTitle.Text = localizationService["Welcome_LoadFixtures"];
        WelcomeLoadFixturesSub.Text = localizationService["Welcome_LoadFixturesDesc"];
        WelcomePendingTitleText.Text = localizationService["Welcome_PendingTitle"];
        WelcomePendingBadgeText.Text = localizationService["Welcome_CacheNeeded"];
        WelcomePendingDescText.Text = localizationService["Welcome_PendingNoCache"];
        WelcomePendingAnalyzeButtonText.Text = localizationService["Welcome_AnalyzeNow"];
        WelcomeRecentTitle.Text = localizationService["Welcome_RecentProjects"];
        WelcomeNoRecentText.Text = localizationService["Welcome_NoRecent"];

        Refresh();
    }

    /// <summary>
    /// Gestisce il click sul pulsante "Analizza ora" nella card di progetto in sospeso.
    /// </summary>
    private void OnWelcomePendingAnalyzeClicked(object? sender, RoutedEventArgs e)
    {
        if (WelcomePendingPathText.Tag is string path && !string.IsNullOrEmpty(path) && (Directory.Exists(path) || File.Exists(path)))

            PendingAnalyzeRequested?.Invoke(this, path);
    }

    /// <summary>
    /// Gestisce il click sull'opzione "Apri file progetto".
    /// </summary>
    private void OnWelcomeOpenProjectFilesClicked(object? sender, PointerPressedEventArgs e)
    {
        OpenProjectRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gestisce il click sull'opzione "Apri cartella".
    /// </summary>
    private void OnWelcomeOpenFolderClicked(object? sender, PointerPressedEventArgs e)
    {
        OpenFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gestisce il click sull'opzione "Carica fixture di test".
    /// </summary>
    private void OnWelcomeLoadFixturesClicked(object? sender, PointerPressedEventArgs e)
    {
        LoadFixturesRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gestisce il click su un elemento della lista dei progetti recenti.
    /// </summary>
    private void OnWelcomeRecentItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is WelcomeRecentItem item && item.Paths.Count > 0)

            RecentItemClicked?.Invoke(this, item.Paths);
    }
}
