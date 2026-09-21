using System.Threading;
using System.Windows;

namespace TodoWidget;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static EventWaitHandle? _instanceEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == "--screenshot")
        {
            string outPath = e.Args.Length > 1 ? e.Args[1] : "preview.png";
            string theme = e.Args.Length > 2 ? e.Args[2] : "";
            if (!string.IsNullOrEmpty(theme))
            {
                new Services.SettingsService().Theme = theme;
            }
            var win = new MainWindow();
            win.Loaded += (s, ev) =>
            {
                win.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    if (!string.IsNullOrEmpty(theme))
                    {
                        win.ApplyTheme(theme);
                    }
                    if (win.MainBorder != null)
                    {
                        win.MainBorder.Opacity = 1.0;
                    }
                    int w = (int)win.ActualWidth;
                    int h = (int)win.ActualHeight;
                    if (w <= 0) w = 320;
                    if (h <= 0) h = 480;
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(win);

                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using (var fs = System.IO.File.Create(outPath))
                    {
                        encoder.Save(fs);
                    }
                    win.Close();
                    Shutdown();
                });
            };
            win.Show();
            return;
        }

        const string eventName = @"Global\TodoWidget_SingleInstance_Event";
        _instanceEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // Another instance is already running -> signal it to restore and bring to front, then exit
            _instanceEvent.Set();
            Shutdown();
            return;
        }

        // Listen for signals from duplicate launch attempts
        ThreadPool.QueueUserWorkItem(_ =>
        {
            while (_instanceEvent.WaitOne())
            {
                Current?.Dispatcher.BeginInvoke(() =>
                {
                    if (Current?.MainWindow is MainWindow mw)
                    {
                        mw.RestoreWidget();
                    }
                });
            }
        });

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceEvent?.Dispose();
        base.OnExit(e);
    }
}

