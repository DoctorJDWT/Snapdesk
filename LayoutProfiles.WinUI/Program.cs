using LayoutProfiles.WinUI.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LayoutProfiles.WinUI;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        StartupTrace.Write("Main begin");
        LogBuildIdentity();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                CrashLog.Write("AppDomain.UnhandledException", ex);
                StartupTrace.Write($"AppDomain.UnhandledException: {ex.Message}");
            }
        };

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(
                DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            StartupTrace.Write("Application.Start callback");
            _ = new App();
        });

        StartupTrace.Write("Main end");
    }

    private static void LogBuildIdentity()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        StartupTrace.Write($"Exe: {exePath}");
        try
        {
            StartupTrace.Write($"BuildUtc: {File.GetLastWriteTimeUtc(exePath):u}");
        }
        catch
        {
            // ignore
        }
    }
}
