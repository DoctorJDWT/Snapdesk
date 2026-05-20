using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Windows.UI;

namespace LayoutProfiles.WinUI.Helpers;

public static class ProfileColors
{
    public static Color BaseColorForName(string name)
    {
        var raw = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim();
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(raw));
        var hue = (int.Parse(
            Convert.ToHexString(hash.AsSpan(0, 3)),
            NumberStyles.HexNumber) % 360) / 360.0;

        // HLS with S=0.7, L=0.46 (dark gumball palette)
        HlsToRgb(hue, 0.46, 0.7, out var r, out var g, out var b);
        return Color.FromArgb(255, (byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    public static string Initial(string name)
    {
        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                return char.ToUpperInvariant(ch).ToString();
            }
        }

        return "?";
    }

    public static Color ForegroundFor(Color baseColor)
    {
        var lum = RelativeLuminance(baseColor);
        return lum > 0.58 ? Color.FromArgb(255, 18, 18, 18) : Color.FromArgb(255, 250, 250, 250);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Lin(double u) => u <= 0.03928 ? u / 12.92 : Math.Pow((u + 0.055) / 1.055, 2.4);
        var r = Lin(c.R / 255.0);
        var g = Lin(c.G / 255.0);
        var b = Lin(c.B / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static void HlsToRgb(double h, double l, double s, out double r, out double g, out double b)
    {
        if (s == 0)
        {
            r = g = b = l;
            return;
        }

        double m2 = l <= 0.5 ? l * (1 + s) : l + s - l * s;
        double m1 = 2 * l - m2;
        r = HueToRgb(m1, m2, h + 1.0 / 3.0);
        g = HueToRgb(m1, m2, h);
        b = HueToRgb(m1, m2, h - 1.0 / 3.0);
    }

    private static double HueToRgb(double m1, double m2, double h)
    {
        if (h < 0)
        {
            h += 1;
        }

        if (h > 1)
        {
            h -= 1;
        }

        if (h < 1.0 / 6.0)
        {
            return m1 + (m2 - m1) * h * 6;
        }

        if (h < 0.5)
        {
            return m2;
        }

        if (h < 2.0 / 3.0)
        {
            return m1 + (m2 - m1) * (2.0 / 3.0 - h) * 6;
        }

        return m1;
    }

}
