namespace ChudLink.Desktop;
public partial class App : System.Windows.Application
{
    Mutex? singleInstance;
    EventWaitHandle? openWindow;
    RegisteredWaitHandle? windowSignal;
    protected override void OnStartup(StartupEventArgs e)
    {
        singleInstance = new Mutex(true, "Local\\ChudAI.ChudLink.Desktop", out var created);
        if (!created)
        {
            try { using var existing = EventWaitHandle.OpenExisting("Local\\ChudAI.ChudLink.OpenWindow"); existing.Set(); }
            catch (WaitHandleCannotBeOpenedException) { System.Windows.MessageBox.Show("ChudLink is already running. Open it from the system tray.", "ChudLink"); }
            Shutdown(); return;
        }
        openWindow = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ChudAI.ChudLink.OpenWindow");
        windowSignal = ThreadPool.RegisterWaitForSingleObject(openWindow, (_, _) => Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is not { } window) return;
            window.Show(); window.WindowState = WindowState.Normal; window.Activate();
        }), null, Timeout.Infinite, false);
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { windowSignal?.Unregister(null); openWindow?.Dispose(); singleInstance?.Dispose(); base.OnExit(e); }
}
