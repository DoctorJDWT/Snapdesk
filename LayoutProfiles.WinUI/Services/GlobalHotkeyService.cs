using System.Runtime.InteropServices;
using LayoutProfiles.WinUI.Models;
using Microsoft.UI.Dispatching;

namespace LayoutProfiles.WinUI.Services;

/// <summary>
/// Hidden message-only HWND that registers system hotkeys and forwards WM_HOTKEY to a UI dispatcher.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const string HostClassName = "SnapdeskGlobalHotkeyHost";
    private const uint WmHotkey = 0x0312;

    private static WindowProcDelegate? s_wndProc;
    private static bool s_classRegistered;

    private readonly Dictionary<int, string> _hotkeyIdToAction = new();
    private IntPtr _hwnd;
    private DispatcherQueue? _dispatcher;
    private Action<string>? _onHotkey;
    private bool _disposed;

    public bool IsRunning => _hwnd != IntPtr.Zero;

    public void Start(
        DispatcherQueue dispatcher,
        IReadOnlyList<HotkeyBinding> bindings,
        Action<string> onHotkey)
    {
        Stop();
        _dispatcher = dispatcher;
        _onHotkey = onHotkey;
        EnsureHostWindow();
        RegisterBindings(bindings);
    }

    public void Stop()
    {
        UnregisterAll();
        DestroyHostWindow();
        _dispatcher = null;
        _onHotkey = null;
    }

    public void ApplyBindings(IReadOnlyList<HotkeyBinding> bindings)
    {
        if (!IsRunning || _dispatcher is null)
        {
            return;
        }

        UnregisterAll();
        RegisterBindings(bindings);
    }

    public bool TryRegisterChord(uint modifiers, uint virtualKey, out int win32Error)
    {
        win32Error = 0;
        if (_hwnd == IntPtr.Zero || virtualKey == 0)
        {
            return false;
        }

        const int probeId = 0xBFFE;
        NativeMethods.UnregisterHotKey(_hwnd, probeId);
        if (NativeMethods.RegisterHotKey(_hwnd, probeId, modifiers, virtualKey))
        {
            NativeMethods.UnregisterHotKey(_hwnd, probeId);
            return true;
        }

        win32Error = Marshal.GetLastWin32Error();
        NativeMethods.UnregisterHotKey(_hwnd, probeId);
        return false;
    }

    public IReadOnlyList<HotkeyRegistrationFailure> RegisterBindingsWithResults(IReadOnlyList<HotkeyBinding> bindings)
    {
        var failures = new List<HotkeyRegistrationFailure>();
        if (_hwnd == IntPtr.Zero)
        {
            return failures;
        }

        UnregisterAll();

        var id = 1;
        foreach (var binding in bindings)
        {
            if (!binding.Enabled || binding.VirtualKey == 0)
            {
                continue;
            }

            if (id > 0xBFFF)
            {
                failures.Add(new HotkeyRegistrationFailure(binding.ActionId, binding.Modifiers, binding.VirtualKey, 0));
                continue;
            }

            if (!NativeMethods.RegisterHotKey(_hwnd, id, binding.Modifiers, binding.VirtualKey))
            {
                failures.Add(new HotkeyRegistrationFailure(
                    binding.ActionId,
                    binding.Modifiers,
                    binding.VirtualKey,
                    Marshal.GetLastWin32Error()));
                id++;
                continue;
            }

            _hotkeyIdToAction[id] = binding.ActionId;
            id++;
        }

        return failures;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }

    private void RegisterBindings(IReadOnlyList<HotkeyBinding> bindings)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var id = 1;
        foreach (var binding in bindings)
        {
            if (!binding.Enabled || binding.VirtualKey == 0)
            {
                continue;
            }

            if (id > 0xBFFF)
            {
                break;
            }

            if (!NativeMethods.RegisterHotKey(_hwnd, id, binding.Modifiers, binding.VirtualKey))
            {
                id++;
                continue;
            }

            _hotkeyIdToAction[id] = binding.ActionId;
            id++;
        }
    }

    private void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero)
        {
            _hotkeyIdToAction.Clear();
            return;
        }

        foreach (var hotkeyId in _hotkeyIdToAction.Keys.ToList())
        {
            NativeMethods.UnregisterHotKey(_hwnd, hotkeyId);
        }

        _hotkeyIdToAction.Clear();
    }

    private void EnsureHostWindow()
    {
        if (_hwnd != IntPtr.Zero)
        {
            return;
        }

        s_wndProc = HostWndProc;
        RegisterHostClass();
        _hwnd = NativeMethods.CreateWindowEx(
            0,
            HostClassName,
            "",
            0,
            0,
            0,
            0,
            0,
            NativeMethods.HwndMessage,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create global hotkey host window.");
        }

        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlpUserdata, GCHandle.ToIntPtr(GCHandle.Alloc(this)));
    }

    private void DestroyHostWindow()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var userData = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlpUserdata);
            if (userData != IntPtr.Zero && GCHandle.FromIntPtr(userData).IsAllocated)
            {
                GCHandle.FromIntPtr(userData).Free();
            }
        }
        catch
        {
            // ignore
        }

        NativeMethods.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    private static void RegisterHostClass()
    {
        if (s_classRegistered)
        {
            return;
        }

        var wc = new NativeMethods.Wndclass
        {
            lpfnWndProc = s_wndProc!,
            lpszClassName = HostClassName,
        };
        var atom = NativeMethods.RegisterClassW(ref wc);
        if (atom == 0 && Marshal.GetLastWin32Error() != 0x00000582)
        {
            throw new InvalidOperationException("Failed to register hotkey host window class.");
        }

        s_classRegistered = true;
    }

    private static IntPtr HostWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WmHotkey)
        {
            var hotkeyId = (int)wParam;
            GlobalHotkeyService? service = null;
            try
            {
                var userData = NativeMethods.GetWindowLongPtr(hWnd, NativeMethods.GwlpUserdata);
                if (userData != IntPtr.Zero && GCHandle.FromIntPtr(userData).Target is GlobalHotkeyService instance)
                {
                    service = instance;
                }
            }
            catch
            {
                // ignore
            }

            service?.OnWmHotkey(hotkeyId);
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void OnWmHotkey(int hotkeyId)
    {
        if (!_hotkeyIdToAction.TryGetValue(hotkeyId, out var actionId)
            || _dispatcher is null
            || _onHotkey is null)
        {
            return;
        }

        _dispatcher.TryEnqueue(() => _onHotkey(actionId));
    }

    private delegate IntPtr WindowProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static class NativeMethods
    {
        public const int GwlpUserdata = -21;
        public static readonly IntPtr HwndMessage = new(-3);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct Wndclass
        {
            public uint style;
            public WindowProcDelegate lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassW(ref Wndclass lpWndClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }
}

public sealed class HotkeyRegistrationFailure
{
    public HotkeyRegistrationFailure(string actionId, uint modifiers, uint virtualKey, int win32Error)
    {
        ActionId = actionId;
        Modifiers = modifiers;
        VirtualKey = virtualKey;
        Win32Error = win32Error;
    }

    public string ActionId { get; }

    public uint Modifiers { get; }

    public uint VirtualKey { get; }

    public int Win32Error { get; }
}
