using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>
/// Resolves which monitor a docked widget belongs to (multi-monitor safe).
/// </summary>
internal static class DockDisplayHelper
{
    public const int DockThresholdPixels = 18;
    private const int NearestDockMaxPixels = 48;

    public static bool TryResolveDockedDisplay(
        AppWindow appWindow,
        out WindowDockEdge edge,
        out DisplayArea display)
    {
        edge = WindowDockEdge.None;
        display = null!;

        try
        {
            var size = appWindow.Size;
            var pos = appWindow.Position;
            if (size.Width <= 0 || size.Height <= 0)
            {
                return false;
            }

            // FindAll is unreliable in WinUI 3 from secondary threads/contexts; use
            // GetFromPoint on the window center, then check edges against that monitor.
            // Fallback to each window corner if GetFromPoint returns null.
            var anchors = new[]
            {
                new PointInt32(pos.X + size.Width / 2, pos.Y + size.Height / 2),
                new PointInt32(pos.X, pos.Y),
                new PointInt32(pos.X + size.Width - 1, pos.Y),
                new PointInt32(pos.X, pos.Y + size.Height - 1),
                new PointInt32(pos.X + size.Width - 1, pos.Y + size.Height - 1),
            };

            foreach (var anchor in anchors)
            {
                var candidate = DisplayArea.GetFromPoint(anchor, DisplayAreaFallback.Nearest);
                if (candidate is null)
                {
                    continue;
                }

                var work = candidate.WorkArea;
                if (TryPickEdge(pos, size, work, out var picked))
                {
                    edge = picked;
                    display = candidate;
                    return true;
                }

                // First anchor that resolves a display wins for nearest-edge fallback.
                if (TryGetNearestEdge(pos, size, work, out var nearestEdge, out _))
                {
                    edge = nearestEdge;
                    display = candidate;
                    return true;
                }

                display = candidate;
                return false;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetDisplayByWorkArea(RectInt32 workArea, out DisplayArea display)
    {
        display = null!;
        try
        {
            // Use the work area centre to locate the matching display via GetFromPoint.
            var center = new PointInt32(
                workArea.X + workArea.Width / 2,
                workArea.Y + workArea.Height / 2);
            var resolved = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest);
            if (resolved is null)
            {
                return false;
            }

            display = resolved;
            return true;
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static bool TryPickEdge(PointInt32 pos, SizeInt32 size, RectInt32 work, out WindowDockEdge edge)
    {
        edge = WindowDockEdge.None;
        foreach (var candidateEdge in GetMatchingEdges(pos, size, work))
        {
            edge = candidateEdge;
            return true;
        }

        return false;
    }

    public static bool WorkAreasMatch(RectInt32 a, RectInt32 b, int tolerancePx = 2) =>
        Math.Abs(a.X - b.X) <= tolerancePx
        && Math.Abs(a.Y - b.Y) <= tolerancePx
        && Math.Abs(a.Width - b.Width) <= tolerancePx
        && Math.Abs(a.Height - b.Height) <= tolerancePx;

    public static PointInt32 GetVisibleDockPosition(
        AppWindow appWindow,
        WindowDockEdge edge,
        DisplayArea display)
    {
        var size = appWindow.Size;
        var work = display.WorkArea;
        var x = Math.Clamp(
            appWindow.Position.X,
            work.X,
            Math.Max(work.X, work.X + work.Width - size.Width));
        var y = Math.Clamp(
            appWindow.Position.Y,
            work.Y,
            Math.Max(work.Y, work.Y + work.Height - size.Height));

        return edge switch
        {
            WindowDockEdge.Left => new PointInt32(work.X, y),
            WindowDockEdge.Right => new PointInt32(work.X + work.Width - size.Width, y),
            WindowDockEdge.Top => new PointInt32(x, work.Y),
            WindowDockEdge.Bottom => new PointInt32(x, work.Y + work.Height - size.Height),
            _ => appWindow.Position,
        };
    }

    /// <summary>
    /// Slide a docked window along its edge (used while auto-hidden and dragging the reveal pill).
    /// </summary>
    public static PointInt32 SlideAlongDockEdge(
        WindowDockEdge edge,
        PointInt32 windowStart,
        int deltaX,
        int deltaY,
        SizeInt32 size,
        RectInt32 work)
    {
        var target = new PointInt32(windowStart.X + deltaX, windowStart.Y + deltaY);
        var x = Math.Clamp(
            target.X,
            work.X,
            Math.Max(work.X, work.X + work.Width - size.Width));
        var y = Math.Clamp(
            target.Y,
            work.Y,
            Math.Max(work.Y, work.Y + work.Height - size.Height));

        return edge switch
        {
            WindowDockEdge.Left => new PointInt32(work.X, y),
            WindowDockEdge.Right => new PointInt32(work.X + work.Width - size.Width, y),
            WindowDockEdge.Top => new PointInt32(x, work.Y),
            WindowDockEdge.Bottom => new PointInt32(x, work.Y + work.Height - size.Height),
            _ => new PointInt32(x, y),
        };
    }

    private static bool TryGetNearestEdge(
        PointInt32 pos,
        SizeInt32 size,
        RectInt32 work,
        out WindowDockEdge edge,
        out int distancePx)
    {
        edge = WindowDockEdge.None;
        distancePx = int.MaxValue;

        var candidates = new (WindowDockEdge Edge, int Distance)[]
        {
            (WindowDockEdge.Left, Math.Abs(pos.X - work.X)),
            (WindowDockEdge.Right, Math.Abs(pos.X + size.Width - (work.X + work.Width))),
            (WindowDockEdge.Top, Math.Abs(pos.Y - work.Y)),
            (WindowDockEdge.Bottom, Math.Abs(pos.Y + size.Height - (work.Y + work.Height))),
        };

        foreach (var (candidateEdge, candidateDistance) in candidates)
        {
            if (candidateDistance < distancePx)
            {
                distancePx = candidateDistance;
                edge = candidateEdge;
            }
        }

        return edge != WindowDockEdge.None && distancePx <= NearestDockMaxPixels;
    }

    private static IEnumerable<WindowDockEdge> GetMatchingEdges(
        PointInt32 pos,
        SizeInt32 size,
        RectInt32 work)
    {
        if (Math.Abs(pos.X - work.X) <= DockThresholdPixels)
        {
            yield return WindowDockEdge.Left;
        }

        if (Math.Abs(pos.X + size.Width - (work.X + work.Width)) <= DockThresholdPixels)
        {
            yield return WindowDockEdge.Right;
        }

        if (Math.Abs(pos.Y - work.Y) <= DockThresholdPixels)
        {
            yield return WindowDockEdge.Top;
        }

        if (Math.Abs(pos.Y + size.Height - (work.Y + work.Height)) <= DockThresholdPixels)
        {
            yield return WindowDockEdge.Bottom;
        }
    }

    private static long IntersectionArea(RectInt32 a, RectInt32 b)
    {
        var left = Math.Max(a.X, b.X);
        var top = Math.Max(a.Y, b.Y);
        var right = Math.Min(a.X + a.Width, b.X + b.Width);
        var bottom = Math.Min(a.Y + a.Height, b.Y + b.Height);
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return 0;
        }

        return (long)width * height;
    }
}
