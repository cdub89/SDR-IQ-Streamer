using System.Text;

namespace SDRIQStreamer.CWSkimmer;

/// <summary>
/// Writes a CW Skimmer Afreet-format INI file from a <see cref="CwSkimmerIniModel"/>.
/// Only the sections owned by this app are written; CW Skimmer manages the rest
/// ([Windows], [BandMap], [Validation], etc.) itself on first run.
/// </summary>
public sealed class CwSkimmerIniWriter
{
    private static readonly string[] s_ownedSectionOrder =
    [
        "Audio",
        "Radio",
        "Telnet",
    ];

    /// <summary>
    /// Updates app-owned INI sections while preserving all other sections that
    /// CW Skimmer manages (for example [Windows], [BandMap], and dialog state).
    /// </summary>
    public void Write(CwSkimmerIniModel model, string path)
    {
        var ownedSections = BuildOwnedSections(model);
        var existingSections = File.Exists(path)
            ? ParseSections(File.ReadAllLines(path))
            : [];

        var mergedSections = new List<IniSection>(existingSections.Count + s_ownedSectionOrder.Length);
        var seenOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in existingSections)
        {
            if (ownedSections.TryGetValue(section.Name, out var replacement))
            {
                mergedSections.Add(new IniSection(section.Name, replacement));
                seenOwned.Add(section.Name);
            }
            else
            {
                mergedSections.Add(section);
            }
        }

        foreach (var sectionName in s_ownedSectionOrder)
        {
            if (seenOwned.Contains(sectionName)) continue;
            if (ownedSections.TryGetValue(sectionName, out var lines))
                mergedSections.Add(new IniSection(sectionName, lines));
        }

        var sb = new StringBuilder();
        foreach (var section in mergedSections)
        {
            sb.Append('[').Append(section.Name).AppendLine("]");
            foreach (var line in section.Lines)
                sb.AppendLine(line);
            sb.AppendLine();
        }

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(path, sb.ToString());
    }

    private static Dictionary<string, List<string>> BuildOwnedSections(CwSkimmerIniModel model)
    {
        var cfg = model.Config;

        return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Audio"] =
            [
                $"WdmSignalDev={model.WdmSignalDevIndex}",
                $"WdmAudioDev={model.WdmAudioDevIndex}",
                "MmeSignalDev=0",
                "MmeAudioDev=0",
                "UseWdm=1",
                "ShiftQ=0",
                "SwapIQ=0",
            ],
            ["Radio"] =
            [
                "SdrType=2",
                $"Pitch={cfg.CwPitch}",
                "EstimateIQBalance=1",
                "CorrectIQBalance=1",
            ],
            ["Telnet"] =
            [
                $"Port={cfg.TelnetPort}",
                $"PasswordRequired={(cfg.TelnetPasswordRequired ? 1 : 0)}",
                $"Password={cfg.TelnetPassword}",
                "CqOnly=0",
                "AllowAnn=1",
                "AnnUserOnly=0",
                "AnnUser=",
                "TelnetSrvEnabled=1",
                "UdpSourceName=CW Skimmer",
                "UdpAddress=127.0.0.1",
                "UdpPort=13064",
                "UdpEnabled=0",
            ],
        };
    }

    private static List<IniSection> ParseSections(string[] lines)
    {
        var sections = new List<IniSection>();
        string? currentName = null;
        var currentLines = new List<string>();

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (TryParseSectionHeader(line, out var sectionName))
            {
                if (!string.IsNullOrWhiteSpace(currentName))
                    sections.Add(new IniSection(currentName, [.. currentLines]));

                currentName = sectionName;
                currentLines.Clear();
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentName))
                continue;

            if (line.Length == 0)
                continue;

            currentLines.Add(line);
        }

        if (!string.IsNullOrWhiteSpace(currentName))
            sections.Add(new IniSection(currentName, [.. currentLines]));

        return sections;
    }

    private static bool TryParseSectionHeader(string line, out string sectionName)
    {
        sectionName = string.Empty;
        if (line.Length < 3 || line[0] != '[' || line[^1] != ']')
            return false;

        sectionName = line[1..^1].Trim();
        return sectionName.Length > 0;
    }

    private sealed record IniSection(string Name, IReadOnlyList<string> Lines);
}
