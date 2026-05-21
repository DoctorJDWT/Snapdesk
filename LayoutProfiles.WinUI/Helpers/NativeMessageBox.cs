using System.Runtime.InteropServices;

namespace LayoutProfiles.WinUI.Helpers;

internal static class NativeMessageBox
{
    public const uint MbOk = 0x00000000;
    public const uint MbYesNo = 0x00000004;
    public const int IdYes = 6;
    public const uint MbIconError = 0x00000010;
    public const uint MbIconWarning = 0x00000030;
    public const uint MbIconInformation = 0x00000040;
    public const uint MbIconQuestion = 0x00000020;

    public static int Show(string title, string message, uint type)
    {
        try
        {
            return MessageBoxW(IntPtr.Zero, message, title, type);
        }
        catch
        {
            return 0;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, BestFitMapping = false)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
