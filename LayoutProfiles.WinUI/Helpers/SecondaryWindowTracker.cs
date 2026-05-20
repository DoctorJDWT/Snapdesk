using Microsoft.UI.Xaml;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>Tracks secondary windows opened from <see cref="MainWindow"/> so they close when the main window closes.</summary>
internal static class SecondaryWindowTracker
{
    private static readonly object Lock = new();
    private static readonly List<Window> Open = new();

    public static void Register(Window window)
    {
        lock (Lock)
        {
            if (!Open.Contains(window))
            {
                Open.Add(window);
            }
        }

        window.Closed += OnTrackedWindowClosed;
    }

    public static void CloseAll()
    {
        Window[] snapshot;
        lock (Lock)
        {
            snapshot = Open.ToArray();
        }

        foreach (var window in snapshot)
        {
            try
            {
                window.Close();
            }
            catch
            {
                // ignore close failures during shutdown
            }
        }
    }

    private static void OnTrackedWindowClosed(object sender, WindowEventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.Closed -= OnTrackedWindowClosed;
        lock (Lock)
        {
            Open.Remove(window);
        }
    }
}
