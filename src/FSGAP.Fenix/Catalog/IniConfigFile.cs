namespace FSGAP.Fenix.Catalog;

/// <summary>
/// Minimal, tolerant reader for MSFS <c>.cfg</c> files (<c>livery.cfg</c>, <c>aircraft.cfg</c>). It handles
/// <c>[SECTION]</c> headers, <c>key = "value"</c> pairs and <c>//</c> comments, and is case-insensitive for
/// sections and keys. Malformed lines are skipped, never fatal. Ported from the audited applications.
/// </summary>
internal sealed class IniConfigFile
{
    private readonly Dictionary<string, Dictionary<string, string>> _sections = new(StringComparer.OrdinalIgnoreCase);

    private IniConfigFile()
    {
    }

    /// <summary>A value of a section; <c>""</c> is the implicit section before any header.</summary>
    public string? Get(string section, string key) =>
        _sections.TryGetValue(section, out var values) && values.TryGetValue(key, out var value) ? value : null;

    /// <summary>Section names starting with <paramref name="prefix"/> (e.g. <c>FLTSIM</c> → FLTSIM.0, FLTSIM.1), in file order.</summary>
    public IEnumerable<string> SectionsStartingWith(string prefix) =>
        _sections.Keys.Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static IniConfigFile Parse(IEnumerable<string> lines)
    {
        var file = new IniConfigFile();
        var section = file.Section(string.Empty);
        foreach (var raw in lines)
        {
            var comment = raw.IndexOf("//", StringComparison.Ordinal);
            var line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                section = file.Section(line[1..^1].Trim());
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }

            section[key] = value;
        }

        return file;
    }

    private Dictionary<string, string> Section(string name)
    {
        if (!_sections.TryGetValue(name, out var values))
        {
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _sections[name] = values;
        }

        return values;
    }
}
