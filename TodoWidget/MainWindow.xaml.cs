using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Hardcodet.Wpf.TaskbarNotification;
using TodoWidget.Services;

namespace TodoWidget;

public partial class MainWindow : Window
{
    private readonly TodoService _todoService = new();
    private readonly SettingsService _settings = new();
    private TaskbarIcon? _trayIcon;
    private bool _isPinned = true;

    private static readonly SolidColorBrush PaintbrushColor = new(Color.FromRgb(0xBB, 0x69, 0xCF));
    private static readonly SolidColorBrush PaintbrushHover = new(Color.FromRgb(0xCC, 0x85, 0xDB));

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

    private SolidColorBrush GetHeaderIconColor()
    {
        return (SolidColorBrush)FindResource("HeaderIconColor");
    }

    private SolidColorBrush GetHeaderIconHover()
    {
        return CalculateHoverBrush(GetHeaderIconColor());
    }

    private SolidColorBrush GetAccentColor()
    {
        return (SolidColorBrush)FindResource("bg/checkbox-filled");
    }

    private SolidColorBrush GetAccentHover()
    {
        return CalculateHoverBrush(GetAccentColor());
    }

    public MainWindow()
    {
        // Load saved theme
        var savedTheme = _settings.Theme;
        if (savedTheme == "Aeropixel")
            savedTheme = "Pixel-76";
        if (savedTheme == "Reilly")
            savedTheme = "Amber";
        var validThemes = new[] { "Dark", "Light", "Kanagawa", "Argentina for Plemyannic", "Terminal", "Amber", "Pixel-76", "Syntwave" };
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

        InitializeComponent();
        UpdateBackgroundImage(_currentTheme);
        UpdateThemeCheckmarks();
        Loaded += MainWindow_Loaded;
        Activated += MainWindow_Activated;
        Deactivated += MainWindow_Deactivated;
        RefreshList();

        SetupTrayIcon();
        SetupHeaderIcons();

        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };
    }

    private void MainBorder_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (MainClip != null && MainBorder != null)
            MainClip.Rect = new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight);
    }

    private void SetupHeaderIcons()
    {
        if (ThemeIconPath?.Parent is FrameworkElement themeBtn)
        {
            themeBtn.MouseEnter += (s, e) => ThemeIconPath.Stroke = _showingThemes ? PaintbrushHover : GetHeaderIconHover();
            themeBtn.MouseLeave += (s, e) => ThemeIconPath.Stroke = _showingThemes ? PaintbrushColor : GetHeaderIconColor();
        }

        if (PinIconPath?.Parent is FrameworkElement pinBtn)
        {
            pinBtn.MouseEnter += (s, e) => PinIconPath.Stroke = _isPinned ? GetAccentHover() : GetHeaderIconHover();
            pinBtn.MouseLeave += (s, e) => PinIconPath.Stroke = _isPinned ? GetAccentColor() : GetHeaderIconColor();
        }

        UpdateHeaderIconsTheme();
    }

    private void UpdateHeaderIconsTheme()
    {
        if (ThemeIconPath != null)
        {
            ThemeIconPath.Stroke = _showingThemes ? PaintbrushColor : GetHeaderIconColor();
        }
        UpdatePinIcon();
    }

    private void SetupTrayIcon()
    {
        var icon = CreateTrayIcon();
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
        if (MainBorder != null && OpacitySlider != null)
            MainBorder.Opacity = OpacitySlider.Value;
    }

    private void MainWindow_Deactivated(object? sender, EventArgs e)
    {
        if (MainBorder != null)
            MainBorder.Opacity = 0.4;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            Hide();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _trayIcon?.Dispose();
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
            HeaderTitle.Text = "Themes";
            TaskCounter.Visibility = Visibility.Collapsed;
            ThemeIconPath.Stroke = PaintbrushColor;
            UpdateThemeCheckmarks();
        }
        else
        {
            ThemesView.Visibility = Visibility.Collapsed;
            TasksView.Visibility = Visibility.Visible;
            HeaderTitle.Text = "ADHD to-do";
            TaskCounter.Visibility = Visibility.Visible;
            ThemeIconPath.Stroke = GetHeaderIconColor();
        }
    }

    private void DarkTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Dark");
    private void LightTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Light");
    private void KanagawaTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Kanagawa");
    private void ArgentinaTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Argentina for Plemyannic");
    private void TerminalTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Terminal");
    private void AmberTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Amber");
    private void Pixel76Theme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Pixel-76");
    private void SyntwaveTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Syntwave");

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

        UpdateBackgroundImage(theme);
        UpdateHeaderIconsTheme();
        UpdateThemeCheckmarks();
    }

    private void UpdateBackgroundImage(string theme)
    {
        if (ThemeBackgroundImage == null) return;

        if (theme == "Argentina for Plemyannic")
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri("pack://application:,,,/Images/bg_a.png");
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            ThemeBackgroundImage.Source = img;
            ThemeBackgroundImage.Opacity = 1;
        }
        else if (theme == "Pixel-76")
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri("pack://application:,,,/Images/bg_aerop.png");
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            ThemeBackgroundImage.Source = img;
            ThemeBackgroundImage.Opacity = 1;
        }
        else
        {
            ThemeBackgroundImage.Source = null;
            ThemeBackgroundImage.Opacity = 0;
        }
    }

    private void UpdateThemeCheckmarks()
    {
        DarkCheck.Opacity = _currentTheme == "Dark" ? 1 : 0;
        LightCheck.Opacity = _currentTheme == "Light" ? 1 : 0;
        KanagawaCheck.Opacity = _currentTheme == "Kanagawa" ? 1 : 0;
        ArgentinaCheck.Opacity = _currentTheme == "Argentina for Plemyannic" ? 1 : 0;
        TerminalCheck.Opacity = _currentTheme == "Terminal" ? 1 : 0;
        AmberCheck.Opacity = _currentTheme == "Amber" ? 1 : 0;
        Pixel76Check.Opacity = _currentTheme == "Pixel-76" ? 1 : 0;
        SyntwaveCheck.Opacity = _currentTheme == "Syntwave" ? 1 : 0;
    }

    // === Pin ===
    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        Topmost = _isPinned;
        UpdatePinIcon();
    }

    private void UpdatePinIcon()
    {
        if (PinIconPath == null) return;

        if (_isPinned)
        {
            PinIconPath.Data = (Geometry)FindResource("PinIcon");
            PinIconPath.Stroke = GetAccentColor();
        }
        else
        {
            PinIconPath.Data = (Geometry)FindResource("PinOffIcon");
            PinIconPath.Stroke = GetHeaderIconColor();
        }
    }

    // === Opacity ===
    private void OpacityButton_Click(object sender, RoutedEventArgs e)
    {
        OpacitySliderBorder.Visibility =
            OpacitySliderBorder.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private void OpacitySlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MainBorder == null || OpacityValueText == null) return;
        MainBorder.Opacity = e.NewValue;
        OpacityValueText.Text = $"{(int)(e.NewValue * 100)}%";
    }

    // === Tile ===
    private TileWindow? _tile;

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_tile != null)
        {
            var tile = _tile;
            _tile = null;
            tile.Close();
        }
        _tile = new TileWindow(Left + Width / 2 - 16, Top + Height / 2 - 16, RestoreWidget);
        _tile.Show();
        Hide();
    }

    public void RestoreWidget()
    {
        if (_tile != null)
        {
            var tile = _tile;
            _tile = null;
            Left = tile.Left + 16 - Width / 2;
            Top = tile.Top + 16 - Height / 2;
            tile.Close();
        }
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    private void ShowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        RestoreWidget();
    }

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
            NewTaskInput.Text = string.Empty;
            WatermarkText.Visibility = Visibility.Visible;
        }
    }

    private void TodoCheckbox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkbox && checkbox.Tag is string id)
        {
            _todoService.Toggle(id);
            RefreshList();
        }
    }

    private void DeleteText_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBlock textBlock && textBlock.Tag is string id)
        {
            _todoService.Remove(id);
            RefreshList();
        }
    }

    private void RefreshList()
    {
        var todos = _todoService.GetAll();
        TodoList.ItemsSource = null;
        TodoList.ItemsSource = todos;
        var total = todos.Count;
        var completed = todos.Count(t => t.IsCompleted);
        TaskCounter.Text = $"{completed}/{total}";
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Images", "btn.png");
        if (!System.IO.File.Exists(path))
        {
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

        var img = System.Drawing.Image.FromFile(path);
        var bmp = new System.Drawing.Bitmap(img, 32, 32);
        var iconHandle = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(iconHandle);
    }
}
