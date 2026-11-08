using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Macondo.App.Models;
using Macondo.App.Services;

namespace Macondo.App.Controls;

/// <summary>
/// Controllo placeholder visualizzato all'interno delle schede analitiche quando la
/// pre-elaborazione automatica in background è disabilitata e l'analisi non è ancora stata eseguita.
/// Permette all'utente di avviare l'analisi on-demand per la singola scheda o per tutte le schede rimanenti,
/// fornendo durante il calcolo un feedback visivo accurato con barra di progresso e stima temporale.
/// </summary>
public partial class AnalyticsPlaceholderControl : UserControl
{
    public event EventHandler? StartAnalysisRequested;
    public event EventHandler? StartAllRequested;

    private readonly DispatcherTimer _progressTimer;
    private readonly Stopwatch _stopwatch = new();
    private double _estimatedDurationSeconds = 1.2;
    private bool _isComputing;
    private string? _customComputingTitle;

    public AnalyticsPlaceholderControl()
    {
        InitializeComponent();

        _progressTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40) // ~25 FPS
        };
        _progressTimer.Tick += OnProgressTimerTick;

        var startBtn = this.FindControl<Button>("StartButton");
        if (startBtn != null)

            startBtn.Click += (_, _) => StartAnalysisRequested?.Invoke(this, EventArgs.Empty);

        var startAllBtn = this.FindControl<Button>("StartAllButton");
        if (startAllBtn != null)

            startAllBtn.Click += (_, _) => StartAllRequested?.Invoke(this, EventArgs.Empty);

        UpdateLocalizedUI();
    }

    /// <summary>
    /// Imposta lo stato visivo del controllo placeholder (In attesa di avvio, In elaborazione con stima, o Completato).
    /// </summary>
    /// <param name="isPending">True se l'analisi non è ancora stata eseguita.</param>
    /// <param name="isComputing">True se l'elaborazione è attivamente in corso in background.</param>
    /// <param name="estimatedSeconds">Durata stimata dell'analisi in secondi (opzionale, predefinita 1.0s).</param>
    /// <param name="customTitle">Titolo personalizzato per l'elaborazione (es. "Analisi Matrice DSM in corso...").</param>
    public void SetState(bool isPending, bool isComputing, double estimatedSeconds = 1.0, string? customTitle = null)
    {
        if (!isPending && !isComputing)
        {
            _progressTimer.Stop();
            _stopwatch.Stop();
            _isComputing = false;
            IsVisible = false;
            return;
        }

        IsVisible = true;
        _isComputing = isComputing;
        _customComputingTitle = customTitle;
        _estimatedDurationSeconds = Math.Max(0.4, estimatedSeconds);

        var cardActionPanel = this.FindControl<StackPanel>("CardActionPanel");
        var computingContainer = this.FindControl<StackPanel>("ComputingContainer");
        var progressBar = this.FindControl<ProgressBar>("ComputeProgressBar");
        var percentText = this.FindControl<TextBlock>("ComputingPercentText");
        var computingText = this.FindControl<TextBlock>("ComputingText");
        var titleText = this.FindControl<TextBlock>("TitleText");
        var descText = this.FindControl<TextBlock>("DescText");

        var loc = LocalizationService.Instance;

        if (isComputing)
        {
            if (cardActionPanel != null) cardActionPanel.IsVisible = false;
            if (computingContainer != null) computingContainer.IsVisible = true;

            if (titleText != null)
                titleText.Text = !string.IsNullOrWhiteSpace(customTitle) ? customTitle : loc["Analytics_ComputingTitle"];

            if (descText != null)
                descText.Text = loc["Analytics_ComputingDesc"];

            if (progressBar != null) progressBar.Value = 0;
            if (percentText != null) percentText.Text = "0%";
            if (computingText != null)
                computingText.Text = string.Format(loc["Analytics_TimeRemaining"], _estimatedDurationSeconds);

            _stopwatch.Restart();
            _progressTimer.Start();
        }
        else
        {
            _progressTimer.Stop();
            _stopwatch.Stop();

            if (cardActionPanel != null) cardActionPanel.IsVisible = true;
            if (computingContainer != null) computingContainer.IsVisible = false;

            if (titleText != null) titleText.Text = loc["Analytics_NotComputedTitle"];
            if (descText != null) descText.Text = loc["Analytics_NotComputedDesc"];
        }
    }

    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        if (!_isComputing) return;

        var elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        var est = Math.Max(0.2, _estimatedDurationSeconds);

        // Curva progressiva asintotica fluida: raggiunge ~90% al tempo stimato e rallenta fino al 98% se richiede più tempo
        var ratio = elapsedSeconds / est;
        double progressPercent;

        if (ratio <= 1.0)

            progressPercent = 90.0 * (1.0 - Math.Pow(1.0 - ratio, 2.0));

        else
        {
            // Asintoto morbido verso il 98% in caso di computazioni più lunghe del previsto
            var overflow = ratio - 1.0;
            progressPercent = 90.0 + 8.0 * (1.0 - Math.Exp(-overflow * 1.5));
        }

        progressPercent = Math.Clamp(progressPercent, 1.0, 98.0);

        var remainingSeconds = Math.Max(0.1, est - elapsedSeconds);
        var loc = LocalizationService.Instance;

        var progressBar = this.FindControl<ProgressBar>("ComputeProgressBar");
        var percentText = this.FindControl<TextBlock>("ComputingPercentText");
        var computingText = this.FindControl<TextBlock>("ComputingText");

        if (progressBar != null) progressBar.Value = progressPercent;
        if (percentText != null) percentText.Text = $"{(int)progressPercent}%";
        if (computingText != null)

            computingText.Text = elapsedSeconds < est
                ? string.Format(loc["Analytics_TimeRemaining"], remainingSeconds)
                : loc["Analytics_Computing"];
    }

    public void UpdateLocalizedUI()
    {
        var loc = LocalizationService.Instance;
        var titleText = this.FindControl<TextBlock>("TitleText");
        var descText = this.FindControl<TextBlock>("DescText");
        var startBtnText = this.FindControl<Button>("StartButton");
        var startAllBtnText = this.FindControl<Button>("StartAllButton");
        var computingText = this.FindControl<TextBlock>("ComputingText");

        var startBtnTextBlock = this.FindControl<TextBlock>("StartBtnText");
        var startAllBtnTextBlock = this.FindControl<TextBlock>("StartAllBtnText");

        if (_isComputing)
        {
            if (titleText != null)
                titleText.Text = !string.IsNullOrWhiteSpace(_customComputingTitle)
                    ? _customComputingTitle
                    : loc["Analytics_ComputingTitle"];
            if (descText != null) descText.Text = loc["Analytics_ComputingDesc"];
            if (computingText != null) computingText.Text = loc["Analytics_Computing"];
        }
        else
        {
            if (titleText != null) titleText.Text = loc["Analytics_NotComputedTitle"];
            if (descText != null) descText.Text = loc["Analytics_NotComputedDesc"];
        }

        if (startBtnTextBlock != null) startBtnTextBlock.Text = loc["Analytics_StartButton"];
        if (startAllBtnTextBlock != null) startAllBtnTextBlock.Text = loc["Analytics_StartAllButton"];
    }

    public void ApplyTheme(AppTheme theme)
    {
        // I pennelli del tema sono collegati dinamicamente nel markup AXAML tramite DynamicResource
    }
}
