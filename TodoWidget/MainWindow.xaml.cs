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

    private static readonly SolidColorBrush AccentColor = new(Color.FromRgb(0x1F, 0x64, 0xA9));
    private static readonly SolidColorBrush AccentHover = new(Color.FromRgb(0x28, 0x78, 0xC8));
    private static readonly SolidColorBrush PaintbrushColor = new(Color.FromRgb(0xBB, 0x69, 0xCF));
    private static readonly SolidColorBrush PaintbrushHover = new(Color.FromRgb(0xCC, 0x85, 0xDB));

    private SolidColorBrush GetHeaderIconColor()
    {
        return (SolidColorBrush)FindResource("HeaderIconColor");
    }

    private SolidColorBrush GetHeaderIconHover()
    {
        var c = GetHeaderIconColor().Color;
        return new SolidColorBrush(Color.FromArgb(c.A,
            (byte)Math.Min(255, c.R + 0x1A),
            (byte)Math.Min(255, c.G + 0x1A),
            (byte)Math.Min(255, c.B + 0x1A)));
    }

    public MainWindow()
    {
        // Load saved theme
        var savedTheme = _settings.Theme;
        var validThemes = new[] { "Dark", "Light", "Kanagawa", "Argentina for Plemyannic", "Terminal", "Reilly" };
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

        InitializeComponent();
        Loaded += MainWindow_Loaded;
        Activated += MainWindow_Activated;
        Deactivated += MainWindow_Deactivated;
        RefreshList();

        SetupTrayIcon();
        SetupIconHover(ThemeIconPath, GetHeaderIconColor(), GetHeaderIconHover());
        SetupIconHover(PinIconPath, _isPinned ? AccentColor : GetHeaderIconColor(), _isPinned ? AccentHover : GetHeaderIconHover());

        MouseLeftButtonDown += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        };
    }

    private void SetupIconHover(Path icon, SolidColorBrush normal, SolidColorBrush hover)
    {
        if (icon?.Parent is FrameworkElement element)
        {
            element.MouseEnter += (s, e) => icon.Stroke = hover;
            element.MouseLeave += (s, e) => icon.Stroke = normal;
        }
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

        _trayIcon.TrayMouseDoubleClick += (s, e) =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };
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
            SetupIconHover(ThemeIconPath, PaintbrushColor, PaintbrushHover);
            UpdateThemeCheckmarks();
        }
        else
        {
            ThemesView.Visibility = Visibility.Collapsed;
            TasksView.Visibility = Visibility.Visible;
            HeaderTitle.Text = "ADHD to-do";
            TaskCounter.Visibility = Visibility.Visible;
            ThemeIconPath.Stroke = GetHeaderIconColor();
            SetupIconHover(ThemeIconPath, GetHeaderIconColor(), GetHeaderIconHover());
        }
    }

    private void DarkTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Dark");
    private void LightTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Light");
    private void KanagawaTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Kanagawa");
    private void ArgentinaTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Argentina for Plemyannic");
    private void TerminalTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Terminal");
    private void ReillyTheme_Click(object sender, MouseButtonEventArgs e) => ApplyTheme("Reilly");

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

        if (MainBorder != null) MainBorder.Opacity = savedOpacity;

        // Show/hide background image for Argentina theme
        if (ThemeBackgroundImage != null)
        {
            if (theme == "Argentina for Plemyannic")
            {
                // Load directly from disk (no caching) so replaced files take effect
                var img = new BitmapImage();
                img.BeginInit();
                img.UriSource = new Uri("pack://application:,,,/Images/bg_a.png");
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

        UpdateThemeCheckmarks();
    }

    private void UpdateThemeCheckmarks()
    {
        DarkCheck.Opacity = _currentTheme == "Dark" ? 1 : 0;
        LightCheck.Opacity = _currentTheme == "Light" ? 1 : 0;
        KanagawaCheck.Opacity = _currentTheme == "Kanagawa" ? 1 : 0;
        ArgentinaCheck.Opacity = _currentTheme == "Argentina for Plemyannic" ? 1 : 0;
        TerminalCheck.Opacity = _currentTheme == "Terminal" ? 1 : 0;
        ReillyCheck.Opacity = _currentTheme == "Reilly" ? 1 : 0;
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
            PinIconPath.Stroke = AccentColor;
            SetupIconHover(PinIconPath, AccentColor, AccentHover);
        }
        else
        {
            PinIconPath.Data = (Geometry)FindResource("PinOffIcon");
            PinIconPath.Stroke = GetHeaderIconColor();
            SetupIconHover(PinIconPath, GetHeaderIconColor(), GetHeaderIconHover());
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

    // === Tray ===
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        // Show tile at current widget position
        _tile = new TileWindow(Left + Width / 2 - 16, Top + Height / 2 - 16, RestoreFromTile);
        _tile.Show();
        Hide();
    }

    private void RestoreFromTile()
    {
        if (_tile != null)
        {
            Left = _tile.Left + 16 - Width / 2;
            Top = _tile.Top + 16 - Height / 2;
        }
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ShowMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
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
