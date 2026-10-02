using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Hardcodet.Wpf.TaskbarNotification;
using TodoWidget.Services;

namespace TodoWidget;

public partial class MainWindow : Window
{
    private readonly TodoService _todoService = new();
    private readonly SettingsService _settings = new();
    private readonly UpdateService _updateService = new();
    private TaskbarIcon? _trayIcon;
    private TileWindow? _tile;
    private bool _isPinned = true;
    private bool _isUrgentMode;
    private bool _showCompleted;

    // Drag visual
    private Window? _dragGhost;
    private TextBlock? _dragGhostText;
    private System.Windows.Threading.DispatcherTimer? _urgentTimer;
    private DateTime? _urgentStartTime;
    private System.Windows.Threading.DispatcherTimer? _deadlineCountdownTimer;

    public static SolidColorBrush CalculateHoverBrush(SolidColorBrush? baseBrush)
    {
        if (baseBrush == null) return Brushes.Transparent;
        var c = baseBrush.Color;
        double luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        int delta = luminance > 180 ? -0x1A : 0x1A;
        byte r = (byte)Math.Clamp(c.R + delta, 0, 255);
        byte g = (byte)Math.Clamp(c.G + delta, 0, 255);
        byte b = (byte)Math.Clamp(c.B + delta, 0, 255);
        var brush = new SolidColorBrush(Color.FromArgb(c.A, r, g, b));
        brush.Freeze();
        return brush;
    }

    private void UpdateDynamicHoverResources()
    {
        var emptyBrush = Application.Current.TryFindResource("bg/checkbox-empty") as SolidColorBrush;
        var filledBrush = Application.Current.TryFindResource("bg/checkbox-filled") as SolidColorBrush;
        var buttonBrush = (Application.Current.TryFindResource("bg/button") as SolidColorBrush) ?? filledBrush;

        Application.Current.Resources["bg/checkbox-empty/hover"] = CalculateHoverBrush(emptyBrush);
        Application.Current.Resources["bg/checkbox-filled/hover"] = CalculateHoverBrush(filledBrush);
        Application.Current.Resources["bg/button/hover"] = CalculateHoverBrush(buttonBrush);

        var accentBrush = (Application.Current.TryFindResource("fg/accent") as SolidColorBrush)
                          ?? buttonBrush
                          ?? filledBrush
                          ?? new SolidColorBrush(Color.FromRgb(31, 100, 169));
        Application.Current.Resources["fg/accent"] = accentBrush;
    }

    public MainWindow()
    {
        // Load saved theme
        var savedTheme = _settings.Theme;
        if (savedTheme == "Pixel-76")
            savedTheme = "Aeropixel";
        if (savedTheme == "Reilly")
            savedTheme = "Amber";
        if (savedTheme == "K in the night")
            savedTheme = "Nocturnal K";
        var validThemes = new[] { "Dark", "Light", "Kanagawa", "Argentina for Plemyannic", "Terminal", "Amber", "Pixel-76", "Aeropixel", "Syntwave", "Stormcloud", "Deep Antarctic", "Druid", "Hoarfrost", "Coalglow", "Nocturnal K", "Engraving K", "Graffity" };
        if (!validThemes.Contains(savedTheme))
            savedTheme = "Dark";

        Application.Current.Resources.MergedDictionaries.Add(
            new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Dark.xaml") });
        if (savedTheme != "Dark")
        {
            var encoded = savedTheme.Replace(" ", "%20");
            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{encoded}.xaml") });
        }
        _currentTheme = savedTheme;
        UpdateDynamicHoverResources();

        _isPinned = _settings.IsPinned;
        Topmost = _isPinned;

        InitializeComponent();

        if (OpacitySlider != null)
        {
            double savedOpacity = _settings.Opacity > 0.1 ? _settings.Opacity : 1.0;
            OpacitySlider.Value = savedOpacity;
            if (MainBorder != null) MainBorder.Opacity = savedOpacity;
            if (OutlineBorder != null) OutlineBorder.Opacity = savedOpacity;
            if (OpacityValueText != null) OpacityValueText.Text = $"{(int)(savedOpacity * 100)}%";
        }

        UpdateBackgroundImage(_currentTheme);
        UpdateThemeCheckmarks();
        Loaded += MainWindow_Loaded;
        Activated += MainWindow_Activated;
        Deactivated += MainWindow_Deactivated;

        // Check for urgent tasks BEFORE RefreshList so it filters correctly
        var urgentItem = _todoService.GetAll().FirstOrDefault(t => t.IsUrgent && !t.IsCompleted);
        if (urgentItem != null)
        {
            EnterUrgentMode(urgentItem);
        }
        else
        {
            _todoService.ClearUrgent();
        }
        RefreshList();

        SetupTrayIcon();
        SetupHeaderIcons();
        _updateService.Start();
        SetupDragVisuals();

        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };

        MouseEnter += (s, e) => AnimateScrollBars(true);
        MouseLeave += (s, e) =>
        {
            if (Mouse.Captured == null)
                AnimateScrollBars(false);
        };
        PreviewMouseUp += (s, e) =>
        {
            if (!IsMouseOver && MainBorder?.IsMouseOver != true)
                AnimateScrollBars(false);
        };

        if (MainBorder != null)
        {
            MainBorder.MouseEnter += (s, e) => AnimateScrollBars(true);
            MainBorder.MouseLeave += (s, e) =>
            {
                if (Mouse.Captured == null)
                    AnimateScrollBars(false);
            };
        }

        TasksScrollViewer.ScrollChanged += (s, e) =>
        {
            bool hasScroll = TasksScrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible;
            bool isHovered = IsMouseOver || MainBorder?.IsMouseOver == true;
            IsScrollBarVisible = isHovered && hasScroll;
        };
    }

    public static readonly DependencyProperty IsScrollBarVisibleProperty =
        DependencyProperty.Register(nameof(IsScrollBarVisible), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

    public bool IsScrollBarVisible
    {
        get => (bool)GetValue(IsScrollBarVisibleProperty);
        set => SetValue(IsScrollBarVisibleProperty, value);
    }

    private void SetupDragVisuals()
    {
        _dragGhostText = new TextBlock
        {
            FontSize = 13,
            MaxWidth = 220,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Padding = new Thickness(10, 6, 10, 6)
        };
        _dragGhostText.SetResourceReference(TextBlock.ForegroundProperty, "fg/primary");
        _dragGhostText.SetResourceReference(TextBlock.FontFamilyProperty, "FontRegular");

        var ghostBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = _dragGhostText
        };
        ghostBorder.SetResourceReference(Border.BackgroundProperty, "bg/task");
        ghostBorder.SetResourceReference(Border.BorderBrushProperty, "border/widget");
        ghostBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 12, ShadowDepth = 3, Opacity = 0.35, Color = Colors.Black
        };

        _dragGhost = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            IsHitTestVisible = false,
            Content = ghostBorder,
            SizeToContent = SizeToContent.WidthAndHeight,
            Opacity = 0.9
        };
    }

    private void ShowDragGhost(string text, Point windowPos)
    {
        if (_dragGhost == null || _dragGhostText == null) return;
        _dragGhostText.Text = text;

        try
        {
            _dragGhost.Left = this.Left + windowPos.X + 12;
            _dragGhost.Top = this.Top + windowPos.Y + 12;
            if (!_dragGhost.IsVisible) _dragGhost.Show();
        }
        catch { }
    }

    private void HideDragGhost()
    {
        _dragGhost?.Hide();
    }

    private void MainBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MainClip != null && MainBorder != null)
            MainClip.Rect = new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight);
    }

    private void SetupHeaderIcons()
    {
        UpdateHeaderIcons();
    }

    private void UpdateHeaderIcons()
    {
        if (ThemeButton != null)
            ThemeButton.Tag = _showingThemes ? "Active" : null;

        if (OpacityButton != null)
            OpacityButton.Tag = (OpacitySliderBorder?.Visibility == Visibility.Visible) ? "Active" : null;

        if (PinButton != null)
            PinButton.Tag = _isPinned ? "Active" : null;

        if (PinIconPath != null)
            PinIconPath.Data = (StreamGeometry)FindResource(_isPinned ? "PinIcon" : "PinOffIcon");
    }

    private void SetupTrayIcon()
    {
        var icon = GetTrayIcon(_isUrgentMode);
        _trayIcon = new TaskbarIcon { Icon = icon, ToolTipText = "Todo Widget" };

        var contextMenu = new ContextMenu();
        var showItem = new MenuItem { Header = "Показать" };
        showItem.Click += ShowMenuItem_Click;
        var exitItem = new MenuItem { Header = "Выход" };
        exitItem.Click += ExitMenuItem_Click;
        contextMenu.Items.Add(showItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);
        _trayIcon.ContextMenu = contextMenu;

        _trayIcon.TrayMouseDoubleClick += (s, e) => RestoreWidget();
    }

    private void AnimateScrollBars(bool visible)
    {
        bool hasScroll = TasksScrollViewer?.ComputedVerticalScrollBarVisibility == Visibility.Visible;
        IsScrollBarVisible = visible && hasScroll;

        double targetOpacity = visible ? 0.6 : 0.0;
        var duration = TimeSpan.FromMilliseconds(visible ? 250 : 350);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        AnimateScrollBar(TasksScrollViewer, targetOpacity, duration, ease);
        AnimateScrollBar(ThemesScrollViewer, targetOpacity, duration, ease);
    }

    private void AnimateScrollBar(ScrollViewer? scrollViewer, double targetOpacity, TimeSpan duration, IEasingFunction ease)
    {
        if (scrollViewer == null) return;
        scrollViewer.ApplyTemplate();
        var sb = (scrollViewer.Template?.FindName("PART_VerticalScrollBar", scrollViewer) as ScrollBar)
                 ?? FindChildByName<ScrollBar>(scrollViewer, "PART_VerticalScrollBar");
        if (sb != null)
        {
            if (sb.Tag == null)
            {
                sb.Tag = "Initialized";
                sb.MouseEnter += (s, e) =>
                {
                    var a = new DoubleAnimation(0.9, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
                    sb.BeginAnimation(UIElement.OpacityProperty, a);
                };
                sb.MouseLeave += (s, e) =>
                {
                    if (IsMouseOver || MainBorder?.IsMouseOver == true)
                    {
                        var a = new DoubleAnimation(0.6, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
                        sb.BeginAnimation(UIElement.OpacityProperty, a);
                    }
                };
            }

            var anim = new DoubleAnimation(targetOpacity, duration) { EasingFunction = ease };
            sb.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        MaxHeight = Math.Max(600, SystemParameters.WorkArea.Height - 40);
        if (_settings.Height >= MinHeight)
        {
            Height = Math.Min(_settings.Height, MaxHeight);
        }
        NewTaskInput.Focus();

        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (IsMouseOver || MainBorder?.IsMouseOver == true)
            {
                AnimateScrollBars(true);
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        double activeOpacity = OpacitySlider?.Value ?? 1.0;
        if (MainBorder != null) MainBorder.Opacity = activeOpacity;
        if (OutlineBorder != null) OutlineBorder.Opacity = activeOpacity;

        if (IsMouseOver || MainBorder?.IsMouseOver == true)
        {
            AnimateScrollBars(true);
        }
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        AnimateScrollBars(false);

        if (MainBorder != null && !_isDragging && !_isDraggingDeadline)
        {
            double baseOpacity = OpacitySlider?.Value ?? 1.0;
            double inactiveOpacity = Math.Max(0.15, baseOpacity * 0.4);
            MainBorder.Opacity = inactiveOpacity;
            if (OutlineBorder != null) OutlineBorder.Opacity = inactiveOpacity;
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _trayIcon?.Dispose();
        _tile?.Close();
        _deadlineCountdownTimer?.Stop();
    }

    // === Theme ===
    private bool _showingThemes;
    private string _currentTheme = "Dark";

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        _showingThemes = !_showingThemes;

        if (_showingThemes)
        {
            TasksView.Visibility = Visibility.Collapsed;
            ThemesView.Visibility = Visibility.Visible;
            HeaderTitle.Visibility = Visibility.Visible;
            HeaderTitle.Text = "Themes";
            TimerText.Visibility = Visibility.Collapsed;
            TaskCounter.Visibility = Visibility.Collapsed;
            UpdateThemeCheckmarks();
        }
        else
        {
            ThemesView.Visibility = Visibility.Collapsed;
            TasksView.Visibility = Visibility.Visible;

            if (_isUrgentMode)
            {
                HeaderTitle.Visibility = Visibility.Collapsed;
                TaskCounter.Visibility = Visibility.Collapsed;
                TimerText.Visibility = Visibility.Visible;
                UpdateTimerDisplay();
            }
            else
            {
                HeaderTitle.Visibility = Visibility.Visible;
                HeaderTitle.Text = "ADHD to-do";
                TaskCounter.Visibility = Visibility.Visible;
                TimerText.Visibility = Visibility.Collapsed;
            }
        }

        UpdateHeaderIcons();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            AnimateScrollBars(IsMouseOver || MainBorder?.IsMouseOver == true);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void DarkTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Dark");
    private void LightTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Light");
    private void KanagawaTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Kanagawa");
    private void ArgentinaTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Argentina for Plemyannic");
    private void TerminalTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Terminal");
    private void AmberTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Amber");
    private void Pixel76Theme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Aeropixel");
    private void SyntwaveTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Syntwave");
    private void StormcloudTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Stormcloud");
    private void DeepAntarcticTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Deep Antarctic");
    private void DruidTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Druid");
    private void HoarfrostTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Hoarfrost");
    private void CoalglowTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Coalglow");
    private void NocturnalKTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Nocturnal K");
    private void EngravingKTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Engraving K");
    private void GraffityTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Graffity");

    private void ApplyTheme(string theme)
    {
        _currentTheme = theme;
        _settings.Theme = theme;
        double savedOpacity = MainBorder?.Opacity ?? 1.0;
        var dicts = Application.Current.Resources.MergedDictionaries;

        // Remove all theme dictionaries
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            if (dicts[i].Source?.ToString().Contains("Themes/") == true)
                dicts.RemoveAt(i);
        }

        // Always load Dark.xaml first (base)
        dicts.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Themes/Dark.xaml") });

        // Load selected theme on top if not Dark
        if (theme != "Dark")
        {
            var encoded = theme.Replace(" ", "%20");
            dicts.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/Themes/{encoded}.xaml") });
        }

        UpdateDynamicHoverResources();

        if (MainBorder != null) MainBorder.Opacity = savedOpacity;
        if (OutlineBorder != null) OutlineBorder.Opacity = savedOpacity;

        UpdateBackgroundImage(theme);
        UpdateHeaderIcons();
        UpdateThemeCheckmarks();
    }

    private void UpdateBackgroundImage(string theme)
    {
        if (ThemeBackgroundImage == null) return;

        string? imgFile = theme switch
        {
            "Argentina for Plemyannic" => "bg_a.png",
            "Aeropixel" => "bg_aerop.png",
            "Pixel-76" => "bg_aerop.png",
            "Nocturnal K" => "bg_best.png",
            "Engraving K" => "bg_engr.png",
            "Graffity" => "bg_graf.png",
            _ => null
        };

        if (imgFile != null)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/Images/{imgFile}", UriKind.Absolute);
                ThemeBackgroundImage.Source = new BitmapImage(uri);
                ThemeBackgroundImage.Opacity = 1;
            }
            catch
            {
                ThemeBackgroundImage.Source = null;
                ThemeBackgroundImage.Opacity = 0;
            }
        }
        else
        {
            ThemeBackgroundImage.Source = null;
            ThemeBackgroundImage.Opacity = 0;
        }
    }

    private void UpdateThemeCheckmarks()
    {
        if (DarkCheck != null) DarkCheck.Opacity = _currentTheme == "Dark" ? 1 : 0;
        if (LightCheck != null) LightCheck.Opacity = _currentTheme == "Light" ? 1 : 0;
        if (KanagawaCheck != null) KanagawaCheck.Opacity = _currentTheme == "Kanagawa" ? 1 : 0;
        if (ArgentinaCheck != null) ArgentinaCheck.Opacity = _currentTheme == "Argentina for Plemyannic" ? 1 : 0;
        if (TerminalCheck != null) TerminalCheck.Opacity = _currentTheme == "Terminal" ? 1 : 0;
        if (AmberCheck != null) AmberCheck.Opacity = _currentTheme == "Amber" ? 1 : 0;
        if (Pixel76Check != null) Pixel76Check.Opacity = (_currentTheme == "Pixel-76" || _currentTheme == "Aeropixel") ? 1 : 0;
        if (SyntwaveCheck != null) SyntwaveCheck.Opacity = _currentTheme == "Syntwave" ? 1 : 0;
        if (StormcloudCheck != null) StormcloudCheck.Opacity = _currentTheme == "Stormcloud" ? 1 : 0;
        if (DeepAntarcticCheck != null) DeepAntarcticCheck.Opacity = _currentTheme == "Deep Antarctic" ? 1 : 0;
        if (DruidCheck != null) DruidCheck.Opacity = _currentTheme == "Druid" ? 1 : 0;
        if (HoarfrostCheck != null) HoarfrostCheck.Opacity = _currentTheme == "Hoarfrost" ? 1 : 0;
        if (CoalglowCheck != null) CoalglowCheck.Opacity = _currentTheme == "Coalglow" ? 1 : 0;
        if (NocturnalKCheck != null) NocturnalKCheck.Opacity = _currentTheme == "Nocturnal K" ? 1 : 0;
        if (EngravingKCheck != null) EngravingKCheck.Opacity = _currentTheme == "Engraving K" ? 1 : 0;
        if (GraffityCheck != null) GraffityCheck.Opacity = _currentTheme == "Graffity" ? 1 : 0;
    }

    // === Controls ===
    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        _settings.IsPinned = _isPinned;
        Topmost = _isPinned;
        _tile?.SetPinned(_isPinned);
        UpdateHeaderIcons();
    }

    private void OpacityButton_Click(object sender, RoutedEventArgs e)
    {
        OpacitySliderBorder.Visibility = OpacitySliderBorder.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        UpdateHeaderIcons();
    }

    private void OpacitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _settings.Opacity = e.NewValue;
        if (MainBorder != null)
            MainBorder.Opacity = e.NewValue;
        if (OutlineBorder != null)
            OutlineBorder.Opacity = e.NewValue;
        if (OpacityValueText != null)
            OpacityValueText.Text = $"{(int)(e.NewValue * 100)}%";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tile != null)
        {
            var tile = _tile;
            _tile = null;
            tile.Close();
        }
        _tile = new TileWindow(Left + Width / 2 - 16, Top + Height / 2 - 16, RestoreWidget, _isUrgentMode, _isPinned);
        _tile.Show();
        Hide();
    }

    public void RestoreWidget()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(RestoreWidget);
            return;
        }

        if (_tile != null)
        {
            var tile = _tile;
            _tile = null;
            double newLeft = tile.Left - Width / 2 + 16;
            double newTop = tile.Top - Height / 2 + 16;
            var screenWidth = SystemParameters.VirtualScreenWidth;
            var screenHeight = SystemParameters.VirtualScreenHeight;
            Left = Math.Clamp(newLeft, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + screenWidth - Width));
            Top = Math.Clamp(newTop, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + screenHeight - Height));
            tile.Close();
        }

        Show();
        WindowState = WindowState.Normal;
        Topmost = _isPinned;
        Activate();
        if (MainBorder != null && OpacitySlider != null)
            MainBorder.Opacity = OpacitySlider.Value;
        if (OutlineBorder != null) OutlineBorder.Opacity = MainBorder?.Opacity ?? 1.0;

        if (_showingThemes)
        {
            TasksView.Visibility = Visibility.Collapsed;
            ThemesView.Visibility = Visibility.Visible;
            HeaderTitle.Visibility = Visibility.Visible;
            HeaderTitle.Text = "Themes";
            TimerText.Visibility = Visibility.Collapsed;
            TaskCounter.Visibility = Visibility.Collapsed;
            UpdateThemeCheckmarks();
        }
        else if (_isUrgentMode)
        {
            ThemesView.Visibility = Visibility.Collapsed;
            TasksView.Visibility = Visibility.Visible;
            HeaderTitle.Visibility = Visibility.Collapsed;
            TaskCounter.Visibility = Visibility.Collapsed;
            TimerText.Visibility = Visibility.Visible;
            UpdateTimerDisplay();
        }
        else
        {
            ThemesView.Visibility = Visibility.Collapsed;
            TasksView.Visibility = Visibility.Visible;
            HeaderTitle.Visibility = Visibility.Visible;
            HeaderTitle.Text = "ADHD to-do";
            TaskCounter.Visibility = Visibility.Visible;
            TimerText.Visibility = Visibility.Collapsed;
        }

        UpdateHeaderIcons();
        StartDeadlineTimerIfNeeded();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            AnimateScrollBars(IsMouseOver || MainBorder?.IsMouseOver == true);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ShowMenuItem_Click(object sender, RoutedEventArgs e) => RestoreWidget();

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _trayIcon?.Dispose();
        Application.Current.Shutdown();
    }

    // === Tasks ===
    private void AddButton_Click(object sender, RoutedEventArgs e) => AddTask();

    private void NewTaskInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddTask();
    }

    private void NewTaskInput_GotFocus(object sender, RoutedEventArgs e)
    {
        WatermarkText.Visibility = Visibility.Collapsed;
    }

    private void NewTaskInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(NewTaskInput.Text))
            WatermarkText.Visibility = Visibility.Visible;
    }

    private void NewTaskInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        WatermarkText.Visibility = string.IsNullOrEmpty(NewTaskInput.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AddTask()
    {
        var text = NewTaskInput.Text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            _todoService.Add(text);
            RefreshList();
            AnimateNewTask();
            NewTaskInput.Text = string.Empty;
            WatermarkText.Visibility = Visibility.Visible;
        }
    }

    private void AnimateNewTask()
    {
        if (TodoList.Items.Count == 0) return;

        var lastItem = TodoList.Items.OfType<Models.TodoItem>().LastOrDefault();
        if (lastItem == null) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            var container = TodoList.ItemContainerGenerator.ContainerFromItem(lastItem) as FrameworkElement;
            if (container == null) return;

            var card = FindChildByName<Border>(container, "TaskCard");
            if (card == null) return;

            card.ClipToBounds = true;
            double targetHeight = card.ActualHeight > 0 ? card.ActualHeight : 38;
            var targetMargin = new Thickness(0, 0, 0, 6);

            card.Height = 0;
            card.Opacity = 0;
            card.Margin = new Thickness(0);

            var duration = TimeSpan.FromMilliseconds(180);
            var ease = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
            };

            var heightAnim = new System.Windows.Media.Animation.DoubleAnimation(0, targetHeight, duration) { EasingFunction = ease };
            var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration) { EasingFunction = ease };
            var marginAnim = new System.Windows.Media.Animation.ThicknessAnimation(new Thickness(0), targetMargin, duration) { EasingFunction = ease };

            heightAnim.Completed += (s, e) =>
            {
                card.BeginAnimation(FrameworkElement.HeightProperty, null);
                card.BeginAnimation(UIElement.OpacityProperty, null);
                card.BeginAnimation(FrameworkElement.MarginProperty, null);
                card.Height = double.NaN;
                card.Opacity = 1;
                card.Margin = targetMargin;
            };

            card.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
            card.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            card.BeginAnimation(FrameworkElement.MarginProperty, marginAnim);

            card.BringIntoView();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void TodoCheckbox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkbox && checkbox.Tag is string id)
        {
            var allBefore = _todoService.GetAll();
            var itemBefore = allBefore.FirstOrDefault(t => t.Id == id);
            bool wasUrgent = itemBefore?.IsUrgent == true;
            bool wasCompleted = itemBefore?.IsCompleted == true;
            int taskIndex = allBefore.Where(t => !t.IsCompleted).ToList().FindIndex(t => t.Id == id);

            if (wasCompleted)
            {
                // Unchecking a completed task: return it to active list immediately
                _todoService.Toggle(id);
                _todoService.MoveToTop(id);
                OnActiveTaskAddedToTop();
                RefreshList();
                return;
            }

            var card = FindAncestorOrSelf<Border>(checkbox);
            var strikeLine = card != null ? FindChildByName<Border>(card, "StrikeLine") : null;
            var titleText = card != null ? FindChildByName<TextBlock>(card, "TaskTitleText") : null;

            if (card != null && strikeLine != null)
            {
                card.IsHitTestVisible = false;

                // Animate strikethrough line
                var strikeScale = strikeLine.RenderTransform as ScaleTransform;
                if (strikeScale == null || strikeScale.IsFrozen)
                {
                    strikeScale = new ScaleTransform(0, 1);
                    strikeLine.RenderTransform = strikeScale;
                }

                var strikeDuration = TimeSpan.FromMilliseconds(220);
                var strikeEase = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                };

                var strikeAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 1, strikeDuration)
                {
                    EasingFunction = strikeEase
                };

                // Dim title text simultaneously
                if (titleText != null)
                {
                    var textDimAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.45, strikeDuration)
                    {
                        EasingFunction = strikeEase
                    };
                    titleText.BeginAnimation(UIElement.OpacityProperty, textDimAnim);
                }

                bool finished = false;
                Action completeTask = () =>
                {
                    if (finished) return;
                    finished = true;

                    if (taskIndex >= 0)
                    {
                        OnActiveTaskRemoved(taskIndex);
                    }
                    _todoService.Toggle(id);
                    _todoService.MoveToTop(id);

                    if (_isUrgentMode || wasUrgent)
                    {
                        _todoService.ClearUrgent();
                        ExitUrgentMode();
                    }
                    RefreshList();
                };

                strikeAnim.Completed += (s, ev) =>
                {
                    // Smoothly collapse the card after strikethrough finishes
                    card.ClipToBounds = true;
                    double initialHeight = card.ActualHeight;
                    card.Height = initialHeight;

                    var collapseDuration = TimeSpan.FromMilliseconds(180);
                    var collapseEase = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
                    };

                    var heightAnim = new System.Windows.Media.Animation.DoubleAnimation(initialHeight, 0, collapseDuration) { EasingFunction = collapseEase };
                    var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(card.Opacity, 0, collapseDuration) { EasingFunction = collapseEase };
                    var marginAnim = new System.Windows.Media.Animation.ThicknessAnimation(card.Margin, new Thickness(0), collapseDuration) { EasingFunction = collapseEase };

                    heightAnim.Completed += (s2, ev2) => completeTask();

                    card.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
                    card.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
                    card.BeginAnimation(FrameworkElement.MarginProperty, marginAnim);
                };

                var strikeContainer = strikeLine.Parent as Panel;
                if (titleText != null && titleText.ActualHeight > 25 && strikeContainer != null)
                {
                    strikeLine.Visibility = Visibility.Collapsed;
                    int lineCount = Math.Max(2, (int)Math.Round(titleText.ActualHeight / 19.0));
                    double lineHeight = titleText.ActualHeight / lineCount;

                    for (int i = 0; i < lineCount; i++)
                    {
                        var line = new Border
                        {
                            Height = 1.5,
                            Background = strikeLine.Background,
                            CornerRadius = new CornerRadius(0.75),
                            Opacity = 0.85,
                            VerticalAlignment = VerticalAlignment.Top,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Margin = new Thickness(0, (i + 0.5) * lineHeight - 0.75, 0, 0),
                            RenderTransformOrigin = new Point(0, 0.5),
                            IsHitTestVisible = false
                        };
                        var st = new ScaleTransform(0, 1);
                        line.RenderTransform = st;
                        strikeContainer.Children.Add(line);
                        st.BeginAnimation(ScaleTransform.ScaleXProperty, strikeAnim);
                    }
                }
                else
                {
                    strikeScale.BeginAnimation(ScaleTransform.ScaleXProperty, strikeAnim);
                }
            }
            else
            {
                if (taskIndex >= 0)
                {
                    OnActiveTaskRemoved(taskIndex);
                }
                _todoService.Toggle(id);
                _todoService.MoveToTop(id);

                if (_isUrgentMode || wasUrgent)
                {
                    _todoService.ClearUrgent();
                    ExitUrgentMode();
                }
                RefreshList();
            }
        }
    }

    private void DeleteText_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is TextBlock textBlock && textBlock.Tag is string id)
        {
            var allBefore = _todoService.GetAll();
            var itemBefore = allBefore.FirstOrDefault(t => t.Id == id);
            string? dlGroupId = itemBefore?.GroupDeadlineId;
            int taskIndex = allBefore.Where(t => !t.IsCompleted).ToList().FindIndex(t => t.Id == id);

            var card = FindAncestorOrSelf<Border>(textBlock);
            if (card != null)
            {
                card.IsHitTestVisible = false;

                bool removed = false;
                Action doRemove = () =>
                {
                    if (removed) return;
                    removed = true;
                    if (taskIndex >= 0)
                    {
                        OnActiveTaskRemoved(taskIndex);
                    }
                    if (!string.IsNullOrEmpty(dlGroupId))
                    {
                        bool wasLastInGroup = allBefore.Count(t => t.Id != id && t.GroupDeadlineId == dlGroupId) == 0;
                        if (wasLastInGroup)
                        {
                            RemoveDeadlineById(dlGroupId);
                        }
                    }
                    _todoService.Remove(id);
                    if (_isUrgentMode)
                    {
                        _todoService.ClearUrgent();
                        ExitUrgentMode();
                    }
                    RefreshList();
                };

                // === Effect 3: Ghost Dissolve (Растворение на месте с расфокусом) ===
                card.ClipToBounds = false;

                var blur = new System.Windows.Media.Effects.BlurEffect
                {
                    Radius = 0,
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
                };
                card.Effect = blur;

                var dissolveDuration = TimeSpan.FromMilliseconds(200);
                var blurEase = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                };
                var fadeEase = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn
                };

                var blurAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 14, dissolveDuration)
                {
                    EasingFunction = blurEase
                };
                var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(card.Opacity, 0, dissolveDuration)
                {
                    EasingFunction = fadeEase
                };

                opacityAnim.Completed += (s, ev) =>
                {
                    // Collapse the remaining vertical slot
                    card.ClipToBounds = true;
                    double initialHeight = card.ActualHeight;
                    card.Height = initialHeight;

                    var collapseDuration = TimeSpan.FromMilliseconds(150);
                    var collapseEase = new System.Windows.Media.Animation.CubicEase
                    {
                        EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
                    };

                    var heightAnim = new System.Windows.Media.Animation.DoubleAnimation(initialHeight, 0, collapseDuration) { EasingFunction = collapseEase };
                    var marginAnim = new System.Windows.Media.Animation.ThicknessAnimation(card.Margin, new Thickness(0), collapseDuration) { EasingFunction = collapseEase };

                    heightAnim.Completed += (s2, ev2) => doRemove();

                    card.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
                    card.BeginAnimation(FrameworkElement.MarginProperty, marginAnim);
                };

                blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnim);
                card.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            }
            else
            {
                if (taskIndex >= 0)
                {
                    OnActiveTaskRemoved(taskIndex);
                }
                if (!string.IsNullOrEmpty(dlGroupId))
                {
                    bool wasLastInGroup = allBefore.Count(t => t.Id != id && t.GroupDeadlineId == dlGroupId) == 0;
                    if (wasLastInGroup)
                    {
                        RemoveDeadlineById(dlGroupId);
                    }
                }
                _todoService.Remove(id);
                if (_isUrgentMode)
                {
                    _todoService.ClearUrgent();
                    ExitUrgentMode();
                }
                RefreshList();
            }
        }
    }

    private void TaskBorder_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            var todoItem = border.DataContext as Models.TodoItem;
            if (todoItem != null && !todoItem.IsCompleted)
            {
                var fire = FindChildByName<Border>(border, "FireBorder");
                if (fire != null) fire.Visibility = Visibility.Visible;
            }
        }
    }

    private void TaskBorder_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Border border)
        {
            var todoItem = border.DataContext as Models.TodoItem;
            if (todoItem != null && !todoItem.IsUrgent)
            {
                var fire = FindChildByName<Border>(border, "FireBorder");
                if (fire != null) fire.Visibility = Visibility.Collapsed;
            }
        }
    }

    private Models.TodoItem? _draggedItem;
    private Border? _draggedBorder;
    private bool _isDragging;
    private Point _dragStartPoint;
    private int _dropTargetSlot = -1;

    private static T? FindAncestorOrSelf<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private static FrameworkElement? FindAncestorByName(DependencyObject? current, string name)
    {
        while (current != null)
        {
            if (current is FrameworkElement fe && fe.Name == name) return fe;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private string? _editingTaskId;

    private void TaskBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        if (dep != null)
        {
            // Do not initiate drag or edit if user clicked CheckBox, FireBorder (Urgent), Delete (✕), or inside TextBox
            if (FindAncestorOrSelf<CheckBox>(dep) != null) return;
            if (FindAncestorByName(dep, "FireBorder") != null) return;
            if (dep is TextBlock tb && tb.Tag != null) return;
            if (FindAncestorOrSelf<TextBox>(dep) != null) return;
        }

        if (sender is Border border)
        {
            var item = border.DataContext as Models.TodoItem;
            if (item == null || item.IsCompleted) return;

            if (e.ClickCount == 2)
            {
                CancelDrag();
                BeginEditTask(border, item);
                e.Handled = true;
                return;
            }

            _draggedItem = item;
            _draggedBorder = border;
            _dragStartPoint = e.GetPosition(this);
            _isDragging = false;
        }
    }

    private void BeginEditTask(Border card, Models.TodoItem item)
    {
        var titleText = FindChildByName<TextBlock>(card, "TaskTitleText");
        var editBox = FindChildByName<TextBox>(card, "TaskEditBox");
        if (titleText == null || editBox == null) return;

        _editingTaskId = item.Id;
        titleText.Visibility = Visibility.Collapsed;
        editBox.Text = item.Title;
        editBox.Visibility = Visibility.Visible;
        editBox.Focus();
        editBox.SelectAll();
    }

    private void TaskEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is Models.TodoItem item)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitEditTask(textBox, item);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelEditTask();
            }
        }
    }

    private void TaskEditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is Models.TodoItem item)
        {
            CommitEditTask(textBox, item);
        }
    }

    private void CommitEditTask(TextBox textBox, Models.TodoItem item)
    {
        if (_editingTaskId == null || _editingTaskId != item.Id) return;
        _editingTaskId = null;

        var newTitle = textBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(newTitle) && newTitle != item.Title)
        {
            _todoService.UpdateTitle(item.Id, newTitle);
        }
        RefreshList();
    }

    private void CancelEditTask()
    {
        _editingTaskId = null;
        RefreshList();
    }

    private void TaskBorder_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedItem == null || _draggedBorder == null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CancelDrag();
            return;
        }

        var currentPos = e.GetPosition(this);
        double dx = Math.Abs(currentPos.X - _dragStartPoint.X);
        double dy = Math.Abs(currentPos.Y - _dragStartPoint.Y);

        if (!_isDragging && (dx > 5 || dy > 5))
        {
            if (TodoList.Items.Count <= 1 || _isUrgentMode)
                return;

            _isDragging = true;
            _draggedBorder.Opacity = 0.35;
            _draggedBorder.CaptureMouse();
        }

        if (_isDragging)
        {
            ShowDragGhost(_draggedItem.Title, currentPos);
            UpdateDropSlot(e);
        }
    }

    private void UpdateDropSlot(MouseEventArgs e)
    {
        if (DropIndicator == null || DropIndicatorTransform == null) return;
        int M = TodoList.Items.Count;
        if (M <= 1 || _draggedItem == null)
        {
            DropIndicator.Visibility = Visibility.Collapsed;
            _dropTargetSlot = -1;
            return;
        }

        int draggedIndex = -1;
        for (int i = 0; i < M; i++)
        {
            if (TodoList.Items[i] is Models.TodoItem item && item.Id == _draggedItem.Id)
            {
                draggedIndex = i;
                break;
            }
        }
        if (draggedIndex == -1)
        {
            DropIndicator.Visibility = Visibility.Collapsed;
            _dropTargetSlot = -1;
            return;
        }

        var cardMids = new double[M];
        var cardTops = new double[M];
        var cardBots = new double[M];

        for (int i = 0; i < M; i++)
        {
            var container = TodoList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container == null)
            {
                DropIndicator.Visibility = Visibility.Collapsed;
                _dropTargetSlot = -1;
                return;
            }
            var card = (container is Border b && b.Name == "TaskCard") ? b : FindChildByName<Border>(container, "TaskCard") ?? container;
            var topPt = card.TransformToAncestor(TodoListGrid).Transform(new Point(0, 0));
            cardTops[i] = topPt.Y;
            cardBots[i] = topPt.Y + card.ActualHeight;
            cardMids[i] = (cardTops[i] + cardBots[i]) / 2.0;
        }

        double mouseY = e.GetPosition(TodoListGrid).Y;
        int slot = 0;
        if (mouseY < cardMids[0]) slot = 0;
        else if (mouseY >= cardMids[M - 1]) slot = M;
        else
        {
            for (int i = 0; i < M - 1; i++)
            {
                if (mouseY >= cardMids[i] && mouseY < cardMids[i + 1])
                {
                    slot = i + 1;
                    break;
                }
            }
        }

        if (slot == draggedIndex || slot == draggedIndex + 1)
        {
            DropIndicator.Visibility = Visibility.Collapsed;
            _dropTargetSlot = -1;
            return;
        }

        _dropTargetSlot = slot;
        double lineY = (slot == 0) ? cardTops[0] - 3 : (slot == M) ? cardBots[M - 1] + 2 : (cardBots[slot - 1] + cardTops[slot]) / 2.0 - 0.5;
        DropIndicatorTransform.Y = Math.Max(0, lineY);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void TaskBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging && _draggedItem != null)
        {
            int M = TodoList.Items.Count;
            int draggedIndex = -1;
            for (int i = 0; i < M; i++)
            {
                if (TodoList.Items[i] is Models.TodoItem item && item.Id == _draggedItem.Id)
                {
                    draggedIndex = i;
                    break;
                }
            }

            if (draggedIndex >= 0 && _dropTargetSlot >= 0 && _dropTargetSlot != draggedIndex && _dropTargetSlot != draggedIndex + 1)
            {
                // Build otherItems (TodoList.Items excluding the dragged task)
                var otherItems = new List<object>();
                for (int i = 0; i < M; i++)
                {
                    if (i != draggedIndex) otherItems.Add(TodoList.Items[i]);
                }

                int insertIdx = (_dropTargetSlot <= draggedIndex) ? _dropTargetSlot : _dropTargetSlot - 1;
                var itemBefore = (insertIdx > 0 && insertIdx - 1 < otherItems.Count) ? otherItems[insertIdx - 1] : null;
                var itemAfter = (insertIdx >= 0 && insertIdx < otherItems.Count) ? otherItems[insertIdx] : null;

                // Determine newGroupId
                string? newGroupId = null;

                if (itemBefore is Models.DeadlineItem dlBefore)
                {
                    // Dropped right under a deadline line
                    if (itemAfter is Models.TodoItem tAfter && tAfter.GroupDeadlineId == dlBefore.Id)
                    {
                        newGroupId = dlBefore.Id;
                    }
                    else if (dlBefore.HasGroupedTasks)
                    {
                        newGroupId = dlBefore.Id;
                    }
                }
                else if (itemBefore is Models.TodoItem tBefore && !string.IsNullOrEmpty(tBefore.GroupDeadlineId))
                {
                    if (itemAfter is Models.TodoItem tAfter && tAfter.GroupDeadlineId == tBefore.GroupDeadlineId)
                    {
                        // Inside another or same group (Variant A: auto-absorption / same group reorder)
                        newGroupId = tBefore.GroupDeadlineId;
                    }
                    else if (!string.IsNullOrEmpty(_draggedItem.GroupDeadlineId) && _draggedItem.GroupDeadlineId == tBefore.GroupDeadlineId)
                    {
                        // Moving to the end of its own group
                        newGroupId = _draggedItem.GroupDeadlineId;
                    }
                }

                // Update task's group
                string? oldGroupId = _draggedItem.GroupDeadlineId;
                _draggedItem.GroupDeadlineId = newGroupId;
                _todoService.UpdateGroupDeadlineId(_draggedItem.Id, newGroupId);
                bool joinedGroup = !string.IsNullOrEmpty(newGroupId);
                string draggedId = _draggedItem.Id;

                // Move task in active todos
                Models.TodoItem? nextTask = null;
                for (int i = insertIdx; i < otherItems.Count; i++)
                {
                    if (otherItems[i] is Models.TodoItem t)
                    {
                        nextTask = t;
                        break;
                    }
                }

                if (nextTask != null)
                {
                    _todoService.MoveBefore(_draggedItem.Id, nextTask.Id);
                }
                else
                {
                    Models.TodoItem? prevTask = null;
                    for (int i = insertIdx - 1; i >= 0; i--)
                    {
                        if (otherItems[i] is Models.TodoItem t)
                        {
                            prevTask = t;
                            break;
                        }
                    }
                    if (prevTask != null)
                    {
                        _todoService.MoveAfter(_draggedItem.Id, prevTask.Id);
                    }
                }

                // Update deadline slots based on new visual list order
                var slots = _settings.DeadlineSlots;
                var newSlots = new List<int>();
                for (int s = 0; s < slots.Count; s++)
                {
                    string dlId = _settings.GetDeadlineId(s);
                    int dlIdxInOther = otherItems.FindIndex(it => it is Models.DeadlineItem dl && dl.Id == dlId);
                    if (dlIdxInOther >= 0)
                    {
                        int countTasksBefore = 0;
                        for (int k = 0; k < dlIdxInOther; k++)
                        {
                            if (otherItems[k] is Models.TodoItem) countTasksBefore++;
                        }
                        if (insertIdx <= dlIdxInOther) countTasksBefore++;
                        newSlots.Add(countTasksBefore);
                    }
                    else
                    {
                        newSlots.Add(slots[s]);
                    }
                }

                var lines = new List<(int slot, int originalIndex, string label, string? timerEnd, string id)>();
                for (int s = 0; s < slots.Count; s++)
                {
                    string id = _settings.GetDeadlineId(s);
                    string label = _settings.GetDeadlineLabel(s);
                    string? timerEnd = (s < _settings.DeadlineTimerEnds.Count) ? _settings.DeadlineTimerEnds[s] : null;
                    lines.Add((newSlots[s], s, label, timerEnd, id));
                }
                lines.Sort((a, b) => a.slot != b.slot ? a.slot.CompareTo(b.slot) : a.originalIndex.CompareTo(b.originalIndex));

                _settings.DeadlineSlots = lines.Select(x => x.slot).ToList();
                _settings.DeadlineLabels = lines.Select(x => x.label).ToList();
                _settings.DeadlineTimerEnds = lines.Select(x => x.timerEnd).ToList();
                _settings.DeadlineIds = lines.Select(x => x.id).ToList();

                RefreshList();
                if (joinedGroup)
                {
                    AnimateMagneticSnap(draggedId);
                }
            }
        }
        CancelDrag();
    }

    private void CancelDrag()
    {
        if (_draggedBorder != null)
        {
            _draggedBorder.Opacity = 1;
            _draggedBorder.ReleaseMouseCapture();
        }
        HideDragGhost();
        if (DropIndicator != null) DropIndicator.Visibility = Visibility.Collapsed;
        _dropTargetSlot = -1;
        _isDragging = false;
        _draggedItem = null;
        _draggedBorder = null;
    }

    // === Deadline Line Drag & Drop ===
    private bool _isDraggingDeadline;
    private Border? _draggedDeadlineBorder;
    private Models.DeadlineItem? _draggedDeadlineItem;
    private Point _deadlineDragStartPoint;
    private int _deadlineDropTargetSlot = -1;

    private void Deadline_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            if (FindAncestorByName(dep, "AddLineBtn") != null || FindAncestorByName(dep, "DeleteLineBtn") != null || FindAncestorByName(dep, "DeadlineTargetBtn") != null)
            {
                return;
            }
            if (FindAncestorOrSelf<TextBox>(dep) != null)
            {
                return;
            }
        }

        if (sender is Border border)
        {
            var item = border.DataContext as Models.DeadlineItem;
            if (e.ClickCount == 2)
            {
                CancelDeadlineDrag();
                BeginEditDeadline(border, item);
                e.Handled = true;
                return;
            }

            _draggedDeadlineBorder = border;
            _draggedDeadlineItem = item;
            _deadlineDragStartPoint = e.GetPosition(this);
            _isDraggingDeadline = false;
        }
    }

    private void SetGroupedTasksDragOpacity(string? deadlineId, double opacity)
    {
        if (TodoList == null || string.IsNullOrEmpty(deadlineId)) return;
        for (int i = 0; i < TodoList.Items.Count; i++)
        {
            if (TodoList.Items[i] is Models.TodoItem t && t.GroupDeadlineId == deadlineId)
            {
                var container = TodoList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container != null)
                {
                    var card = (container is Border b && b.Name == "TaskCard") ? b : FindChildByName<Border>(container, "TaskCard") ?? container;
                    card.Opacity = opacity;
                }
            }
        }
    }

    private void Deadline_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedDeadlineBorder == null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            CancelDeadlineDrag();
            return;
        }

        var currentPos = e.GetPosition(this);
        double dy = Math.Abs(currentPos.Y - _deadlineDragStartPoint.Y);
        if (!_isDraggingDeadline && dy > 4)
        {
            _isDraggingDeadline = true;
            _draggedDeadlineBorder.Opacity = 0.35;
            _draggedDeadlineBorder.CaptureMouse();
            if (_draggedDeadlineItem != null)
            {
                SetGroupedTasksDragOpacity(_draggedDeadlineItem.Id, 0.35);
            }
        }

        if (_isDraggingDeadline)
        {
            int groupedCount = _todoService.GetAll().Count(t => !t.IsCompleted && t.GroupDeadlineId == _draggedDeadlineItem?.Id);
            string ghostText = (groupedCount > 0)
                ? $"{_draggedDeadlineItem?.FullDisplayText} (+{groupedCount})"
                : (_draggedDeadlineItem?.FullDisplayText ?? "title");
            ShowDragGhost(ghostText, currentPos);
            UpdateDeadlineDropSlot(e);
        }
    }

    private class DropBlock
    {
        public double Top { get; set; }
        public double Bottom { get; set; }
        public double Mid => (Top + Bottom) / 2.0;
        public int ActiveTaskCount { get; set; }
        public string? GroupDeadlineId { get; set; }
    }

    private void UpdateDeadlineDropSlot(MouseEventArgs e)
    {
        if (DropIndicator == null || DropIndicatorTransform == null) return;

        string? draggedDlId = _draggedDeadlineItem?.Id;
        var blocks = new List<DropBlock>();
        DropBlock? currentGroupBlock = null;

        for (int i = 0; i < TodoList.Items.Count; i++)
        {
            var item = TodoList.Items[i];
            var container = TodoList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container == null) continue;

            if (item is Models.DeadlineItem dlItem)
            {
                if (dlItem.Id == draggedDlId)
                {
                    currentGroupBlock = null;
                    continue;
                }

                var dlCard = (container is Border b && b.Name == "DeadlineContainer") ? b : FindChildByName<Border>(container, "DeadlineContainer") ?? container;
                var topPt = dlCard.TransformToAncestor(TodoListGrid).Transform(new Point(0, 0));
                double top = topPt.Y;
                double bot = top + dlCard.ActualHeight;

                if (dlItem.HasGroupedTasks)
                {
                    currentGroupBlock = new DropBlock
                    {
                        Top = top,
                        Bottom = bot,
                        ActiveTaskCount = 0,
                        GroupDeadlineId = dlItem.Id
                    };
                    blocks.Add(currentGroupBlock);
                }
                else
                {
                    currentGroupBlock = null;
                    blocks.Add(new DropBlock
                    {
                        Top = top,
                        Bottom = bot,
                        ActiveTaskCount = 0,
                        GroupDeadlineId = null
                    });
                }
            }
            else if (item is Models.TodoItem tItem)
            {
                if (!string.IsNullOrEmpty(draggedDlId) && tItem.GroupDeadlineId == draggedDlId)
                {
                    continue;
                }

                var card = (container is Border b && b.Name == "TaskCard") ? b : FindChildByName<Border>(container, "TaskCard") ?? container;
                var topPt = card.TransformToAncestor(TodoListGrid).Transform(new Point(0, 0));
                double top = topPt.Y;
                double bot = top + card.ActualHeight;

                if (currentGroupBlock != null && tItem.GroupDeadlineId == currentGroupBlock.GroupDeadlineId)
                {
                    currentGroupBlock.Bottom = Math.Max(currentGroupBlock.Bottom, bot);
                    currentGroupBlock.ActiveTaskCount++;
                }
                else
                {
                    currentGroupBlock = null;
                    blocks.Add(new DropBlock
                    {
                        Top = top,
                        Bottom = bot,
                        ActiveTaskCount = 1,
                        GroupDeadlineId = null
                    });
                }
            }
        }

        if (blocks.Count == 0)
        {
            DropIndicator.Visibility = Visibility.Collapsed;
            _deadlineDropTargetSlot = 0;
            return;
        }

        double mouseY = e.GetPosition(TodoListGrid).Y;
        int blockSlot = 0;
        if (mouseY < blocks[0].Mid)
        {
            blockSlot = 0;
        }
        else if (mouseY >= blocks[^1].Mid)
        {
            blockSlot = blocks.Count;
        }
        else
        {
            for (int i = 0; i < blocks.Count - 1; i++)
            {
                if (mouseY >= blocks[i].Mid && mouseY < blocks[i + 1].Mid)
                {
                    blockSlot = i + 1;
                    break;
                }
            }
        }

        int targetActiveSlot = 0;
        for (int i = 0; i < blockSlot; i++)
        {
            targetActiveSlot += blocks[i].ActiveTaskCount;
        }

        _deadlineDropTargetSlot = targetActiveSlot;

        double lineY = (blockSlot == 0)
            ? blocks[0].Top - 3
            : (blockSlot == blocks.Count)
                ? blocks[^1].Bottom + 2
                : (blocks[blockSlot - 1].Bottom + blocks[blockSlot].Top) / 2.0 - 0.5;

        DropIndicatorTransform.Y = Math.Max(0, lineY);
        DropIndicator.Visibility = Visibility.Visible;
    }

    private void Deadline_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingDeadline && _deadlineDropTargetSlot >= 0 && _draggedDeadlineItem != null)
        {
            var dl = _draggedDeadlineItem;
            int lineIdx = dl.LineIndex;
            var slots = _settings.DeadlineSlots;

            if (lineIdx >= 0 && lineIdx < slots.Count)
            {
                var activeTodos = _todoService.GetAll().Where(t => !t.IsCompleted).ToList();
                var groupedTasks = activeTodos.Where(t => t.GroupDeadlineId == dl.Id).ToList();

                if (groupedTasks.Count > 0)
                {
                    var otherActive = activeTodos.Where(t => t.GroupDeadlineId != dl.Id).ToList();
                    int targetOtherSlot = Math.Clamp(_deadlineDropTargetSlot, 0, otherActive.Count);

                    var newActive = new List<Models.TodoItem>();
                    newActive.AddRange(otherActive.Take(targetOtherSlot));
                    newActive.AddRange(groupedTasks);
                    newActive.AddRange(otherActive.Skip(targetOtherSlot));

                    _todoService.ReorderActiveTasks(newActive.Select(t => t.Id).ToList());

                    var lines = new List<(int slot, double sortKey, string label, string? timerEnd, string id)>();
                    for (int i = 0; i < slots.Count; i++)
                    {
                        string id = _settings.GetDeadlineId(i);
                        string label = _settings.GetDeadlineLabel(i);
                        string? timerEnd = (i < _settings.DeadlineTimerEnds.Count) ? _settings.DeadlineTimerEnds[i] : null;

                        if (i == lineIdx)
                        {
                            lines.Add((targetOtherSlot, targetOtherSlot, label, timerEnd, id));
                        }
                        else
                        {
                            int oldSlot = slots[i];
                            if (oldSlot >= activeTodos.Count)
                            {
                                lines.Add((newActive.Count, newActive.Count + 0.5, label, timerEnd, id));
                            }
                            else
                            {
                                var taskAfter = activeTodos[oldSlot];
                                int newSlot = newActive.IndexOf(taskAfter);
                                if (newSlot < 0) newSlot = oldSlot;
                                double sortKey = newSlot + (i < lineIdx ? -0.1 : 0.1);
                                lines.Add((newSlot, sortKey, label, timerEnd, id));
                            }
                        }
                    }
                    lines.Sort((a, b) => a.sortKey.CompareTo(b.sortKey));

                    _settings.DeadlineSlots = lines.Select(x => x.slot).ToList();
                    _settings.DeadlineLabels = lines.Select(x => x.label).ToList();
                    _settings.DeadlineTimerEnds = lines.Select(x => x.timerEnd).ToList();
                    _settings.DeadlineIds = lines.Select(x => x.id).ToList();
                }
                else
                {
                    int targetSlot = Math.Clamp(_deadlineDropTargetSlot, 0, activeTodos.Count);
                    var lines = new List<(int slot, double sortKey, string label, string? timerEnd, string id)>();
                    for (int i = 0; i < slots.Count; i++)
                    {
                        int s = (i == lineIdx) ? targetSlot : slots[i];
                        string l = _settings.GetDeadlineLabel(i);
                        string? t = (i < _settings.DeadlineTimerEnds.Count) ? _settings.DeadlineTimerEnds[i] : null;
                        string id = _settings.GetDeadlineId(i);
                        double sortKey = s + (i == lineIdx ? 0.0 : (i < lineIdx ? -0.1 : 0.1));
                        lines.Add((s, sortKey, l, t, id));
                    }
                    lines.Sort((a, b) => a.sortKey.CompareTo(b.sortKey));

                    _settings.DeadlineSlots = lines.Select(x => x.slot).ToList();
                    _settings.DeadlineLabels = lines.Select(x => x.label).ToList();
                    _settings.DeadlineTimerEnds = lines.Select(x => x.timerEnd).ToList();
                    _settings.DeadlineIds = lines.Select(x => x.id).ToList();
                }
            }

            CancelDeadlineDrag();
            RefreshList();
        }
        else
        {
            CancelDeadlineDrag();
        }
    }

    private void CancelDeadlineDrag()
    {
        _isDraggingDeadline = false;
        if (_draggedDeadlineItem != null)
        {
            SetGroupedTasksDragOpacity(_draggedDeadlineItem.Id, 1.0);
        }
        if (_draggedDeadlineBorder != null)
        {
            _draggedDeadlineBorder.Opacity = 1.0;
            _draggedDeadlineBorder.ReleaseMouseCapture();
            _draggedDeadlineBorder = null;
        }
        _draggedDeadlineItem = null;
        HideDragGhost();
        if (DropIndicator != null) DropIndicator.Visibility = Visibility.Collapsed;
        _deadlineDropTargetSlot = -1;
    }

    // === Group Task Thread Dragging ===
    private bool _isDraggingLink;
    private Models.DeadlineItem? _linkOriginDeadline;
    private Point _linkStartPoint;
    private FrameworkElement? _highlightedCard;
    private Brush? _savedBorderBrush;
    private Thickness _savedBorderThickness;

    private System.Windows.Threading.DispatcherTimer? _snapBackTimer;

    private void DeadlineTarget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _snapBackTimer?.Stop();

        if (sender is FrameworkElement fe && fe.DataContext is Models.DeadlineItem deadline)
        {
            _isDraggingLink = true;
            _linkOriginDeadline = deadline;

            _linkStartPoint = fe.TranslatePoint(new Point(fe.ActualWidth / 2, fe.ActualHeight / 2), LinkDragCanvas);

            fe.CaptureMouse();

            LinkDragCanvas.Visibility = Visibility.Visible;

            Canvas.SetLeft(LinkOriginDot, _linkStartPoint.X - 3);
            Canvas.SetTop(LinkOriginDot, _linkStartPoint.Y - 3);

            Canvas.SetLeft(LinkCursorDot, _linkStartPoint.X - 4);
            Canvas.SetTop(LinkCursorDot, _linkStartPoint.Y - 4);

            LinkThreadPath.Data = null;
        }
    }

    private void DeadlineTarget_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingLink || sender is not FrameworkElement) return;
        e.Handled = true;

        var currentPos = e.GetPosition(LinkDragCanvas);

        Canvas.SetLeft(LinkCursorDot, currentPos.X - 4);
        Canvas.SetTop(LinkCursorDot, currentPos.Y - 4);

        double dist = Math.Sqrt(Math.Pow(currentPos.X - _linkStartPoint.X, 2) + Math.Pow(currentPos.Y - _linkStartPoint.Y, 2));
        double sag = Math.Min(30, dist * 0.12);
        Point mid = new Point((_linkStartPoint.X + currentPos.X) / 2, (_linkStartPoint.Y + currentPos.Y) / 2 + sag);

        var figure = new PathFigure { StartPoint = _linkStartPoint, IsClosed = false };
        figure.Segments.Add(new QuadraticBezierSegment(mid, currentPos, true));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        LinkThreadPath.Data = geometry;

        var windowPos = e.GetPosition(this);
        var hoveredCard = FindTaskCardUnderPoint(windowPos);
        HighlightTaskCard(hoveredCard);
    }

    private void DeadlineTarget_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var fe = sender as FrameworkElement;
        fe?.ReleaseMouseCapture();

        if (_isDraggingLink && _linkOriginDeadline != null)
        {
            e.Handled = true;
            var windowPos = e.GetPosition(this);
            var targetCard = FindTaskCardUnderPoint(windowPos);
            var targetItem = targetCard?.DataContext as Models.TodoItem;
            var dl = _linkOriginDeadline;

            if (targetItem != null && dl != null)
            {
                CancelLinkDrag();
                GroupTaskWithDeadline(targetItem, dl);
            }
            else
            {
                var releasePos = e.GetPosition(LinkDragCanvas);
                PlaySparksBurstAnimation(releasePos, fe);
            }
        }
        else
        {
            CancelLinkDrag();
        }
    }

    private readonly List<UIElement> _threadParticles = new();

    private void PlaySparksBurstAnimation(Point releasePos, FrameworkElement? originElement)
    {
        _isDraggingLink = false;
        _linkOriginDeadline = null;
        HighlightTaskCard(null);

        _snapBackTimer?.Stop();
        ClearThreadParticles();

        double dist = Math.Sqrt(Math.Pow(releasePos.X - _linkStartPoint.X, 2) + Math.Pow(releasePos.Y - _linkStartPoint.Y, 2));
        if (dist < 8)
        {
            CancelLinkDrag();
            return;
        }

        var rand = new Random();
        var brush = LinkThreadPath.Stroke ?? (FindResource("fg/accent") as Brush) ?? Brushes.CornflowerBlue;

        // Normal and tangent vector to thread
        double dx = releasePos.X - _linkStartPoint.X;
        double dy = releasePos.Y - _linkStartPoint.Y;
        double len = Math.Max(1.0, dist);
        double nx = -dy / len;
        double ny = dx / len;

        var particleData = new List<(UIElement Element, double X, double Y, double Vx, double Vy, double Size)>();

        // 1. Tip burst particles (around release point)
        int tipCount = 16;
        for (int i = 0; i < tipCount; i++)
        {
            double angle = rand.NextDouble() * Math.PI * 2;
            double speed = 50 + rand.NextDouble() * 95;
            double size = 2.0 + rand.NextDouble() * 2.8;
            var el = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = brush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(el, releasePos.X - size / 2);
            Canvas.SetTop(el, releasePos.Y - size / 2);
            LinkDragCanvas.Children.Add(el);
            _threadParticles.Add(el);
            particleData.Add((el, releasePos.X, releasePos.Y, Math.Cos(angle) * speed, Math.Sin(angle) * speed, size));
        }

        // 2. Origin burst particles (around target anchor point)
        int originCount = 6;
        for (int i = 0; i < originCount; i++)
        {
            double angle = rand.NextDouble() * Math.PI * 2;
            double speed = 30 + rand.NextDouble() * 50;
            double size = 1.8 + rand.NextDouble() * 2.0;
            var el = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = brush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(el, _linkStartPoint.X - size / 2);
            Canvas.SetTop(el, _linkStartPoint.Y - size / 2);
            LinkDragCanvas.Children.Add(el);
            _threadParticles.Add(el);
            particleData.Add((el, _linkStartPoint.X, _linkStartPoint.Y, Math.Cos(angle) * speed, Math.Sin(angle) * speed, size));
        }

        // 3. Scatter particles along the thread length
        int lineCount = Math.Clamp((int)(dist / 14), 8, 24);
        for (int i = 1; i <= lineCount; i++)
        {
            double t = (double)i / (lineCount + 1);
            double px = _linkStartPoint.X + dx * t;
            double py = _linkStartPoint.Y + dy * t;
            double perpSpeed = (rand.NextDouble() * 2 - 1) * 75;
            double parallelSpeed = (rand.NextDouble() * 2 - 1) * 35;
            double vx = nx * perpSpeed + (dx / len) * parallelSpeed;
            double vy = ny * perpSpeed + (dy / len) * parallelSpeed;
            double size = 1.8 + rand.NextDouble() * 2.4;

            var el = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = brush,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(el, px - size / 2);
            Canvas.SetTop(el, py - size / 2);
            LinkDragCanvas.Children.Add(el);
            _threadParticles.Add(el);
            particleData.Add((el, px, py, vx, vy, size));
        }

        LinkCursorDot.Opacity = 0;
        LinkOriginDot.Opacity = 0;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        double durationMs = 360.0;
        double lastElapsed = 0;

        _snapBackTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(10)
        };

        _snapBackTimer.Tick += (s, ev) =>
        {
            double elapsed = sw.Elapsed.TotalMilliseconds;
            double dt = (elapsed - lastElapsed) / 1000.0;
            lastElapsed = elapsed;
            double progress = Math.Clamp(elapsed / durationMs, 0.0, 1.0);

            // Thread line quickly dissolves in first ~70ms
            double lineFade = Math.Clamp(1.0 - (elapsed / 70.0), 0.0, 1.0);
            LinkThreadPath.Opacity = lineFade;

            // Particle fade and movement
            double particleOpacity = Math.Pow(1.0 - progress, 1.2);
            double drag = Math.Exp(-progress * 2.2);

            for (int i = 0; i < particleData.Count; i++)
            {
                var p = particleData[i];
                double newX = p.X + p.Vx * dt * drag;
                double newY = p.Y + p.Vy * dt * drag;
                particleData[i] = (p.Element, newX, newY, p.Vx, p.Vy, p.Size);

                Canvas.SetLeft(p.Element, newX - p.Size / 2);
                Canvas.SetTop(p.Element, newY - p.Size / 2);
                p.Element.Opacity = particleOpacity;
            }

            if (progress >= 1.0)
            {
                _snapBackTimer.Stop();
                ClearThreadParticles();
                CancelLinkDrag();
                LinkThreadPath.Opacity = 1.0;
                LinkCursorDot.Opacity = 1.0;
                LinkOriginDot.Opacity = 1.0;
                LinkDragCanvas.Opacity = 1.0;
            }
        };

        _snapBackTimer.Start();
    }

    private void ClearThreadParticles()
    {
        if (LinkDragCanvas != null && _threadParticles.Count > 0)
        {
            foreach (var p in _threadParticles)
            {
                LinkDragCanvas.Children.Remove(p);
            }
        }
        _threadParticles.Clear();
    }

    private void PulseTargetIcon(FrameworkElement? originElement)
    {
        if (originElement == null) return;

        var transform = originElement.RenderTransform as ScaleTransform;
        if (transform == null)
        {
            transform = new ScaleTransform(1, 1);
            originElement.RenderTransformOrigin = new Point(0.5, 0.5);
            originElement.RenderTransform = transform;
        }

        var animX = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
        animX.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.4, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        animX.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });

        var animY = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
        animY.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.4, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        animY.KeyFrames.Add(new System.Windows.Media.Animation.EasingDoubleKeyFrame(1.0, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });

        transform.BeginAnimation(ScaleTransform.ScaleXProperty, animX);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, animY);
    }

    private void CancelLinkDrag()
    {
        _snapBackTimer?.Stop();
        ClearThreadParticles();
        _isDraggingLink = false;
        _linkOriginDeadline = null;
        HighlightTaskCard(null);
        if (LinkDragCanvas != null)
        {
            LinkDragCanvas.Visibility = Visibility.Collapsed;
            LinkThreadPath.Data = null;
            LinkThreadPath.Opacity = 1.0;
            LinkCursorDot.Opacity = 1.0;
            LinkOriginDot.Opacity = 1.0;
            LinkDragCanvas.Opacity = 1.0;
        }
    }

    private FrameworkElement? FindTaskCardUnderPoint(Point windowPoint)
    {
        if (TodoList == null || TodoList.Items.Count == 0) return null;

        for (int i = 0; i < TodoList.Items.Count; i++)
        {
            if (TodoList.Items[i] is not Models.TodoItem todo || todo.IsCompleted) continue;

            var container = TodoList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container == null) continue;

            var card = (container is Border b && b.Name == "TaskCard") ? b : FindChildByName<Border>(container, "TaskCard") ?? container;

            var topPt = card.TransformToAncestor(this).Transform(new Point(0, 0));
            var rect = new Rect(topPt.X, topPt.Y, card.ActualWidth, card.ActualHeight);

            if (rect.Contains(windowPoint))
            {
                return card;
            }
        }

        return null;
    }

    private void HighlightTaskCard(FrameworkElement? card)
    {
        if (_highlightedCard == card) return;

        if (_highlightedCard is Border prevBorder)
        {
            prevBorder.BorderBrush = _savedBorderBrush;
            prevBorder.BorderThickness = _savedBorderThickness;
        }

        _highlightedCard = card;

        if (_highlightedCard is Border newBorder)
        {
            _savedBorderBrush = newBorder.BorderBrush;
            _savedBorderThickness = newBorder.BorderThickness;

            var accent = Application.Current.TryFindResource("fg/accent") as Brush ?? Brushes.DodgerBlue;
            newBorder.BorderBrush = accent;
            newBorder.BorderThickness = new Thickness(1.5);
        }
    }

    private void GroupTaskWithDeadline(Models.TodoItem targetTodo, Models.DeadlineItem deadline)
    {
        var active = _todoService.GetAll().Where(t => !t.IsCompleted).ToList();
        int oldIdx = active.FindIndex(t => t.Id == targetTodo.Id);
        if (oldIdx < 0) return;

        targetTodo.GroupDeadlineId = deadline.Id;
        _todoService.UpdateGroupDeadlineId(targetTodo.Id, deadline.Id);

        active.RemoveAt(oldIdx);

        var slots = _settings.DeadlineSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            if (oldIdx < slots[i])
            {
                slots[i] = Math.Max(0, slots[i] - 1);
            }
        }

        int updatedDlSlot = deadline.LineIndex < slots.Count ? slots[deadline.LineIndex] : Math.Min(deadline.Slot, active.Count);
        int groupedCount = active.Count(t => t.GroupDeadlineId == deadline.Id);
        int targetIdx = Math.Min(active.Count, updatedDlSlot + groupedCount);

        for (int i = 0; i < slots.Count; i++)
        {
            if (targetIdx <= slots[i] && i != deadline.LineIndex)
            {
                slots[i]++;
            }
        }
        _settings.DeadlineSlots = slots;

        active.Insert(targetIdx, targetTodo);
        _todoService.ReorderActiveTasks(active.Select(t => t.Id).ToList());

        RefreshList();
        AnimateMagneticSnap(targetTodo.Id);
    }

    private void AnimateMagneticSnap(string todoId)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                TodoList.UpdateLayout();
                Border? targetCard = null;
                for (int i = 0; i < TodoList.Items.Count; i++)
                {
                    if (TodoList.Items[i] is Models.TodoItem item && item.Id == todoId)
                    {
                        var container = TodoList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                        if (container != null)
                        {
                            targetCard = (container is Border b && b.Name == "TaskCard") ? b : FindChildByName<Border>(container, "TaskCard");
                        }
                        break;
                    }
                }

                if (targetCard == null) return;

                var scale = new ScaleTransform(0.985, 0.985);
                var translate = new TranslateTransform(0, -12);
                var group = new TransformGroup();
                group.Children.Add(scale);
                group.Children.Add(translate);

                targetCard.RenderTransformOrigin = new Point(0.5, 0.5);
                targetCard.RenderTransform = group;

                // Accent glow halo
                var accentBrush = TryFindResource("fg/accent") as SolidColorBrush ?? Brushes.DodgerBlue;
                var glow = new DropShadowEffect
                {
                    Color = accentBrush.Color,
                    BlurRadius = 14,
                    ShadowDepth = 0,
                    Opacity = 0.0
                };
                targetCard.Effect = glow;

                // 1. Snappy magnetic pull with slight overshoot (+2.5px), rebound (-0.8px), and lock (0px)
                var transAnim = new DoubleAnimationUsingKeyFrames();
                transAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(-12, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                transAnim.KeyFrames.Add(new SplineDoubleKeyFrame(2.5, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), new KeySpline(0.1, 0.9, 0.2, 1.0)));
                transAnim.KeyFrames.Add(new SplineDoubleKeyFrame(-0.8, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170)), new KeySpline(0.3, 0.0, 0.7, 1.0)));
                transAnim.KeyFrames.Add(new SplineDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), new KeySpline(0.2, 0.0, 0.5, 1.0)));

                // 2. Micro tactile spring scale
                var scaleAnim = new DoubleAnimationUsingKeyFrames();
                scaleAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.985, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                scaleAnim.KeyFrames.Add(new SplineDoubleKeyFrame(1.018, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)), new KeySpline(0.1, 0.9, 0.2, 1.0)));
                scaleAnim.KeyFrames.Add(new SplineDoubleKeyFrame(0.995, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(170))));
                scaleAnim.KeyFrames.Add(new SplineDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220))));

                // 3. Neon glow pulse at impact moment
                var glowAnim = new DoubleAnimationUsingKeyFrames();
                glowAnim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                glowAnim.KeyFrames.Add(new SplineDoubleKeyFrame(0.85, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110)), new KeySpline(0.1, 0.9, 0.2, 1.0)));
                glowAnim.KeyFrames.Add(new SplineDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new KeySpline(0.4, 0.0, 1.0, 1.0)));

                // 4. Smooth opacity
                var opacityAnim = new DoubleAnimation(0.75, 1.0, TimeSpan.FromMilliseconds(100));

                glowAnim.Completed += (s, e) =>
                {
                    targetCard.RenderTransform = Transform.Identity;
                    targetCard.Effect = null;
                    targetCard.Opacity = 1.0;
                };

                translate.BeginAnimation(TranslateTransform.YProperty, transAnim);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
                glow.BeginAnimation(DropShadowEffect.OpacityProperty, glowAnim);
                targetCard.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
            }
            catch { }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private string? _editingDeadlineId;

    private void BeginEditDeadline(Border container, Models.DeadlineItem? item)
    {
        if (item == null) return;
        var displayPanel = FindChildByName<DockPanel>(container, "DeadlineDisplayPanel");
        var editBox = FindChildByName<TextBox>(container, "DeadlineEditBox");
        if (displayPanel == null || editBox == null) return;

        _editingDeadlineId = item.Id;
        displayPanel.Visibility = Visibility.Collapsed;
        editBox.Text = item.FullDisplayText;
        editBox.Visibility = Visibility.Visible;
        editBox.Focus();
        editBox.SelectAll();
    }

    private void DeadlineEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is Models.DeadlineItem item)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                CommitEditDeadline(textBox, item);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CancelEditDeadline();
            }
        }
    }

    private void DeadlineEditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.DataContext is Models.DeadlineItem item)
        {
            CommitEditDeadline(textBox, item);
        }
    }

    private void CommitEditDeadline(TextBox textBox, Models.DeadlineItem item)
    {
        if (_editingDeadlineId == null || _editingDeadlineId != item.Id) return;
        _editingDeadlineId = null;

        var text = textBox.Text?.Trim() ?? "";

        bool isTimer = false;
        TimeSpan duration = TimeSpan.Zero;
        string labelText = text;

        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            @"^(?:(.*?)\s*(?:[·•\-])?\s*)?(\d{1,2}:\d{2}:\d{2}|\d{1,2}:\d{2})$"
        );

        if (match.Success)
        {
            var timePart = match.Groups[2].Value;
            var rawLabel = match.Groups[1].Value.Trim();

            var timeParts = timePart.Split(':');
            if (timeParts.Length == 3)
            {
                int h = int.Parse(timeParts[0]);
                int m = int.Parse(timeParts[1]);
                int s = int.Parse(timeParts[2]);
                if (m < 60 && s < 60)
                {
                    duration = new TimeSpan(h, m, s);
                    isTimer = true;
                    labelText = rawLabel;
                }
            }
            else if (timeParts.Length == 2)
            {
                int m = int.Parse(timeParts[0]);
                int s = int.Parse(timeParts[1]);
                if (s < 60)
                {
                    duration = new TimeSpan(0, m, s);
                    isTimer = true;
                    labelText = rawLabel;
                }
            }
        }

        if (isTimer)
        {
            var endUtc = DateTime.UtcNow.Add(duration);
            _settings.SetDeadlineTimerEnd(item.LineIndex, endUtc);
            _settings.SetDeadlineLabel(item.LineIndex, labelText);
        }
        else
        {
            _settings.SetDeadlineTimerEnd(item.LineIndex, null);
            _settings.SetDeadlineLabel(item.LineIndex, string.IsNullOrWhiteSpace(labelText) ? "title" : labelText);
        }

        RefreshList();
    }

    private void CancelEditDeadline()
    {
        _editingDeadlineId = null;
        RefreshList();
    }

    private void StartDeadlineTimerIfNeeded()
    {
        bool hasAnyActiveTimer = false;
        var now = DateTime.UtcNow;
        for (int i = 0; i < _settings.DeadlineSlots.Count; i++)
        {
            var endUtc = _settings.GetDeadlineTimerEnd(i);
            if (endUtc != null && endUtc.Value > now)
            {
                hasAnyActiveTimer = true;
                break;
            }
        }

        if (hasAnyActiveTimer)
        {
            if (_deadlineCountdownTimer == null)
            {
                _deadlineCountdownTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _deadlineCountdownTimer.Tick += (s, e) => UpdateDeadlineTimers();
            }
            if (!_deadlineCountdownTimer.IsEnabled)
            {
                _deadlineCountdownTimer.Start();
            }
        }
        else
        {
            _deadlineCountdownTimer?.Stop();
        }
    }

    private void UpdateDeadlineTimers()
    {
        if (TodoList == null || TodoList.Items.Count == 0) return;
        bool anyRunning = false;
        var now = DateTime.UtcNow;

        foreach (var obj in TodoList.Items)
        {
            if (obj is Models.DeadlineItem dl)
            {
                var endUtc = _settings.GetDeadlineTimerEnd(dl.LineIndex);
                if (endUtc != null)
                {
                    if (_editingDeadlineId == dl.Id)
                    {
                        if (endUtc.Value > now) anyRunning = true;
                        continue;
                    }

                    var rem = endUtc.Value - now;
                    if (rem <= TimeSpan.Zero)
                    {
                        dl.TimerText = "00:00:00";
                        dl.IsExpired = true;
                    }
                    else
                    {
                        anyRunning = true;
                        int th = (int)rem.TotalHours;
                        dl.TimerText = $"{th:D2}:{rem.Minutes:D2}:{rem.Seconds:D2}";
                        dl.IsExpired = false;
                    }
                }
            }
        }

        if (!anyRunning && _deadlineCountdownTimer?.IsEnabled == true)
        {
            _deadlineCountdownTimer.Stop();
        }
    }


    private void AddDeadlineLine_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.DataContext is Models.DeadlineItem item)
        {
            var activeCount = _todoService.GetAll().Count(t => !t.IsCompleted);
            var slots = _settings.DeadlineSlots;

            int newSlot = activeCount;

            var lines = new List<(int slot, string label, string? timerEnd, string id)>();
            for (int i = 0; i < slots.Count; i++)
            {
                string l = _settings.GetDeadlineLabel(i);
                string? t = (i < _settings.DeadlineTimerEnds.Count) ? _settings.DeadlineTimerEnds[i] : null;
                string id = _settings.GetDeadlineId(i);
                lines.Add((slots[i], l, t, id));
            }
            lines.Add((newSlot, "title", null, Guid.NewGuid().ToString()));
            lines.Sort((a, b) => a.slot.CompareTo(b.slot));

            _settings.DeadlineSlots = lines.Select(x => x.slot).ToList();
            _settings.DeadlineLabels = lines.Select(x => x.label).ToList();
            _settings.DeadlineTimerEnds = lines.Select(x => x.timerEnd).ToList();
            _settings.DeadlineIds = lines.Select(x => x.id).ToList();

            RefreshList();
        }
    }

    private void DeleteDeadlineLine_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.DataContext is Models.DeadlineItem item)
        {
            if (item.HasGroupedTasks)
            {
                var allTodos = _todoService.GetAll();
                foreach (var t in allTodos.Where(t => t.GroupDeadlineId == item.Id))
                {
                    t.GroupDeadlineId = null;
                    _todoService.UpdateGroupDeadlineId(t.Id, null);
                }
                RefreshList();
                return;
            }

            var slots = _settings.DeadlineSlots;
            if (item.LineIndex >= 0 && item.LineIndex < slots.Count)
            {
                string deletedId = item.Id;
                var allTodos = _todoService.GetAll();
                foreach (var t in allTodos.Where(t => t.GroupDeadlineId == deletedId))
                {
                    t.GroupDeadlineId = null;
                    _todoService.UpdateGroupDeadlineId(t.Id, null);
                }

                slots.RemoveAt(item.LineIndex);
                _settings.DeadlineSlots = slots;
                if (_settings.DeadlineLabels.Count > item.LineIndex)
                {
                    var labels = _settings.DeadlineLabels;
                    labels.RemoveAt(item.LineIndex);
                    _settings.DeadlineLabels = labels;
                }
                if (_settings.DeadlineTimerEnds.Count > item.LineIndex)
                {
                    var timerEnds = _settings.DeadlineTimerEnds;
                    timerEnds.RemoveAt(item.LineIndex);
                    _settings.DeadlineTimerEnds = timerEnds;
                }
                if (_settings.DeadlineIds.Count > item.LineIndex)
                {
                    var ids = _settings.DeadlineIds;
                    ids.RemoveAt(item.LineIndex);
                    _settings.DeadlineIds = ids;
                }
                RefreshList();
            }
        }
    }

    private void AddFallbackDeadline_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var activeCount = _todoService.GetAll().Count(t => !t.IsCompleted);
        int defaultSlot = activeCount;
        _settings.DeadlineSlots = new List<int> { defaultSlot };
        _settings.DeadlineLabels = new List<string> { "title" };
        _settings.DeadlineTimerEnds = new List<string?> { null };
        _settings.DeadlineIds = new List<string> { Guid.NewGuid().ToString() };
        RefreshList();
    }


    private void OnActiveTaskRemoved(int taskIndex)
    {
        var slots = _settings.DeadlineSlots;
        bool changed = false;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] > taskIndex)
            {
                slots[i]--;
                changed = true;
            }
        }
        if (changed)
        {
            _settings.DeadlineSlots = slots;
        }
    }

    private void OnActiveTaskAddedToTop()
    {
        var slots = _settings.DeadlineSlots;
        for (int i = 0; i < slots.Count; i++)
        {
            slots[i]++;
        }
        _settings.DeadlineSlots = slots;
    }

    private static T? FindChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T fe && fe.Name == name) return fe;
            var result = FindChildByName<T>(child, name);
            if (result != null) return result;
        }
        return null;
    }

    private void UrgentText_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is Border border && border.Tag is string id)
        {
            var item = _todoService.GetAll().FirstOrDefault(t => t.Id == id);

            // If clicking on already-urgent task → unmark it
            if (item != null && item.IsUrgent)
            {
                _todoService.ClearUrgent();
                ExitUrgentMode();
                RefreshList();
            }
            else if (item != null && !item.IsCompleted)
            {
                // Mark as urgent (only one at a time)
                _todoService.SetUrgent(id);
                var updatedItem = _todoService.GetAll().FirstOrDefault(t => t.Id == id);
                EnterUrgentMode(updatedItem);
                RefreshList();
                ShowUrgentFireIcons();
            }
        }
    }

    private void EnterUrgentMode(Models.TodoItem? item = null)
    {
        item ??= _todoService.GetAll().FirstOrDefault(t => t.IsUrgent && !t.IsCompleted);
        if (item == null)
        {
            ExitUrgentMode();
            return;
        }

        _isUrgentMode = true;
        UpdateUrgentIcons();
        HeaderTitle.Visibility = Visibility.Collapsed;
        TaskCounter.Visibility = Visibility.Collapsed;
        TimerText.Visibility = Visibility.Visible;

        if (item.UrgentStartedAt == null)
        {
            _todoService.SetUrgent(item.Id);
            item = _todoService.GetAll().FirstOrDefault(t => t.Id == item.Id);
        }
        StartUrgentTimer(item?.UrgentStartedAt ?? DateTime.UtcNow);
    }

    private void ExitUrgentMode()
    {
        _isUrgentMode = false;
        UpdateUrgentIcons();
        HeaderTitle.Visibility = Visibility.Visible;
        HeaderTitle.Text = "ADHD to-do";
        TaskCounter.Visibility = Visibility.Visible;
        StopUrgentTimer();
        UpdateTaskCounter();
    }

    private void StartUrgentTimer(DateTime startTime)
    {
        _urgentStartTime = startTime.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(startTime, DateTimeKind.Utc)
            : startTime.ToUniversalTime();

        UpdateTimerDisplay();

        _urgentTimer?.Stop();
        _urgentTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _urgentTimer.Tick += (s, e) => UpdateTimerDisplay();
        _urgentTimer.Start();
    }

    private void UpdateTimerDisplay()
    {
        if (_urgentStartTime == null) return;
        var startUtc = _urgentStartTime.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(_urgentStartTime.Value, DateTimeKind.Utc)
            : _urgentStartTime.Value.ToUniversalTime();

        var elapsed = DateTime.UtcNow - startUtc;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;

        int totalHours = (int)elapsed.TotalHours;
        TimerText.Text = $"{totalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }

    private void StopUrgentTimer()
    {
        _urgentTimer?.Stop();
        _urgentTimer = null;
        _urgentStartTime = null;
        TimerText.Visibility = Visibility.Collapsed;
        TimerText.Text = "00:00:00";
    }

    private void CompletedSection_Click(object sender, MouseButtonEventArgs e)
    {
        _showCompleted = !_showCompleted;
        AnimateCompletedArrow(_showCompleted);
        RefreshList();
    }

    private void ClearCompletedButton_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;

        var cards = new List<Border>();
        for (int i = 0; i < CompletedList.Items.Count; i++)
        {
            var container = CompletedList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container == null) continue;
            var card = FindChildByName<Border>(container, "CompletedCard");
            if (card != null) cards.Add(card);
        }

        if (cards.Count == 0)
        {
            _todoService.ClearCompleted();
            _showCompleted = false;
            AnimateCompletedArrow(false);
            RefreshList();
            return;
        }

        if (ClearCompletedButton != null)
        {
            ClearCompletedButton.IsHitTestVisible = false;
            var buttonFade = new System.Windows.Media.Animation.DoubleAnimation(ClearCompletedButton.Opacity, 0, TimeSpan.FromMilliseconds(200));
            ClearCompletedButton.BeginAnimation(UIElement.OpacityProperty, buttonFade);
        }

        AnimateCompletedArrow(false);

        bool finished = false;
        Action doClear = () =>
        {
            if (finished) return;
            finished = true;
            _todoService.ClearCompleted();
            _showCompleted = false;
            RefreshList();
        };

        var fallbackTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        fallbackTimer.Tick += (s, ev) =>
        {
            fallbackTimer.Stop();
            doClear();
        };
        fallbackTimer.Start();

        int remaining = cards.Count;
        var dissolveDuration = TimeSpan.FromMilliseconds(200);
        var collapseDuration = TimeSpan.FromMilliseconds(150);

        var blurEase = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var fadeEase = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn };
        var collapseEase = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };

        foreach (var card in cards)
        {
            card.IsHitTestVisible = false;
            card.ClipToBounds = false;

            var blur = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = 0,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
            };
            card.Effect = blur;

            var blurAnim = new System.Windows.Media.Animation.DoubleAnimation(0, 14, dissolveDuration) { EasingFunction = blurEase };
            var opacityAnim = new System.Windows.Media.Animation.DoubleAnimation(card.Opacity, 0, dissolveDuration) { EasingFunction = fadeEase };

            opacityAnim.Completed += (s, ev) =>
            {
                card.ClipToBounds = true;
                double initialHeight = card.ActualHeight;
                card.Height = initialHeight;

                var heightAnim = new System.Windows.Media.Animation.DoubleAnimation(initialHeight, 0, collapseDuration) { EasingFunction = collapseEase };
                var marginAnim = new System.Windows.Media.Animation.ThicknessAnimation(card.Margin, new Thickness(0), collapseDuration) { EasingFunction = collapseEase };

                heightAnim.Completed += (s2, ev2) =>
                {
                    remaining--;
                    if (remaining <= 0)
                    {
                        fallbackTimer.Stop();
                        doClear();
                    }
                };

                card.BeginAnimation(FrameworkElement.HeightProperty, heightAnim);
                card.BeginAnimation(FrameworkElement.MarginProperty, marginAnim);
            };

            blur.BeginAnimation(System.Windows.Media.Effects.BlurEffect.RadiusProperty, blurAnim);
            card.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        }
    }

    private void AnimateCompletedArrow(bool expanded)
    {
        if (CompletedArrow == null) return;

        var transform = CompletedArrow.RenderTransform as RotateTransform;
        if (transform == null || transform.IsFrozen)
        {
            transform = new RotateTransform(expanded ? 90 : 0);
            CompletedArrow.RenderTransformOrigin = new Point(0.5, 0.5);
            CompletedArrow.RenderTransform = transform;
            return;
        }

        double targetAngle = expanded ? 90 : 0;
        var anim = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = targetAngle,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new System.Windows.Media.Animation.CubicEase
            {
                EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut
            }
        };
        transform.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void RemoveDeadlineById(string deadlineId)
    {
        var slots = _settings.DeadlineSlots;
        var labels = _settings.DeadlineLabels;
        var timerEnds = _settings.DeadlineTimerEnds;
        var ids = _settings.DeadlineIds;

        int idx = -1;
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == deadlineId)
            {
                idx = i;
                break;
            }
        }

        if (idx >= 0 && idx < slots.Count)
        {
            var allTodos = _todoService.GetAll();
            foreach (var t in allTodos.Where(t => t.GroupDeadlineId == deadlineId))
            {
                t.GroupDeadlineId = null;
                _todoService.UpdateGroupDeadlineId(t.Id, null);
            }

            slots.RemoveAt(idx);
            _settings.DeadlineSlots = slots;
            if (labels.Count > idx)
            {
                labels.RemoveAt(idx);
                _settings.DeadlineLabels = labels;
            }
            if (timerEnds.Count > idx)
            {
                timerEnds.RemoveAt(idx);
                _settings.DeadlineTimerEnds = timerEnds;
            }
            if (ids.Count > idx)
            {
                ids.RemoveAt(idx);
                _settings.DeadlineIds = ids;
            }
        }
    }

    private void CleanupFinishedGroups()
    {
        var allTodos = _todoService.GetAll();
        var activeTodos = allTodos.Where(t => !t.IsCompleted).ToList();
        var slots = _settings.DeadlineSlots;

        if (activeTodos.Count == 0)
        {
            if (slots.Count > 0)
            {
                var allGrouped = allTodos.Where(t => !string.IsNullOrEmpty(t.GroupDeadlineId)).ToList();
                foreach (var t in allGrouped)
                {
                    t.GroupDeadlineId = null;
                    _todoService.UpdateGroupDeadlineId(t.Id, null);
                }

                _settings.DeadlineSlots = new List<int>();
                _settings.DeadlineLabels = new List<string>();
                _settings.DeadlineTimerEnds = new List<string?>();
                _settings.DeadlineIds = new List<string>();
            }
            return;
        }

        var lines = new List<(int slot, string label, string? timerEnd, string id)>();
        bool changed = false;

        for (int i = 0; i < slots.Count; i++)
        {
            string id = _settings.GetDeadlineId(i);
            string label = _settings.GetDeadlineLabel(i);
            string? timerEnd = (i < _settings.DeadlineTimerEnds.Count) ? _settings.DeadlineTimerEnds[i] : null;

            int activeGrouped = activeTodos.Count(t => t.GroupDeadlineId == id);
            int totalGrouped = allTodos.Count(t => t.GroupDeadlineId == id);

            // If a group had tasks and all of them are now closed (activeGrouped == 0): delete this title completely!
            if (totalGrouped > 0 && activeGrouped == 0)
            {
                changed = true;
                foreach (var t in allTodos.Where(t => t.GroupDeadlineId == id))
                {
                    t.GroupDeadlineId = null;
                    _todoService.UpdateGroupDeadlineId(t.Id, null);
                }
                continue;
            }

            lines.Add((slots[i], label, timerEnd, id));
        }

        if (changed)
        {
            _settings.DeadlineSlots = lines.Select(x => x.slot).ToList();
            _settings.DeadlineLabels = lines.Select(x => x.label).ToList();
            _settings.DeadlineTimerEnds = lines.Select(x => x.timerEnd).ToList();
            _settings.DeadlineIds = lines.Select(x => x.id).ToList();
        }
    }

    private void RefreshList()
    {
        CleanupFinishedGroups();
        var todos = _todoService.GetAll();

        if (_isUrgentMode)
        {
            var urgent = todos.Where(t => t.IsUrgent && !t.IsCompleted).ToList();
            TodoList.ItemsSource = urgent;
            if (AddDeadlineFallback != null) AddDeadlineFallback.Visibility = Visibility.Collapsed;
            CompletedSection.Visibility = Visibility.Collapsed;
            if (ClearCompletedButton != null)
            {
                ClearCompletedButton.BeginAnimation(UIElement.OpacityProperty, null);
                ClearCompletedButton.Opacity = 0.7;
                ClearCompletedButton.IsHitTestVisible = true;
                ClearCompletedButton.Visibility = Visibility.Collapsed;
            }
            CompletedList.ItemsSource = null;
        }
        else
        {
            var active = todos.Where(t => !t.IsCompleted).ToList();
            var completed = todos.Where(t => t.IsCompleted).ToList();

            if (active.Count > 0)
            {
                var slots = _settings.DeadlineSlots.OrderBy(s => s).ToList();
                var listItems = new List<object>();
                int activeCount = active.Count;
                int lineIndex = 0;
                var now = DateTime.UtcNow;

                Models.DeadlineItem CreateDeadlineItem(int slot, int idx)
                {
                    var endUtc = _settings.GetDeadlineTimerEnd(idx);
                    bool isTimer = endUtc != null;
                    bool isExpired = false;
                    string timerText = "";
                    string label = _settings.GetDeadlineLabel(idx);
                    string id = _settings.GetDeadlineId(idx);

                    if (isTimer)
                    {
                        var rem = endUtc!.Value - now;
                        if (rem <= TimeSpan.Zero)
                        {
                            timerText = "00:00:00";
                            isExpired = true;
                        }
                        else
                        {
                            int th = (int)rem.TotalHours;
                            timerText = $"{th:D2}:{rem.Minutes:D2}:{rem.Seconds:D2}";
                            isExpired = false;
                        }
                    }

                    return new Models.DeadlineItem
                    {
                        Id = id,
                        Slot = slot,
                        LineIndex = idx,
                        LabelText = label,
                        IsTimer = isTimer,
                        TimerText = timerText,
                        IsExpired = isExpired
                    };
                }

                var deadlineItems = new List<Models.DeadlineItem>();
                for (int i = 0; i < activeCount; i++)
                {
                    while (lineIndex < slots.Count && slots[lineIndex] == i)
                    {
                        var dlItem = CreateDeadlineItem(slots[lineIndex], lineIndex);
                        deadlineItems.Add(dlItem);
                        listItems.Add(dlItem);
                        lineIndex++;
                    }

                    active[i].IsAboveDeadline = (slots.Count == 0 || i < slots[0]);
                    listItems.Add(active[i]);
                }

                while (lineIndex < slots.Count)
                {
                    var dlItem = CreateDeadlineItem(Math.Min(slots[lineIndex], activeCount), lineIndex);
                    deadlineItems.Add(dlItem);
                    listItems.Add(dlItem);
                    lineIndex++;
                }

                // Update grouping, corner radiuses and margins
                var validDeadlineIds = new HashSet<string>(deadlineItems.Select(d => d.Id));
                var groupedSet = new HashSet<string>();

                foreach (var dl in deadlineItems)
                {
                    var dlGrouped = active.Where(t => t.GroupDeadlineId == dl.Id).ToList();
                    dl.HasGroupedTasks = dlGrouped.Count > 0;

                    if (dlGrouped.Count == 1)
                    {
                        dlGrouped[0].CardCornerRadius = new CornerRadius(8);
                        dlGrouped[0].CardMargin = new Thickness(0, 0, 0, 11);
                        groupedSet.Add(dlGrouped[0].Id);
                    }
                    else if (dlGrouped.Count >= 2)
                    {
                        dlGrouped[0].CardCornerRadius = new CornerRadius(8, 8, 0, 0);
                        dlGrouped[0].CardMargin = new Thickness(0, 0, 0, 0);
                        groupedSet.Add(dlGrouped[0].Id);

                        for (int g = 1; g < dlGrouped.Count - 1; g++)
                        {
                            dlGrouped[g].CardCornerRadius = new CornerRadius(0);
                            dlGrouped[g].CardMargin = new Thickness(0, 0, 0, 0);
                            groupedSet.Add(dlGrouped[g].Id);
                        }

                        dlGrouped[^1].CardCornerRadius = new CornerRadius(0, 0, 8, 8);
                        dlGrouped[^1].CardMargin = new Thickness(0, 0, 0, 11);
                        groupedSet.Add(dlGrouped[^1].Id);
                    }
                }

                foreach (var t in active)
                {
                    if (!groupedSet.Contains(t.Id))
                    {
                        t.CardCornerRadius = new CornerRadius(8);
                        t.CardMargin = new Thickness(0, 0, 0, 6);
                        if (!string.IsNullOrEmpty(t.GroupDeadlineId) && !validDeadlineIds.Contains(t.GroupDeadlineId))
                        {
                            t.GroupDeadlineId = null;
                            _todoService.UpdateGroupDeadlineId(t.Id, null);
                        }
                    }
                }

                TodoList.ItemsSource = listItems;
                StartDeadlineTimerIfNeeded();
                if (AddDeadlineFallback != null)
                {
                    AddDeadlineFallback.Visibility = (slots.Count == 0) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            else
            {
                TodoList.ItemsSource = null;
                StartDeadlineTimerIfNeeded();
                if (AddDeadlineFallback != null) AddDeadlineFallback.Visibility = Visibility.Collapsed;
            }

            if (completed.Count > 0)
            {
                CompletedSection.Visibility = Visibility.Visible;
                CompletedText.Text = $"Завершенные ({completed.Count})";
                if (ClearCompletedButton != null)
                {
                    ClearCompletedButton.BeginAnimation(UIElement.OpacityProperty, null);
                    ClearCompletedButton.Opacity = 0.7;
                    ClearCompletedButton.IsHitTestVisible = true;
                    ClearCompletedButton.Visibility = _showCompleted ? Visibility.Visible : Visibility.Collapsed;
                }
                CompletedList.ItemsSource = _showCompleted ? completed : null;
            }
            else
            {
                _showCompleted = false;
                AnimateCompletedArrow(false);
                CompletedSection.Visibility = Visibility.Collapsed;
                if (ClearCompletedButton != null)
                {
                    ClearCompletedButton.BeginAnimation(UIElement.OpacityProperty, null);
                    ClearCompletedButton.Opacity = 0.7;
                    ClearCompletedButton.IsHitTestVisible = true;
                    ClearCompletedButton.Visibility = Visibility.Collapsed;
                }
                CompletedList.ItemsSource = null;
            }
        }

        UpdateTaskCounter();
        ShowUrgentFireIcons();
    }

    private void ShowUrgentFireIcons()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            for (int i = 0; i < TodoList.Items.Count; i++)
            {
                var container = TodoList.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container == null) continue;
                var fire = FindChildByName<Border>(container, "FireBorder");
                if (fire != null)
                {
                    bool isUrgent = (TodoList.Items[i] as Models.TodoItem)?.IsUrgent == true;
                    fire.Visibility = isUrgent ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateTaskCounter()
    {
        var todos = _todoService.GetAll();
        var total = todos.Count;
        var completed = todos.Count(t => t.IsCompleted);
        TaskCounter.Text = $"{completed}/{total}";
    }

    private static System.Drawing.Icon? _normalTrayIcon;
    private static System.Drawing.Icon? _urgentTrayIcon;

    private static System.Drawing.Icon GetTrayIcon(bool isUrgent)
    {
        if (isUrgent)
        {
            return _urgentTrayIcon ??= CreateTrayIcon("btn-urgent.png");
        }
        return _normalTrayIcon ??= CreateTrayIcon("btn.png");
    }

    private void UpdateUrgentIcons()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Icon = GetTrayIcon(_isUrgentMode);
        }
        _tile?.SetUrgent(_isUrgentMode);
    }

    private static System.Drawing.Icon CreateTrayIcon(string fileName = "btn.png")
    {
        try
        {
            // Load from embedded resources
            var resourcePath = $"pack://application:,,,/Images/{fileName}";
            var uri = new Uri(resourcePath, UriKind.Absolute);
            var stream = Application.GetResourceStream(uri);
            if (stream != null)
            {
                var img = System.Drawing.Image.FromStream(stream.Stream);
                var bmp = new System.Drawing.Bitmap(img, 32, 32);
                var iconHandle = bmp.GetHicon();
                return System.Drawing.Icon.FromHandle(iconHandle);
            }
        }
        catch { }

        // Fallback: generate icon programmatically
        var bitmap = new System.Drawing.Bitmap(32, 32);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.Clear(System.Drawing.Color.Transparent);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(31, 100, 169));
        graphics.FillEllipse(brush, 1, 1, 30, 30);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2);
        graphics.DrawLines(pen, new System.Drawing.Point[] { new(8, 16), new(14, 22), new(24, 10) });
        var hIcon = bitmap.GetHicon();
        return System.Drawing.Icon.FromHandle(hIcon);
    }

    // === Window Height Resizing (Edge Glow + HUD) ===
    private bool _isResizingHeight;
    private double _resizeStartScreenDipY;
    private double _resizeStartHeight;

    private double GetScreenDipY(Point windowPoint)
    {
        var screenPoint = PointToScreen(windowPoint);
        var source = PresentationSource.FromVisual(this);
        double dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        return screenPoint.Y / dpiScaleY;
    }

    private void ResizeGrip_MouseEnter(object sender, MouseEventArgs e)
    {
        var anim = new DoubleAnimation(0.75, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ResizeGlowBar.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void ResizeGrip_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isResizingHeight)
        {
            var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            ResizeGlowBar.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }

    private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            e.Handled = true;
            _isResizingHeight = true;
            _resizeStartScreenDipY = GetScreenDipY(e.GetPosition(this));
            _resizeStartHeight = Height;
            (sender as UIElement)?.CaptureMouse();

            var glowAnim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120));
            ResizeGlowBar.BeginAnimation(UIElement.OpacityProperty, glowAnim);

            ShowHeightHud();
        }
    }

    private void ResizeGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isResizingHeight)
        {
            e.Handled = true;
            double currentScreenDipY = GetScreenDipY(e.GetPosition(this));
            double delta = currentScreenDipY - _resizeStartScreenDipY;
            double newHeight = Math.Clamp(_resizeStartHeight + delta, MinHeight, MaxHeight);
            Height = newHeight;
            UpdateHeightHud(newHeight);
        }
    }

    private void ResizeGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isResizingHeight)
        {
            e.Handled = true;
            FinishResize(sender as UIElement);
        }
    }

    private void ResizeGrip_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isResizingHeight)
        {
            FinishResize(sender as UIElement);
        }
    }

    private void FinishResize(UIElement? element)
    {
        _isResizingHeight = false;
        element?.ReleaseMouseCapture();
        HideHeightHud();
        _settings.Height = Height;

        if (!ResizeGripArea.IsMouseOver)
        {
            var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            ResizeGlowBar.BeginAnimation(UIElement.OpacityProperty, anim);
        }
        else
        {
            var anim = new DoubleAnimation(0.75, TimeSpan.FromMilliseconds(160));
            ResizeGlowBar.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }

    private void ShowHeightHud()
    {
        UpdateHeightHud(Height);

        var fade = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        HeightHud.BeginAnimation(UIElement.OpacityProperty, fade);
        HeightHudTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void UpdateHeightHud(double currentHeight)
    {
        HeightHudText.Text = $"{(int)Math.Round(currentHeight)} px";
    }

    private void HideHeightHud()
    {
        var fade = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        var slide = new DoubleAnimation(4.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        HeightHud.BeginAnimation(UIElement.OpacityProperty, fade);
        HeightHudTranslate.BeginAnimation(TranslateTransform.YProperty, slide);
    }
}
