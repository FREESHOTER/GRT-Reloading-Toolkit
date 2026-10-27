using System.Globalization;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The question every import asks before it writes into the load on top in GRT: which load is it, where does it sit and whose is it.
/// The shooter keeps other people's loads too, so writing into the wrong tab is an easy mistake to make and a slow one to undo.
/// </summary>
internal static class ImportTarget
{
    /// <summary>True when the shooter agrees to write into <paramref name="path"/>.</summary>
    public static bool Confirm(IWin32Window owner, string path, Db? db)
    {
        var people = db?.Shooters(includeArchived: true);
        string? who = LoadOwner.FromPath(path, people?.Select(s => s.Name));
        string text = string.Format(CultureInfo.InvariantCulture,
            Lang.T("Import the strings into this load?") + "\n\n" + Lang.T("Load: {0}") + "\n" + Lang.T("Folder: {1}") + "\n" + Lang.T("Shooter: {2}"),
            Path.GetFileNameWithoutExtension(path), Path.GetDirectoryName(path) ?? "", who ?? Lang.T("(not recognised)"));
        if (db?.CurrentShooterId is { } sel && who != null
            && people!.FirstOrDefault(s => s.Id == sel) is { } picked
            && !string.Equals(picked.Name, who, StringComparison.OrdinalIgnoreCase))
            text += "\n\n" + string.Format(CultureInfo.InvariantCulture, Lang.T("Careful: the launcher is set to {0}."), picked.Name);
        return MessageBox.Show(owner, text, Lang.T("Import"), MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) == DialogResult.Yes;
    }
}
