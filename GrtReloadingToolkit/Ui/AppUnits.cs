using GrtReloadingToolkit.Measuring;

namespace GrtReloadingToolkit.Ui;

/// <summary>The choices of unit kept beside the program's other small settings, the same for every window.</summary>
internal sealed class UiStateUnitStore : IUnitStore
{
    public string? Get(string key) => UiState.Get(key, "") is { Length: > 0 } v ? v : null;
    public void Set(string key, string value) => UiState.Set(key, value);
    public void Remove(string key) => UiState.Set(key, "");
    public IEnumerable<string> Keys(string prefix) => UiState.KeysStartingWith(prefix);
}

internal static class AppUnits
{
    public static UnitPrefs Prefs { get; } = new(new UiStateUnitStore());

    /// <summary>The unit of a measure of the cartridges as it is shown now, or null when that measure has no choice (a force, a volume).</summary>
    public static (UnitKind Kind, string Code)? ForMeasure(string key)
    {
        var kind = UnitSystem.KindOfFieldUnit(Cartridges.MeasureFields.UnitOf(key));
        return kind is { } k ? (k, Prefs.For("measure." + key, k)) : null;
    }

    /// <summary>A stored value of a measure in the unit it is shown in, without the unit; null for "no unit" returns the value as it is.</summary>
    public static string ShowMeasure(string key, double storedValue) =>
        ForMeasure(key) is { } u ? UnitSystem.Show(storedValue, u.Code) : storedValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>What was typed for a measure, in the unit it is shown in, as the stored value.</summary>
    public static double MeasureToStored(string key, double typed) => ForMeasure(key) is { } u ? UnitSystem.ToBase(typed, u.Code) : typed;

    /// <summary>The caption of a measure with the unit it is shown in.</summary>
    public static string MeasureHeader(string key) =>
        Lang.T(Cartridges.MeasureFields.LabelOf(key)) + (ForMeasure(key) is { } u ? " (" + UnitSystem.Label(u.Code) + ")"
            : Cartridges.MeasureFields.UnitOf(key) is { Length: > 0 } fixedUnit ? " (" + fixedUnit + ")" : "");

    /// <summary>
    /// Lets the shooter change the unit of one measure from the title of its column: a right click lists the units of its kind, the choice is
    /// remembered for that field, and <paramref name="refresh"/> redraws the grid.
    /// </summary>
    public static void AttachColumnMenu(DataGridView grid, Action refresh)
    {
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            string key = grid.Columns[e.ColumnIndex].Name;
            if (ForMeasure(key) is not { } cur) return;
            var menu = new ContextMenuStrip();
            foreach (var u in UnitSystem.Of(cur.Kind))
            {
                var item = new ToolStripMenuItem(UnitSystem.Label(u.Code)) { Checked = u.Code == cur.Code };
                string code = u.Code;
                item.Click += (_, _) => { Prefs.SetField("measure." + key, cur.Kind, code); refresh(); };
                menu.Items.Add(item);
            }
            if (Prefs.HasOwnChoice("measure." + key, cur.Kind))
            {
                menu.Items.Add(new ToolStripSeparator());
                var same = new ToolStripMenuItem(Lang.T("Like the others of its kind"));
                same.Click += (_, _) => { Prefs.ClearField("measure." + key); refresh(); };
                menu.Items.Add(same);
            }
            menu.Show(grid, grid.PointToClient(Cursor.Position));
        };
    }

    /// <summary>
    /// The same right click on the title of one column of a table that is not the cartridges': <paramref name="field"/> names the field the choice is kept for,
    /// <paramref name="kind"/> the kind of unit it offers.
    /// </summary>
    public static void AttachFieldMenu(DataGridView grid, string column, string field, UnitKind kind, Action refresh)
    {
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || grid.Columns[e.ColumnIndex].Name != column) return;
            string current = ProgramUnits.Current.ForField(field).CodeOf(kind);
            var menu = new ContextMenuStrip();
            foreach (var u in UnitSystem.Of(kind))
            {
                var item = new ToolStripMenuItem(UnitSystem.Label(u.Code)) { Checked = u.Code == current };
                string code = u.Code;
                item.Click += (_, _) => { Prefs.SetField(field, kind, code); refresh(); };
                menu.Items.Add(item);
            }
            if (Prefs.HasOwnChoice(field, kind))
            {
                menu.Items.Add(new ToolStripSeparator());
                var same = new ToolStripMenuItem(Lang.T("Like the others of its kind"));
                same.Click += (_, _) => { Prefs.ClearField(field); refresh(); };
                menu.Items.Add(same);
            }
            menu.Show(grid, grid.PointToClient(Cursor.Position));
        };
    }
}
