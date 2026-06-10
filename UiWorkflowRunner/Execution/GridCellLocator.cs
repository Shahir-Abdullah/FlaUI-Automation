using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using UiWorkflowRunner.Workflow;

namespace UiWorkflowRunner.Execution;

/// <summary>
/// Resolves <c>{ grid, row, column }</c> locators into a concrete
/// <see cref="AutomationElement"/> representing the targeted cell. Supports:
///
///   row    : integer index | "first" | "last" |
///            { columnEquals: { ColumnHeader: "value" } }
///
///   column : integer index | column-header text
/// </summary>
internal static class GridCellLocator
{
    public static AutomationElement? Find(AutomationElement root, TargetSpec target)
    {
        var gridElement = root.FindFirstDescendant(cf => cf.ByAutomationId(target.Grid!));
        if (gridElement is null)
        {
            return null;
        }

        var grid = gridElement.AsGrid();

        var rows = SafeGetRows(grid);
        if (rows.Length == 0)
        {
            return null;
        }

        var rowIndex = ResolveRowIndex(grid, rows, target.Row);
        if (rowIndex < 0 || rowIndex >= rows.Length)
        {
            return null;
        }

        var row = rows[rowIndex];
        var cells = row.Cells;
        if (cells is null || cells.Length == 0)
        {
            return null;
        }

        var columnIndex = ResolveColumnIndex(grid, target.Column);
        if (columnIndex < 0 || columnIndex >= cells.Length)
        {
            return null;
        }

        return cells[columnIndex];
    }

    // ------------------------------------------------------------------ rows

    private static int ResolveRowIndex(Grid grid, GridRow[] rows, object? rowSpec)
    {
        switch (rowSpec)
        {
            case null:
                throw new InvalidOperationException("Grid locator is missing the 'row' field.");

            case int i:
                return i;

            case long l:
                return checked((int)l);

            case string s when int.TryParse(s, out var n):
                return n;

            case string s when string.Equals(s, "first", StringComparison.OrdinalIgnoreCase):
                return 0;

            case string s when string.Equals(s, "last", StringComparison.OrdinalIgnoreCase):
                return rows.Length - 1;

            case IDictionary dict:
                return ResolveRowByColumnEquals(grid, rows, dict);

            default:
                throw new InvalidOperationException(
                    $"Unsupported 'row' value '{rowSpec}'. Use an integer, 'first'/'last', or 'columnEquals: {{ Column: value }}'.");
        }
    }

    private static int ResolveRowByColumnEquals(Grid grid, GridRow[] rows, IDictionary dict)
    {
        if (!TryGetMap(dict, "columnEquals", out var inner))
        {
            throw new InvalidOperationException(
                "row locator must contain 'columnEquals: { ColumnHeader: \"value\" }'.");
        }

        if (inner is null || inner.Count == 0)
        {
            throw new InvalidOperationException("columnEquals must contain at least one Column: value pair.");
        }

        foreach (DictionaryEntry kv in inner)
        {
            var columnHeader = kv.Key?.ToString() ?? string.Empty;
            var expected     = kv.Value?.ToString() ?? string.Empty;

            var columnIndex = FindColumnIndex(grid, columnHeader);
            if (columnIndex < 0)
            {
                throw new InvalidOperationException($"Grid has no column named '{columnHeader}'.");
            }

            for (int r = 0; r < rows.Length; r++)
            {
                var cells = rows[r].Cells;
                if (cells is null || columnIndex >= cells.Length) continue;
                var value = SafeCellValue(cells[columnIndex]);
                if (string.Equals(value, expected, StringComparison.Ordinal))
                {
                    return r;
                }
            }
        }
        return -1;
    }

    private static bool TryGetMap(IDictionary dict, string key, out IDictionary? value)
    {
        foreach (DictionaryEntry kv in dict)
        {
            if (string.Equals(kv.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
            {
                value = kv.Value as IDictionary;
                return value is not null;
            }
        }
        value = null;
        return false;
    }

    // ------------------------------------------------------------------ columns

    private static int ResolveColumnIndex(Grid grid, object? columnSpec)
    {
        switch (columnSpec)
        {
            case null:
                throw new InvalidOperationException("Grid locator is missing the 'column' field.");

            case int i:
                return i;

            case long l:
                return checked((int)l);

            case string s when int.TryParse(s, out var n):
                return n;

            case string s:
                return FindColumnIndex(grid, s);

            default:
                throw new InvalidOperationException(
                    $"Unsupported 'column' value '{columnSpec}'. Use an integer or the column header text.");
        }
    }

    private static int FindColumnIndex(Grid grid, string headerText)
    {
        GridHeader? header;
        try
        {
            header = grid.Header;
        }
        catch
        {
            header = null;
        }

        if (header is not null)
        {
            var columns = header.Columns;
            for (int i = 0; i < columns.Length; i++)
            {
                if (string.Equals(SafeHeaderText(columns[i]), headerText, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        // Fallback: try ColumnHeaders array (some grids expose only this).
        var fallback = grid.ColumnHeaders;
        if (fallback is not null)
        {
            for (int i = 0; i < fallback.Length; i++)
            {
                if (string.Equals(fallback[i].Name, headerText, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    // ------------------------------------------------------------------ helpers

    private static GridRow[] SafeGetRows(Grid grid)
    {
        try { return grid.Rows ?? Array.Empty<GridRow>(); }
        catch { return Array.Empty<GridRow>(); }
    }

    private static string SafeCellValue(GridCell cell)
    {
        try { return cell.Value ?? string.Empty; }
        catch
        {
            // Some cells expose their value via Name rather than the Value
            // property (e.g. WPF DataGrid template columns).
            try { return cell.Name ?? string.Empty; }
            catch { return string.Empty; }
        }
    }

    private static string SafeHeaderText(GridHeaderItem item)
    {
        try { return item.Text ?? string.Empty; }
        catch { return item.Name ?? string.Empty; }
    }
}
