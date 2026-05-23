using System.Drawing;
using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Dispatching;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>System tray icon for unpackaged WinUI — uses WinForms <see cref="System.Windows.Forms.NotifyIcon"/>.</summary>
internal sealed class TrayIconHelper : IDisposable
{
    private readonly DispatcherQueue _dispatcher;
    private readonly Action _showWidget;
    private readonly Action _exitApp;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _disposed;

    public TrayIconHelper(DispatcherQueue dispatcher, Action showWidget, Action exitApp)
    {
        _dispatcher = dispatcher;
        _showWidget = showWidget;
        _exitApp = exitApp;
        CreateNotifyIcon();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_notifyIcon is null)
        {
            return;
        }

        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIcon = null;
    }

    private void CreateNotifyIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        var openItem = new System.Windows.Forms.ToolStripMenuItem("Open Snapdesk");
        openItem.Click += (_, _) => RunOnUiThread(_showWidget);
        menu.Items.Add(openItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit Snapdesk");
        exitItem.Click += (_, _) => RunOnUiThread(_exitApp);
        menu.Items.Add(exitItem);

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Snapdesk",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => RunOnUiThread(_showWidget);
    }

    private void RunOnUiThread(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed)
            {
                action();
            }
        });
    }

    private static Icon LoadTrayIcon()
    {
        foreach (var path in CandidateIconPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                return new Icon(path);
            }
            catch
            {
                // try next path
            }
        }

        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            try
            {
                var extracted = Icon.ExtractAssociatedIcon(exePath);
                if (extracted is not null)
                {
                    return extracted;
                }
            }
            catch
            {
                // fall through
            }
        }

        return SystemIcons.Application;
    }

    private static IEnumerable<string> CandidateIconPaths()
    {
        yield return Path.Combine(RepoPaths.RepoRoot, "assets", "app_icon.ico");
        yield return Path.Combine(AppContext.BaseDirectory, "app_icon.ico");
        yield return Path.Combine(AppContext.BaseDirectory, "assets", "app_icon.ico");
    }
}
