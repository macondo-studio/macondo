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
/// Visual control for the welcome screen and initial state (Welcome View / Empty State).
/// Manages quick launch, card for pending recent project and list of recent projects with cache badges.
/// </summary>
public partial class WelcomeView : UserControl
{
    /// <summary>
    /// Event raised when user requests to open individual project files (.vbp, .frm, .bas, .cls).
    /// </summary>
    public event EventHandler? OpenProjectRequested;

    /// <summary>
    /// Event raised when the user requests to open a folder.
    /// </summary>
    public event EventHandler? OpenFolderRequested;

    /// <summary>
    /// Event generated when the user requests to load the demo fixtures.
    /// </summary>
    public event EventHandler? LoadFixturesRequested;

    /// <summary>
    /// Event generated when the user clicks on "Analyze Now" in the pending project card.
    /// </summary>
    public event EventHandler<string>? PendingAnalyzeRequested;

    /// <summary>
    /// Event raised when the user selects a recent project from the list.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? RecentItemClicked;

    public WelcomeView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Updates the dynamic contents of the welcome screen (pending project cards and recent list with cache badges).
    /// </summary>
    /// <param name="pendingFolder">Optional path (file or folder) to show on the card of the detected project.</param>
    public void Refresh(string? pendingFolder = null)
    {
        var localizationService = LocalizationService.Instance;
        var settings = SettingsService.Instance.Settings;

        // --------------------------------------------------------------------------------------------
        // 1. Management of pending "Last Project Detected" card (project without ready cache)
        // --------------------------------------------------------------------------------------------
        string? targetPending = pendingFolder;
        if (string.IsNullOrEmpty(targetPending))
        {
            // Search for the first item in the RecentProjects history as a priority
            if (settings.RecentProjects.Count > 0)
            {
                var topRecent = settings.RecentProjects[0];
                var firstPath = topRecent.Paths.FirstOrDefault();
                if (!string.IsNullOrEmpty(firstPath))
                {
                    // Calculate the folder to check if there is already a cache saved on disk
                    var cacheFolder = Directory.Exists(firstPath)
                        ? firstPath
                        : (File.Exists(firstPath) ? Path.GetDirectoryName(firstPath) : null);

                    if (!string.IsNullOrEmpty(cacheFolder) && Directory.Exists(cacheFolder) && !ProjectCacheService.Instance.HasCache(cacheFolder))

                        targetPending = firstPath;
                }
            }

            // If the history did not provide a cache-free target, it falls back to the folder saved in the settings
            if (string.IsNullOrEmpty(targetPending) && !string.IsNullOrEmpty(settings.LastOpenedFolder) && Directory.Exists(settings.LastOpenedFolder) && !ProjectCacheService.Instance.HasCache(settings.LastOpenedFolder))

                targetPending = settings.LastOpenedFolder;
        }

        // If there is a valid project (folder or file) without cache, view the card with the start analysis button
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
        // 2. Population of recent projects list with dynamic calculation of the cache status for each element
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
    /// Update all localized welcome UI strings.
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
    /// Manages the click on the "Analyze Now" button in the pending project card.
    /// </summary>
    private void OnWelcomePendingAnalyzeClicked(object? sender, RoutedEventArgs e)
    {
        if (WelcomePendingPathText.Tag is string path && !string.IsNullOrEmpty(path) && (Directory.Exists(path) || File.Exists(path)))

            PendingAnalyzeRequested?.Invoke(this, path);
    }

    /// <summary>
    /// Manages clicking on the "Open project file" option.
    /// </summary>
    private void OnWelcomeOpenProjectFilesClicked(object? sender, PointerPressedEventArgs e)
    {
        OpenProjectRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Handles clicking on the "Open folder" option.
    /// </summary>
    private void OnWelcomeOpenFolderClicked(object? sender, PointerPressedEventArgs e)
    {
        OpenFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Manages clicking on the "Load test fixture" option.
    /// </summary>
    private void OnWelcomeLoadFixturesClicked(object? sender, PointerPressedEventArgs e)
    {
        LoadFixturesRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Manages clicking on an item in the recent projects list.
    /// </summary>
    private void OnWelcomeRecentItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Control)?.DataContext is WelcomeRecentItem item && item.Paths.Count > 0)

            RecentItemClicked?.Invoke(this, item.Paths);
    }
}
