using System.Text.Json;
using LayoutProfiles.WinUI.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WinRT.Interop;

namespace LayoutProfiles.WinUI.Helpers;

internal sealed class WindowGeometryController
{
    private readonly Window _window;
    private readonly Func<AppWindow> _getAppWindow;
    private readonly SettingsService _settings;
    private readonly Func<Dictionary<string, JsonElement>> _getSettingsCache;
    private readonly Action<Dictionary<string, JsonElement>> _setSettingsCache;
    private readonly Func<int> _getMinClientWidth;
    private readonly Func<int> _getMinClientHeight;
    private readonly DispatcherTimer _saveTimer;

    private string? _lastSavedGeometry;
    private bool _restoreAttempted;
    private bool _readyForPersist;

    public WindowGeometryController(
        Window window,
        Func<AppWindow> getAppWindow,
        SettingsService settings,
        Func<Dictionary<string, JsonElement>> getSettingsCache,
        Action<Dictionary<string, JsonElement>> setSettingsCache,
        Func<int> getMinClientWidth,
        Func<int> getMinClientHeight)
    {
        _window = window;
        _getAppWindow = getAppWindow;
        _settings = settings;
        _getSettingsCache = getSettingsCache;
        _setSettingsCache = setSettingsCache;
        _getMinClientWidth = getMinClientWidth;
        _getMinClientHeight = getMinClientHeight;
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _saveTimer.Tick += OnSaveTimerTick;
    }

    public bool IsReadyForPersist => _readyForPersist;

    public WidgetSizePreset RestoreSavedOrDefaultGeometry(
        int defaultWidth,
        int defaultHeight,
        Action<WidgetSizePreset, bool> applyPreset)
    {
        if (_restoreAttempted)
        {
            return WidgetSizePreset.OneByOne;
        }

        _restoreAttempted = true;
        var preset = _settings.GetWidgetSizePreset(_getSettingsCache());
        if (preset == WidgetSizePreset.Custom)
        {
            preset = WidgetSizePreset.OneByOne;
        }

        if (TryGetSavedPosition(out var x, out var y))
        {
            try
            {
                _getAppWindow().Move(new PointInt32(x, y));
            }
            catch
            {
                // ignore
            }
        }
        else
        {
            TrySetDefaultWindowSize(defaultWidth, defaultHeight);
        }

        applyPreset(preset, false);
        StartupTrace.Write($"Applied widget size preset {WidgetSizePresets.ToSettingsValue(preset)}");

        _readyForPersist = true;
        return preset;
    }

    public void ScheduleSave()
    {
        if (!_readyForPersist)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void Persist(bool force = false)
    {
        try
        {
            if (!TryReadWindowGeometry(out var w, out var h, out var x, out var y))
            {
                return;
            }

            var minH = _getMinClientHeight();
            var minW = _getMinClientWidth();
            (w, h) = WindowGeometryHelper.ClampWidgetSize(w, h, minW, minH);
            if (!WindowGeometryHelper.IsPlausibleWidgetSize(w, h, minW, minH))
            {
                return;
            }

            var geom = SettingsService.FormatTkGeometry(w, h, x, y);
            if (!force && geom == _lastSavedGeometry)
            {
                return;
            }

            _lastSavedGeometry = geom;
            _settings.MergeAndSave(new Dictionary<string, object?> { ["window_geometry"] = geom });
            _setSettingsCache(_settings.Load());
            StartupTrace.Write($"Saved window geometry {geom}");
        }
        catch (Exception ex)
        {
            CrashLog.Write("PersistWindowGeometry", ex);
        }
    }

    private void OnSaveTimerTick(object? sender, object e)
    {
        _saveTimer.Stop();
        Persist();
    }

    private bool TryGetSavedPosition(out int x, out int y)
    {
        x = 0;
        y = 0;
        var spec = SettingsService.ParseWindowGeometry(_getSettingsCache());
        if (spec is null)
        {
            return false;
        }

        x = spec.Value.X;
        y = spec.Value.Y;
        return true;
    }

    private bool TryApplySavedGeometry()
    {
        var spec = SettingsService.ParseWindowGeometry(_getSettingsCache());
        if (spec is null)
        {
            return false;
        }

        var minWidth = _getMinClientWidth();
        var minHeight = _getMinClientHeight();
        if (!WindowGeometryHelper.IsPlausibleWidgetSize(
                spec.Value.Width,
                spec.Value.Height,
                minWidth,
                minHeight))
        {
            StartupTrace.Write(
                $"Ignoring implausible window_geometry "
                + $"{spec.Value.Width}x{spec.Value.Height}+{spec.Value.X}+{spec.Value.Y}");
            ClearSavedWindowGeometry();
            return false;
        }

        var (w, h, x, y) = WindowGeometryHelper.ClampToVirtualScreen(
            spec.Value.Width,
            spec.Value.Height,
            spec.Value.X,
            spec.Value.Y,
            minWidth,
            minHeight);

        if (!WindowGeometryHelper.IntersectsAnyWorkArea(x, y, w, h))
        {
            ClearSavedWindowGeometry();
            return false;
        }

        try
        {
            var appWindow = _getAppWindow();
            appWindow.Resize(new SizeInt32(w, h));
            appWindow.Move(new PointInt32(x, y));
            _lastSavedGeometry = SettingsService.FormatTkGeometry(w, h, x, y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ClearSavedWindowGeometry()
    {
        try
        {
            _settings.MergeAndSave(new Dictionary<string, object?> { ["window_geometry"] = null });
            var cache = _getSettingsCache();
            cache.Remove("window_geometry");
        }
        catch
        {
            // ignore
        }
    }

    private void TrySetDefaultWindowSize(int width, int height)
    {
        try
        {
            WindowGeometryHelper.CenterOnPrimaryWorkArea(_getAppWindow(), width, height);
        }
        catch
        {
            // ignore
        }
    }

    private bool TryReadWindowGeometry(out int width, out int height, out int x, out int y)
    {
        width = 0;
        height = 0;
        x = 0;
        y = 0;

        try
        {
            var appWindow = _getAppWindow();
            var size = appWindow.Size;
            var pos = appWindow.Position;
            if (size.Width > 0 && size.Height > 0)
            {
                width = size.Width;
                height = size.Height;
                x = pos.X;
                y = pos.Y;
                return true;
            }
        }
        catch
        {
            // fall through to Win32
        }

        try
        {
            var hwnd = WindowNative.GetWindowHandle(_window);
            if (WindowGeometryHelper.TryGetWindowRect(hwnd, out var rect))
            {
                width = rect.Width;
                height = rect.Height;
                x = rect.X;
                y = rect.Y;
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }
}
