using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TodoWidget;

public class TileWindow : Window
{
    private readonly Action _onRestore;
    private readonly Border _border;

    public TileWindow(double left, double top, Action onRestore)
    {
        _onRestore = onRestore;

        Title = "ADHD Tile";
        Width = 32;
        Height = 32;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left;
        Top = top;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;

        // Load btn.png
        var img = new BitmapImage();
        img.BeginInit();
        img.UriSource = new Uri("pack://application:,,,/Images/btn.png");
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.EndInit();
        img.Freeze();

        var image = new Image
        {
            Source = img,
            Width = 32,
            Height = 32,
            Stretch = Stretch.Uniform
        };

        _border = new Border
        {
            CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand,
            Width = 32,
            Height = 32,
            Child = image
        };

        _border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 8,
            ShadowDepth = 2,
            Opacity = 0.3,
            Color = Colors.Black
        };

        Content = _border;

        // Focus events for transparency
        Deactivated += (s, e) => Opacity = 0.4;
        Activated += (s, e) => Opacity = 1.0;

        // Drag and click logic
        bool isDragging = false;
        Point dragStart = new();

        _border.MouseLeftButtonDown += (s, e) =>
        {
            isDragging = false;
            dragStart = e.GetPosition(this);
            _border.CaptureMouse();
        };

        _border.MouseMove += (s, e) =>
        {
            if (_border.IsMouseCaptured)
            {
                var pos = e.GetPosition(this);
                if (Math.Abs(pos.X - dragStart.X) > 4 ||
                    Math.Abs(pos.Y - dragStart.Y) > 4)
                {
                    isDragging = true;
                }
                if (isDragging)
                {
                    Left += pos.X - dragStart.X;
                    Top += pos.Y - dragStart.Y;
                }
            }
        };

        _border.MouseLeftButtonUp += (s, e) =>
        {
            _border.ReleaseMouseCapture();
            if (!isDragging)
            {
                _onRestore();
            }
        };
    }
}
