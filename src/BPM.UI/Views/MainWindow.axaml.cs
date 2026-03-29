using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using BPM.Core.Models;
using BPM.Core.Services;
using BPM.UI.ViewModels;
using ReactiveUI;

namespace BPM.UI.Views;

public partial class MainWindow : ReactiveWindow<DashboardViewModel>
{
    private WindowNotificationManager? _notificationManager;
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (_initialized) return;
        _initialized = true;

        _notificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 5
        };

        var settingsButton = this.FindControl<Button>("SettingsButton");
        if (settingsButton != null)
        {
            settingsButton.Click += (_, _) => OpenSettings();
        }

        var compactButton = this.FindControl<Button>("CompactButton");
        if (compactButton != null)
        {
            compactButton.Click += (_, _) =>
            {
                if (Avalonia.Application.Current is App app)
                    app.ShowCompactMode();
            };
        }

        // Wire up DataGrid selection for detail panels
        var actionsGrid = this.FindControl<DataGrid>("ActionsDataGrid");
        if (actionsGrid != null)
        {
            actionsGrid.SelectionChanged += OnActionsSelectionChanged;
        }

        var prGrid = this.FindControl<DataGrid>("PrDataGrid");
        if (prGrid != null)
        {
            prGrid.SelectionChanged += OnPrSelectionChanged;
        }

        if (ViewModel != null)
        {
            ViewModel.PendingNotifications.CollectionChanged += OnPendingNotificationsChanged;

            // Clear DataGrid selection when detail panels are hidden (e.g. on refresh)
            ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(DashboardViewModel.IsActionDetailVisible) && !ViewModel.IsActionDetailVisible)
                {
                    var grid = this.FindControl<DataGrid>("ActionsDataGrid");
                    if (grid != null) grid.SelectedItem = null;
                }
                else if (args.PropertyName == nameof(DashboardViewModel.IsPrDetailVisible) && !ViewModel.IsPrDetailVisible)
                {
                    var grid = this.FindControl<DataGrid>("PrDataGrid");
                    if (grid != null) grid.SelectedItem = null;
                }
            };
        }
    }

    private async void OnActionsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel == null) return;
        if (sender is DataGrid grid && grid.SelectedItem is ActionRunInfo run)
        {
            await ViewModel.LoadActionAnnotationsAsync(run);
        }
    }

    private async void OnPrSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel == null) return;
        if (sender is DataGrid grid && grid.SelectedItem is PullRequestInfo pr)
        {
            await ViewModel.LoadPrCommentsAsync(pr);
        }
    }

    private void OnPendingNotificationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null) return;

        foreach (NotificationService.NotificationEvent notification in e.NewItems)
        {
            _notificationManager?.Show(new Notification(
                notification.Title,
                notification.Message,
                NotificationType.Information));
        }

        ViewModel?.PendingNotifications.Clear();
    }

    private static readonly Regex UrlRegex = new(@"https?://[^\s<>""]+", RegexOptions.Compiled);

    private void OnAnnotationMessageLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not TextBlock tb) return;
        var message = tb.Tag as string ?? string.Empty;
        tb.Inlines?.Clear();

        int lastIndex = 0;
        foreach (Match match in UrlRegex.Matches(message))
        {
            if (match.Index > lastIndex)
            {
                tb.Inlines?.Add(new Run(message[lastIndex..match.Index]));
            }

            var url = match.Value;
            var link = new InlineUIContainer
            {
                Child = new TextBlock
                {
                    Text = url,
                    Foreground = Brushes.DodgerBlue,
                    TextDecorations = TextDecorations.Underline,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    FontSize = 12,
                    Tag = url
                }
            };
            ((TextBlock)link.Child).PointerPressed += (_, _) =>
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "http" || uri.Scheme == "https"))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            };
            tb.Inlines?.Add(link);
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < message.Length)
        {
            tb.Inlines?.Add(new Run(message[lastIndex..]));
        }

        // Fallback: if no inlines were added (no URLs), just set text
        if (tb.Inlines?.Count == 0)
        {
            tb.Text = message;
        }
    }

    private void OpenSettings()
    {
        if (ViewModel == null) return;

        var configService = App.Services.GetService(typeof(ConfigService)) as ConfigService;
        if (configService == null) return;

        var settingsVm = new SettingsViewModel(configService);
        var settingsWindow = new SettingsWindow
        {
            DataContext = settingsVm
        };
        settingsWindow.ShowDialog(this);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
    }
}
