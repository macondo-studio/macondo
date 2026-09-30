using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Macondo.App.Models;
using Macondo.App.Services;

namespace Macondo.App.Controls;

/// <summary>
/// Controllo placeholder visualizzato all'interno delle schede analitiche quando la
/// pre-elaborazione automatica in background è disabilitata e l'analisi non è ancora stata eseguita.
/// Permette all'utente di avviare l'analisi on-demand per la singola scheda o per tutte le schede rimanenti.
/// </summary>
public sealed class AnalyticsPlaceholderControl : UserControl
{
    public event EventHandler? StartAnalysisRequested;
    public event EventHandler? StartAllRequested;

    private readonly Border _card;
    private readonly Border _iconCircle;
    private readonly PathIcon _pathIcon;
    private readonly TextBlock _titleText;
    private readonly TextBlock _descText;
    private readonly Button _startButton;
    private readonly PathIcon _playIcon;
    private readonly TextBlock _startBtnText;
    private readonly Button _startAllButton;
    private readonly TextBlock _startAllBtnText;

    private readonly StackPanel _cardActionPanel;
    private readonly StackPanel _computingContainer;
    private readonly TextBlock _computingText;
    private readonly ProgressBar _progressBar;

    public AnalyticsPlaceholderControl()
    {
        // Contenitore principale a tutto schermo con sfondo semi-trasparente elegante
        var rootGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(220, 20, 20, 28))
        };

        // Scheda centrale
        _card = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(28, 24),
            MaxWidth = 480
        };

        var contentStack = new StackPanel
        {
            Spacing = 14,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        // Icona in cerchio con accento
        _iconCircle = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            BorderThickness = new Thickness(1.5)
        };

        _pathIcon = new PathIcon
        {
            Width = 20,
            Height = 20,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        _iconCircle.Child = _pathIcon;
        contentStack.Children.Add(_iconCircle);

        // Titolo
        _titleText = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        contentStack.Children.Add(_titleText);

        // Descrizione
        _descText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 420
        };
        contentStack.Children.Add(_descText);

        // Pannello Azioni (Avvia Analisi / Avvia Tutte)
        _cardActionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        };

        // Pulsante Primario "Avvia Analisi"
        _startButton = new Button
        {
            Padding = new Thickness(16, 8),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            BorderThickness = new Thickness(0)
        };
        var startBtnStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        _playIcon = new PathIcon
        {
            Width = 12,
            Height = 12,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        _startBtnText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        startBtnStack.Children.Add(_playIcon);
        startBtnStack.Children.Add(_startBtnText);
        _startButton.Content = startBtnStack;
        _startButton.Click += (_, _) => StartAnalysisRequested?.Invoke(this, EventArgs.Empty);

        // Pulsante Secondario "Avvia Tutte le Analisi"
        _startAllButton = new Button
        {
            Padding = new Thickness(14, 8),
            CornerRadius = new CornerRadius(6),
            Cursor = new Cursor(StandardCursorType.Hand),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            FontSize = 11
        };
        _startAllBtnText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        _startAllButton.Content = _startAllBtnText;
        _startAllButton.Click += (_, _) => StartAllRequested?.Invoke(this, EventArgs.Empty);

        _cardActionPanel.Children.Add(_startButton);
        _cardActionPanel.Children.Add(_startAllButton);
        contentStack.Children.Add(_cardActionPanel);

        // Pannello di elaborazione (in corso)
        _computingContainer = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
            IsVisible = false
        };

        _progressBar = new ProgressBar
        {
            Width = 220,
            Height = 4,
            IsIndeterminate = true,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _computingText = new TextBlock
        {
            FontSize = 12,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _computingContainer.Children.Add(_progressBar);
        _computingContainer.Children.Add(_computingText);
        contentStack.Children.Add(_computingContainer);

        _card.Child = contentStack;
        rootGrid.Children.Add(_card);
        Content = rootGrid;

        RefreshTheme();
        UpdateLocalizedUI();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RefreshTheme();
    }

    private IBrush GetBrush(string key, IBrush fallback)
    {
        if (this.TryFindResource(key, out var localRes) && localRes is IBrush localBrush)
            return localBrush;
        if (Application.Current != null && Application.Current.TryFindResource(key, out var appRes) && appRes is IBrush appBrush)
            return appBrush;
        return fallback;
    }

    private StreamGeometry? GetGeometry(string key)
    {
        if (this.TryFindResource(key, out var localRes) && localRes is StreamGeometry localGeo)
            return localGeo;
        if (Application.Current != null && Application.Current.TryFindResource(key, out var appRes) && appRes is StreamGeometry appGeo)
            return appGeo;
        return null;
    }

    private void RefreshTheme()
    {
        var surfaceBrush = GetBrush("ThemeBackgroundSurfaceBrush", new SolidColorBrush(Color.FromRgb(30, 30, 36)));
        var borderBrush = GetBrush("ThemeBorderDefaultBrush", new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)));
        var elevatedBrush = GetBrush("ThemeBackgroundElevatedBrush", new SolidColorBrush(Color.FromRgb(38, 38, 48)));
        var accentBrush = GetBrush("ThemeAccentBrush", new SolidColorBrush(Color.FromRgb(0, 122, 255)));
        var textPrimaryBrush = GetBrush("ThemeTextPrimaryBrush", new SolidColorBrush(Color.FromRgb(240, 240, 240)));
        var textSecondaryBrush = GetBrush("ThemeTextSecondaryBrush", new SolidColorBrush(Color.FromRgb(160, 160, 160)));

        _card.Background = surfaceBrush;
        _card.BorderBrush = borderBrush;

        _iconCircle.Background = elevatedBrush;
        _iconCircle.BorderBrush = accentBrush;

        var zapGeo = GetGeometry("IconZap");
        if (zapGeo != null) _pathIcon.Data = zapGeo;
        _pathIcon.Foreground = accentBrush;

        _titleText.Foreground = textPrimaryBrush;
        _descText.Foreground = textSecondaryBrush;

        _startButton.Background = accentBrush;

        var playGeo = GetGeometry("IconPlay");
        if (playGeo != null) _playIcon.Data = playGeo;

        _startAllButton.BorderBrush = borderBrush;
        _startAllButton.Foreground = textPrimaryBrush;

        _progressBar.Foreground = accentBrush;
        _computingText.Foreground = textSecondaryBrush;
    }

    public void SetState(bool isPending, bool isComputing)
    {
        if (!isPending && !isComputing)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;
        if (isComputing)
        {
            _cardActionPanel.IsVisible = false;
            _computingContainer.IsVisible = true;
        }
        else
        {
            _cardActionPanel.IsVisible = true;
            _computingContainer.IsVisible = false;
        }
    }

    public void UpdateLocalizedUI()
    {
        var loc = LocalizationService.Instance;
        _titleText.Text = loc["Analytics_NotComputedTitle"];
        _descText.Text = loc["Analytics_NotComputedDesc"];
        _startBtnText.Text = loc["Analytics_StartButton"];
        _startAllBtnText.Text = loc["Analytics_StartAllButton"];
        _computingText.Text = loc["Analytics_Computing"];
    }

    public void ApplyTheme(AppTheme theme)
    {
        RefreshTheme();
    }
}
