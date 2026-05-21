using LayoutProfiles.WinUI.Helpers;
using Microsoft.UI.Xaml;

namespace LayoutProfiles.WinUI;

public partial class App : Application
{
    private Window? _window;

    public static Window? MainWindowInstance => (Current as App)?._window;

    public App()
    {
        StartupTrace.Write("App.ctor begin");
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        StartupTrace.Write("App.ctor end");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            StartupTrace.Write("OnLaunched begin");
            // BuildUi runs in MainWindow ctor before FinishStartup; palette must exist in published builds
            // where App.xaml resources may not be merged yet. System until settings load (FinishStartup).
            AppTheme.Initialize(ThemePreference.System);
            var main = new MainWindow();
            _window = main;
            StartupTrace.Write("MainWindow created");
            main.Activate();
            StartupTrace.Write("MainWindow.Activate done");
            main.DispatcherQueue.TryEnqueue(() => main.FinishStartup());
        }
        catch (Exception ex)
        {
            CrashLog.Write("OnLaunched", ex);
            StartupTrace.Write($"OnLaunched FAILED: {ex.Message}");
            throw;
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        CrashLog.Write("Application.UnhandledException", e.Exception);
        e.Handled = true;
    }
}
