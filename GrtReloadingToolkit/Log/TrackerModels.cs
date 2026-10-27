using System.Globalization;

namespace GrtReloadingToolkit.Log;

/// <summary>A load followed over time: one caliber, one bullet, one powder, one charge. There can be several (one per caliber, or two
/// charges of the same one); each has its own history of outings. Bullet and powder are free text so nothing here needs an inventory.</summary>
public sealed class TrackedLoad
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Caliber { get; set; } = "";
    public string Bullet { get; set; } = "";
    public string Powder { get; set; } = "";
    public double ChargeGr { get; set; }
    public string Notes { get; set; } = "";
    public bool Archived { get; set; }
    /// <summary>Whose load this is: a shooter of the Reloading Toolkit's database (0 = not said yet: the person on screen).</summary>
    public long ShooterId { get; set; }

    public override string ToString() => $"{Name}  ·  {Caliber}";
}

/// <summary>One outing with a tracked load: what was shot, the conditions, and what was different from the time before.
/// Velocities are in m/s and stored as the shots themselves, because a mean without its shots says nothing about the spread.</summary>
public sealed class Outing
{
    public long Id { get; set; }
    public long LoadId { get; set; }
    public string Date { get; set; } = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public double? TempC { get; set; }
    public double? PressureHpa { get; set; }
    public double? HumidityPct { get; set; }
    /// <summary>Every shot's velocity in m/s, comma-separated.</summary>
    public string ShotsMs { get; set; } = "";
    public double? GroupMoa { get; set; }
    public double? DistanceM { get; set; }
    public double? WindMps { get; set; }
    public double? WindDeg { get; set; }
    public string Firearm { get; set; } = "";
    public string PowderLot { get; set; } = "";
    public string PrimerLot { get; set; } = "";
    public double? SeatingMm { get; set; }
    public int? BrassFirings { get; set; }
    /// <summary>Rounds the barrel had fired before this outing, when the shooter counts them.</summary>
    public int? BarrelRounds { get; set; }
    public string Notes { get; set; } = "";

    public List<double> Velocities() => ParseShots(ShotsMs);

    /// <summary>A shot of a rifle or a pistol is between these: a "5" or a "9000" in a list of velocities is a slip (a round count, a comma
    /// read as a separator in "800,5,801,2") and would wreck the mean and the SD of the string.</summary>
    public static bool IsPlausibleShotMps(double mps) => mps is >= 50 and <= 2500;

    /// <summary>Shots separated by spaces, semicolons or new lines (what a chronograph's export pastes as). A comma is a decimal separator
    /// when one or two digits follow it ("800,5": an Italian keyboard), a separator between shots otherwise ("800,801").</summary>
    public static List<double> ParseShots(string? text)
    {
        var list = new List<double>();
        foreach (string token in (text ?? "").Split(new[] { ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = System.Text.RegularExpressions.Regex.IsMatch(token, @"^\d+,\d{1,2}$") ? new[] { token.Replace(',', '.') } : token.Split(',');
            foreach (string part in parts)
                if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 && !double.IsNaN(v))
                    list.Add(v);
        }
        return list;
    }

    public static string FormatShots(IEnumerable<double> velocities)
        => string.Join(",", velocities.Select(v => v.ToString("0.##", CultureInfo.InvariantCulture)));
}
