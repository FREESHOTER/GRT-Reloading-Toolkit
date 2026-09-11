using System.Reflection;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The plugin's version, read from the assembly rather than typed into each window title.
///
/// Eight titles used to carry a literal "(v0.1)" while the csproj said 0.1.0 and the newest tag
/// said v0.1.5, so a released build told the user a version four patches behind itself. The
/// number now comes from <c>Directory.Build.props</c> through the compiler, which is the only
/// arrangement that cannot drift.
/// </summary>
internal static class AppVersion
{
    /// <summary>"0.1.6" — the informational version with any "+buildmetadata" suffix trimmed.</summary>
    public static string Short { get; } = Read();

    /// <summary>A window caption with the version appended the way every tool window shows it.</summary>
    public static string Title(string name) => $"{name}  (v{Short})";

    private static string Read()
    {
        var asm = typeof(AppVersion).Assembly;
        string? v = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // SourceLink appends "+<commit sha>"; the assembly version is the fallback when the
        // attribute is absent (it never carries a suffix, but it does carry a 4th component).
        if (v is { Length: > 0 })
        {
            int plus = v.IndexOf('+');
            return plus < 0 ? v : v[..plus];
        }
        Version? av = asm.GetName().Version;
        return av is null ? "0" : $"{av.Major}.{av.Minor}.{av.Build}";
    }
}
