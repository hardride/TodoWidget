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

