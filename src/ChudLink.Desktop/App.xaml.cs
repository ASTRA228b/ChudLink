namespace ChudLink.Desktop;
public partial class App : System.Windows.Application
{
    Mutex? singleInstance;
    protected override void OnStartup(StartupEventArgs e)
    {
        singleInstance = new Mutex(true, "Local\\ChudAI.ChudLink.Desktop", out var created);
        if (!created) { System.Windows.MessageBox.Show("ChudLink is already running. Open it from the system tray.", "ChudLink"); Shutdown(); return; }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e) { singleInstance?.Dispose(); base.OnExit(e); }
}
