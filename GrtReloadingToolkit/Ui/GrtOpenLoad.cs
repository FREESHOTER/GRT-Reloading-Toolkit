using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Ui;

/// <summary>The shots of one chronographed charge of the load open in GRT.</summary>
internal sealed record ChargeSet(string Label, double? ChargeGr, IReadOnlyList<double> Shots);

/// <summary>What the tracker reads from the load open in GRT: which load it is (caliber, bullet, powder, charge) and every chronographed
/// charge in it, so an outing can be filled in from it. Read from the freshest member of the load's family, like every other tool, because the
/// active tab can be an older file than the one holding the string that was just imported.</summary>
internal sealed record OpenLoad(string Path, string Name, string Caliber, string Bullet, string Powder, string Firearm, double? RecipeChargeGr, IReadOnlyList<ChargeSet> Sets)
{
    public static OpenLoad Read(string tabFile)
    {
        string path = GrtLoadDoc.EffectiveReadPath(tabFile);
        var doc = GrtLoadDoc.Load(path);
        var u = GrtUnits.Current;
        var sets = new List<ChargeSet>();
        foreach (var meas in doc.Measurements())
            foreach (var ch in meas.Charges)
            {
                var shots = ch.Shots.Select(s => s.VelocityMps).Where(v => v > 0).ToList();
                if (shots.Count == 0) continue;
                var st = StringStats.From(shots);
                string label = ch.ChargeGrains is { } g
                    ? $"{u.Charge(g)} ({meas.Title}) — {st.N} colpi, {u.Velocity(st.Mean)}"
                    : $"{ch.Name} ({meas.Title}) — {st.N} colpi, {u.Velocity(st.Mean)}";
                sets.Add(new ChargeSet(label, ch.ChargeGrains, shots));
            }
        string name = System.IO.Path.GetFileNameWithoutExtension(tabFile);
        return new OpenLoad(path, name, doc.CaliberName, doc.ProjectileName, doc.PropellantName, doc.GunName, doc.PropellantChargeGr, sets);
    }
}
