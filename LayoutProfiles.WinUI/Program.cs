using System.Threading;
using LayoutProfiles.WinUI.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LayoutProfiles.WinUI;

public static class Program
{
    private const string SingleInstanceMutexName = "Local\\Snapdesk.SingleInstance";
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        if (!TryAcquireSingleInstance())
        {
            NativeMessageBox.Show(
                "Snapdesk",
                "Snapdesk is already running.",
                NativeMessageBox.MbOk | NativeMessageBox.MbIconInformation);
            return;
        }

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
        ReleaseSingleInstance();
    }

    private static bool TryAcquireSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var createdNew);
            return createdNew;
        }
        catch
        {
            return false;
        }
    }

    private static void ReleaseSingleInstance()
    {
        try
        {
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch
        {
            // ignore
        }
        finally
        {
            _singleInstanceMutex = null;
        }
    }

    private static void LogBuildIdentity()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        StartupTrace.Write($"Exe: {exePath}");
        CrashLog.WriteDiagnostic("Startup", CrashLog.FormatAssemblyIdentity());
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
