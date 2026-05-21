using System.Runtime.InteropServices;
using LayoutProfiles.WinUI.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace LayoutProfiles.WinUI.Helpers;

/// <summary>
/// Hold and drag profile gumballs to reorder (insert-before semantics). Pointer handling lives on
/// the chip host so moves that leave the 30px circle still count; the + chip is never draggable.
/// The insertion indicator lives in a sibling overlay so it is never wiped by chip-host rebuilds.
/// </summary>
internal sealed class ProfileChipReorderController
{
    private const int VkLButton = 0x01;
    private const int DragThresholdPixels = 5;
    private const double DraggingScale = 1.15;
    private const double DraggingOpacity = 0.85;

    private readonly Grid _chipHost;
    private readonly Panel _insertionLineHost;
    private readonly Func<IReadOnlyList<Button>> _getProfileButtons;
    private readonly Action<int, int> _onReorder;
    private readonly Func<bool> _isDisabled;

    private readonly PointerEventHandler _hostPointerPressed;
    private readonly PointerEventHandler _hostPointerMoved;
    private readonly PointerEventHandler _hostPointerReleased;
    private readonly PointerEventHandler _hostPointerCanceled;
    private readonly PointerEventHandler _hostPointerCaptureLost;

    private readonly Border _insertionLine;
    private Button? _activeChip;
    private Pointer? _activePointer;
    private Point _pressPosition;
    private Point _lastPointerPosition;
    private bool _hasPointerPosition;
    private bool _tracking;
    private bool _dragging;
    private int _fromIndex = -1;
    private int _insertBeforeIndex = -1;
    private Button? _suppressClickFor;

    /// <summary>
    /// Set true while we are in the middle of <see cref="BeginTracking"/> capturing the pointer.
    /// WinUI raises PointerCaptureLost on the child Button (which had the implicit ButtonBase capture)
    /// when we steal capture; that event bubbles to the chip host and would otherwise abort tracking
    /// before the user has had a chance to drag. We use this latch to suppress that single spurious
    /// capture-lost event.
    /// </summary>
    private bool _suppressNextCaptureLost;

    public ProfileChipReorderController(
        Window window,
        Grid chipHost,
        Panel insertionLineHost,
        Func<IReadOnlyList<Button>> getProfileButtons,
        Action<int, int> onReorder,
        Func<bool> isDisabled)
    {
        _ = window;
        _chipHost = chipHost;
        _insertionLineHost = insertionLineHost;
        _getProfileButtons = getProfileButtons;
        _onReorder = onReorder;
        _isDisabled = isDisabled;

        _insertionLine = new Border
        {
            Height = 2,
            Background = GetAccentBrush(),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        Canvas.SetZIndex(_insertionLine, 1000);
        if (!_insertionLineHost.Children.Contains(_insertionLine))
        {
            _insertionLineHost.Children.Add(_insertionLine);
        }

        _hostPointerPressed = (_, e) => OnHostPointerPressed(e);
        _hostPointerMoved = (_, e) => OnHostPointerMoved(e);
        _hostPointerReleased = (_, e) => TryFinishFromHost(e.Pointer);
        _hostPointerCanceled = (_, e) => TryFinishFromHost(e.Pointer);
        _hostPointerCaptureLost = (_, e) => OnHostPointerCaptureLost(e);

        _chipHost.AddHandler(UIElement.PointerPressedEvent, _hostPointerPressed, true);
        _chipHost.AddHandler(UIElement.PointerMovedEvent, _hostPointerMoved, true);
        _chipHost.AddHandler(UIElement.PointerReleasedEvent, _hostPointerReleased, true);
        _chipHost.AddHandler(UIElement.PointerCanceledEvent, _hostPointerCanceled, true);
        _chipHost.AddHandler(UIElement.PointerCaptureLostEvent, _hostPointerCaptureLost, true);
    }

    public void Attach(Button chip)
    {
        // Profile chips are discovered via the visual tree on pointer down.
        _ = chip;
    }

    public void DetachAll()
    {
        EndTracking();
    }

    public bool ShouldSuppressClick(Button? chip)
    {
        if (chip is null || !ReferenceEquals(chip, _suppressClickFor))
        {
            return false;
        }

        _suppressClickFor = null;
        return true;
    }

    private void OnHostPointerPressed(PointerRoutedEventArgs e)
    {
        if (_tracking || _isDisabled() || !e.GetCurrentPoint(_chipHost).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var chip = FindProfileChip(e.OriginalSource as DependencyObject);
        if (chip is null)
        {
            return;
        }

        var pos = e.GetCurrentPoint(_chipHost).Position;
        BeginTracking(chip, e.Pointer, pos);
        e.Handled = true;
    }

    private void OnHostPointerMoved(PointerRoutedEventArgs e)
    {
        if (!_tracking || _activePointer is null || e.Pointer != _activePointer)
        {
            return;
        }

        var pos = e.GetCurrentPoint(_chipHost).Position;
        _lastPointerPosition = pos;
        _hasPointerPosition = true;
        ProcessPointerPosition(pos);
        e.Handled = true;
    }

    private void BeginTracking(Button chip, Pointer pointer, Point positionInHost)
    {
        EndTracking();

        _tracking = true;
        _dragging = false;
        _activeChip = chip;
        _activePointer = pointer;
        _pressPosition = positionInHost;
        _lastPointerPosition = positionInHost;
        _hasPointerPosition = true;
        _fromIndex = IndexOf(chip);
        _insertBeforeIndex = -1;
        _suppressClickFor = null;

        // ButtonBase implicitly captures the pointer in its own PointerPressed handler. When we
        // call CapturePointer below it transfers capture to the chip host and synchronously fires
        // PointerCaptureLost on the chip; that event bubbles up to our chip-host handler. Without
        // this latch the spurious lost event would cancel tracking before any drag begins.
        _suppressNextCaptureLost = true;
        try
        {
            _chipHost.CapturePointer(pointer);
        }
        finally
        {
            _suppressNextCaptureLost = false;
        }

        CompositionTarget.Rendering += OnCompositionRendering;
    }

    private void OnHostPointerCaptureLost(PointerRoutedEventArgs e)
    {
        // Ignore capture-lost events bubbling up from descendants (notably Button stealing/losing
        // its own capture as we transfer pointer ownership to the chip host).
        if (!ReferenceEquals(e.OriginalSource, _chipHost))
        {
            return;
        }

        if (_suppressNextCaptureLost)
        {
            return;
        }

        TryFinishFromHost(e.Pointer);
    }

    private void OnCompositionRendering(object? sender, object e)
    {
        if (!_tracking)
        {
            return;
        }

        if (!IsLeftButtonDown())
        {
            FinishTracking();
            return;
        }

        if (!TryGetPointerPositionInHost(out var pos))
        {
            return;
        }

        ProcessPointerPosition(pos);
    }

    private void ProcessPointerPosition(Point pos)
    {
        if (!_tracking)
        {
            return;
        }

        if (!_dragging)
        {
            var dx = pos.X - _pressPosition.X;
            var dy = pos.Y - _pressPosition.Y;
            if (Math.Abs(dx) < DragThresholdPixels && Math.Abs(dy) < DragThresholdPixels)
            {
                return;
            }

            if (_activeChip is not null)
            {
                EnterDragMode(_activeChip);
            }
        }

        var insertBefore = HitTestInsertBeforeIndex(pos);
        if (insertBefore != _insertBeforeIndex)
        {
            _insertBeforeIndex = insertBefore;
            if (IsNoOpInsert(_fromIndex, insertBefore))
            {
                HideInsertionLine();
            }
            else
            {
                UpdateInsertionLine(insertBefore);
            }
        }
    }

    private void TryFinishFromHost(Pointer pointer)
    {
        if (_tracking && _activePointer is not null && pointer == _activePointer)
        {
            FinishTracking();
        }
    }

    private void EnterDragMode(Button chip)
    {
        _dragging = true;
        Canvas.SetZIndex(chip, 100);
        chip.RenderTransform = new ScaleTransform
        {
            ScaleX = DraggingScale,
            ScaleY = DraggingScale,
            CenterX = chip.ActualWidth > 0 ? chip.ActualWidth / 2 : 15,
            CenterY = chip.ActualHeight > 0 ? chip.ActualHeight / 2 : 15,
        };
        chip.Opacity = DraggingOpacity;
    }

    private void FinishTracking()
    {
        var wasDragging = _dragging;
        var from = _fromIndex;
        var insertBefore = _insertBeforeIndex >= 0 ? _insertBeforeIndex : from;
        var chip = _activeChip;

        EndTracking();

        if (chip is null || !wasDragging || from < 0)
        {
            return;
        }

        var count = _getProfileButtons().Count;
        insertBefore = Math.Clamp(insertBefore, 0, count);
        if (IsNoOpInsert(from, insertBefore))
        {
            return;
        }

        _suppressClickFor = chip;
        _onReorder(from, insertBefore);
    }

    private static bool IsNoOpInsert(int fromIndex, int insertBeforeIndex)
    {
        if (fromIndex < 0)
        {
            return true;
        }

        return fromIndex == insertBeforeIndex || fromIndex + 1 == insertBeforeIndex;
    }

    private void EndTracking()
    {
        CompositionTarget.Rendering -= OnCompositionRendering;

        if (_activePointer is not null)
        {
            try
            {
                _chipHost.ReleasePointerCapture(_activePointer);
            }
            catch
            {
                // ignore
            }
        }

        HideInsertionLine();

        if (_activeChip is not null)
        {
            ResetChipVisual(_activeChip);
        }

        _tracking = false;
        _dragging = false;
        _activeChip = null;
        _activePointer = null;
        _fromIndex = -1;
        _insertBeforeIndex = -1;
        _hasPointerPosition = false;
    }

    private void UpdateInsertionLine(int insertBeforeIndex)
    {
        if (!TryGetInsertionLineBounds(insertBeforeIndex, out var x, out var y, out var width))
        {
            HideInsertionLine();
            return;
        }

        _insertionLine.Margin = new Thickness(x, y, 0, 0);
        _insertionLine.Width = Math.Max(8, width);
        _insertionLine.Visibility = Visibility.Visible;
    }

    private void HideInsertionLine()
    {
        _insertionLine.Visibility = Visibility.Collapsed;
    }

    private bool TryGetInsertionLineBounds(int insertBeforeIndex, out double x, out double y, out double width)
    {
        x = y = width = 0;
        var buttons = _getProfileButtons();
        var count = buttons.Count;
        if (count == 0)
        {
            return false;
        }

        if (insertBeforeIndex <= 0 && TryGetBoundsInInsertionLineHost(buttons[0], out var first))
        {
            x = first.X;
            y = Math.Max(0, first.Y - 2);
            width = first.Width;
            return true;
        }

        if (insertBeforeIndex >= count && TryGetBoundsInInsertionLineHost(buttons[count - 1], out var last))
        {
            x = last.X;
            y = last.Y + last.Height;
            width = last.Width;
            return true;
        }

        if (insertBeforeIndex > 0
            && insertBeforeIndex < count
            && TryGetBoundsInInsertionLineHost(buttons[insertBeforeIndex - 1], out var prev)
            && TryGetBoundsInInsertionLineHost(buttons[insertBeforeIndex], out var next))
        {
            x = Math.Min(prev.X, next.X);
            y = (prev.Y + prev.Height + next.Y) / 2 - 1;
            width = Math.Max(prev.X + prev.Width, next.X + next.Width) - x;
            return true;
        }

        return false;
    }

    private int HitTestInsertBeforeIndex(Point pos)
    {
        var buttons = _getProfileButtons();
        var count = buttons.Count;
        if (count == 0)
        {
            return 0;
        }

        var best = count;
        var bestDist = double.MaxValue;

        void Consider(double gx, double gy, int slot)
        {
            var dx = pos.X - gx;
            var dy = pos.Y - gy;
            var dist = dx * dx + dy * dy;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = slot;
            }
        }

        if (TryGetBoundsInChipHost(buttons[0], out var first))
        {
            Consider(first.X + first.Width / 2, first.Y, 0);
        }

        for (var i = 1; i < count; i++)
        {
            if (TryGetBoundsInChipHost(buttons[i - 1], out var prev)
                && TryGetBoundsInChipHost(buttons[i], out var curr))
            {
                var gx = (prev.X + prev.Width / 2 + curr.X + curr.Width / 2) / 2;
                var gy = (prev.Y + prev.Height + curr.Y) / 2;
                Consider(gx, gy, i);
            }
        }

        if (TryGetBoundsInChipHost(buttons[count - 1], out var last))
        {
            Consider(last.X + last.Width / 2, last.Y + last.Height, count);
        }

        return best;
    }

    private bool TryGetPointerPositionInHost(out Point positionInHost)
    {
        if (_hasPointerPosition)
        {
            positionInHost = _lastPointerPosition;
            return true;
        }

        positionInHost = default;
        return false;
    }

    private bool TryGetBoundsInChipHost(Button chip, out Rect bounds) =>
        TryGetBounds(chip, _chipHost, out bounds);

    private bool TryGetBoundsInInsertionLineHost(Button chip, out Rect bounds) =>
        TryGetBounds(chip, _insertionLineHost, out bounds);

    private static bool TryGetBounds(Button chip, UIElement relativeTo, out Rect bounds)
    {
        bounds = default;
        if (chip.ActualWidth <= 0 || chip.ActualHeight <= 0)
        {
            return false;
        }

        try
        {
            var transform = chip.TransformToVisual(relativeTo);
            bounds = transform.TransformBounds(new Rect(0, 0, chip.ActualWidth, chip.ActualHeight));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Button? FindProfileChip(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button { Tag: ProfileRow } btn)
            {
                return btn;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static void ResetChipVisual(Button chip)
    {
        chip.Opacity = 1;
        chip.RenderTransform = null;
        Canvas.SetZIndex(chip, 0);
        chip.BorderThickness = new Thickness(0);
        chip.BorderBrush = null;
    }

    private static SolidColorBrush GetAccentBrush() =>
        Application.Current.Resources[AppTheme.WidgetAccent] as SolidColorBrush
        ?? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x4C, 0x8B, 0xF5));

    private int IndexOf(Button chip)
    {
        var buttons = _getProfileButtons();
        for (var i = 0; i < buttons.Count; i++)
        {
            if (ReferenceEquals(buttons[i], chip))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsLeftButtonDown() =>
        (NativeMethods.GetAsyncKeyState(VkLButton) & 0x8000) != 0;

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);
    }
}
