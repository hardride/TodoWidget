using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        var validThemes = new[] { "Dark", "Light", "Kanagawa", "Argentina for Plemyannic", "Terminal", "Amber", "Pixel-76", "Aeropixel", "Syntwave", "Stormcloud", "Deep Antarctic", "Druid", "Hoarfrost", "Coalglow", "Nocturnal K", "Graffity" };
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

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        NewTaskInput.Focus();
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        double activeOpacity = OpacitySlider?.Value ?? 1.0;
        if (MainBorder != null) MainBorder.Opacity = activeOpacity;
        if (OutlineBorder != null) OutlineBorder.Opacity = activeOpacity;
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (MainBorder != null && !_isDragging)
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

        var lastItem = TodoList.Items[^1];

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
            var itemBefore = _todoService.GetAll().FirstOrDefault(t => t.Id == id);
            bool wasUrgent = itemBefore?.IsUrgent == true;
            bool wasCompleted = itemBefore?.IsCompleted == true;

            if (wasCompleted)
            {
                // Unchecking a completed task: return it to active list immediately
                _todoService.Toggle(id);
                _todoService.MoveToTop(id);
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
            var card = FindAncestorOrSelf<Border>(textBlock);
            if (card != null)
            {
                card.IsHitTestVisible = false;

                bool removed = false;
                Action doRemove = () =>
                {
                    if (removed) return;
                    removed = true;
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

    private void TaskBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        if (dep != null)
        {
            // Do not initiate drag if user clicked CheckBox, FireBorder (Urgent), or Delete (✕)
            if (FindAncestorOrSelf<CheckBox>(dep) != null) return;
            if (FindAncestorByName(dep, "FireBorder") != null) return;
            if (dep is TextBlock tb && tb.Tag != null) return;
        }

        if (sender is Border border)
        {
            var item = border.DataContext as Models.TodoItem;
            if (item == null || item.IsCompleted) return;

            _draggedItem = item;
            _draggedBorder = border;
            _dragStartPoint = e.GetPosition(this);
            _isDragging = false;
        }
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
        if (_isDragging && _draggedItem != null && _dropTargetSlot >= 0)
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
            if (draggedIndex >= 0 && _dropTargetSlot != draggedIndex && _dropTargetSlot != draggedIndex + 1)
            {
                if (_dropTargetSlot < draggedIndex)
                {
                    if (TodoList.Items[_dropTargetSlot] is Models.TodoItem targetItem)
                        _todoService.MoveBefore(_draggedItem.Id, targetItem.Id);
                }
                else
                {
                    if (TodoList.Items[_dropTargetSlot - 1] is Models.TodoItem prevItem)
                        _todoService.MoveAfter(_draggedItem.Id, prevItem.Id);
                }
                RefreshList();
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
        RefreshList();
    }

    private void RefreshList()
    {
        var todos = _todoService.GetAll();

        if (_isUrgentMode)
        {
            var urgent = todos.Where(t => t.IsUrgent && !t.IsCompleted).ToList();
            TodoList.ItemsSource = urgent;
            CompletedSection.Visibility = Visibility.Collapsed;
            CompletedList.ItemsSource = null;
        }
        else
        {
            var active = todos.Where(t => !t.IsCompleted).ToList();
            var completed = todos.Where(t => t.IsCompleted).ToList();

            TodoList.ItemsSource = active;

            if (completed.Count > 0)
            {
                CompletedSection.Visibility = Visibility.Visible;
                CompletedText.Text = $"Завершенные ({completed.Count})";
                CompletedArrow.RenderTransform = _showCompleted
                    ? new System.Windows.Media.RotateTransform(90, 6, 6)
                    : null;
                CompletedList.ItemsSource = _showCompleted ? completed : null;
            }
            else
            {
                CompletedSection.Visibility = Visibility.Collapsed;
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
}
