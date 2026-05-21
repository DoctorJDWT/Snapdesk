using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace LayoutProfiles.WinUI.Helpers;

internal sealed class GumballGridLayout
{
    private readonly ScrollViewer _scrollViewer;
    private readonly Grid _chipHost;
    private readonly int _chipUnit;

    private int _appliedChipsPerRow = -1;

    public GumballGridLayout(ScrollViewer scrollViewer, Grid chipHost, int chipUnit)
    {
        _scrollViewer = scrollViewer;
        _chipHost = chipHost;
        _chipUnit = chipUnit;
    }

    public int AppliedChipsPerRow => _appliedChipsPerRow;

    public void Reset() => _appliedChipsPerRow = -1;

    public GumballLayoutResult Apply(
        IReadOnlyList<UIElement> chips,
        int clientWidth,
        int clientHeight,
        int? forcedChipsPerRow = null)
    {
        var count = chips.Count;
        var chipsPerRow = forcedChipsPerRow.HasValue
            ? Math.Clamp(forcedChipsPerRow.Value, 1, Math.Max(1, count))
            : ComputeChipsPerRow(clientWidth, _chipUnit);
        var columnCount = count > 0 ? Math.Min(chipsPerRow, count) : 1;
        var rows = (count + chipsPerRow - 1) / chipsPerRow;
        var hostWidth = columnCount * _chipUnit;
        var hostHeight = rows * _chipUnit;

        if (IsCurrent(chips, chipsPerRow, hostWidth))
        {
            return new GumballLayoutResult(
                Changed: false,
                Count: count,
                Rows: rows,
                ChipsPerRow: chipsPerRow,
                HostWidth: hostWidth,
                ClientWidth: clientWidth);
        }

        _chipHost.Children.Clear();
        _chipHost.RowDefinitions.Clear();
        _chipHost.ColumnDefinitions.Clear();

        for (var r = 0; r < rows; r++)
        {
            _chipHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var c = 0; c < columnCount; c++)
        {
            _chipHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var i = 0; i < count; i++)
        {
            if (chips[i] is not FrameworkElement chip)
            {
                continue;
            }

            Grid.SetRow(chip, i / chipsPerRow);
            Grid.SetColumn(chip, ChipColumnIndex(i, count, chipsPerRow));
            _chipHost.Children.Add(chip);
        }

        _appliedChipsPerRow = chipsPerRow;
        _chipHost.Width = hostWidth;
        _chipHost.MaxWidth = hostWidth;
        _chipHost.MinWidth = _chipUnit;
        _chipHost.MinHeight = _chipUnit;

        _scrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _scrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _scrollViewer.HorizontalScrollMode = ScrollMode.Disabled;
        _scrollViewer.VerticalScrollMode = ScrollMode.Disabled;
        _scrollViewer.ZoomMode = ZoomMode.Disabled;

        _scrollViewer.HorizontalContentAlignment =
            hostWidth <= clientWidth ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        _chipHost.HorizontalAlignment =
            hostWidth <= clientWidth ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        _scrollViewer.VerticalContentAlignment =
            hostHeight <= clientHeight ? VerticalAlignment.Center : VerticalAlignment.Top;
        _chipHost.VerticalAlignment =
            hostHeight <= clientHeight ? VerticalAlignment.Center : VerticalAlignment.Top;
        _chipHost.InvalidateMeasure();

        return new GumballLayoutResult(
            Changed: true,
            Count: count,
            Rows: rows,
            ChipsPerRow: chipsPerRow,
            HostWidth: hostWidth,
            ClientWidth: clientWidth);
    }

    private bool IsCurrent(IReadOnlyList<UIElement> chips, int chipsPerRow, int hostWidth)
    {
        var count = chips.Count;
        return chipsPerRow == _appliedChipsPerRow
            && _chipHost.Children.Count == count
            && PlacementMatches(chips, chipsPerRow)
            && Math.Abs(_chipHost.Width - hostWidth) < 0.5
            && Math.Abs(_chipHost.MaxWidth - hostWidth) < 0.5
            && Math.Abs(_chipHost.MinWidth - _chipUnit) < 0.5
            && Math.Abs(_chipHost.MinHeight - _chipUnit) < 0.5;
    }

    private bool PlacementMatches(IReadOnlyList<UIElement> chips, int chipsPerRow)
    {
        var count = chips.Count;
        for (var i = 0; i < count; i++)
        {
            if (chips[i] is not FrameworkElement chip)
            {
                return false;
            }

            if (Grid.GetRow(chip) != i / chipsPerRow
                || Grid.GetColumn(chip) != ChipColumnIndex(i, count, chipsPerRow))
            {
                return false;
            }

            if (!WindowChromeHelper.IsDescendantOf(chip, _chipHost))
            {
                return false;
            }
        }

        return true;
    }

    public static int ComputeChipsPerRow(int clientWidth, int chipUnit)
    {
        if (clientWidth <= 0)
        {
            return 1;
        }

        if (clientWidth < 2 * chipUnit)
        {
            return 1;
        }

        return (clientWidth - chipUnit) / chipUnit;
    }

    private static int ChipsInRow(int count, int chipsPerRow, int row) =>
        Math.Min(chipsPerRow, count - row * chipsPerRow);

    private static int ChipColumnIndex(int index, int count, int chipsPerRow)
    {
        var row = index / chipsPerRow;
        var colInRow = index % chipsPerRow;
        var chipsInRow = ChipsInRow(count, chipsPerRow, row);
        if (chipsInRow >= chipsPerRow || (row == 0 && count <= chipsPerRow))
        {
            return colInRow;
        }

        return colInRow + (chipsPerRow - chipsInRow) / 2;
    }
}

internal sealed record GumballLayoutResult(
    bool Changed,
    int Count,
    int Rows,
    int ChipsPerRow,
    int HostWidth,
    int ClientWidth);
