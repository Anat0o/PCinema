using System.Text.RegularExpressions;
using System.IO;
namespace PCinema;

public static class NameParser
{
    public static ParsedName Parse(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path), folder = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
        var seasonFolder = Regex.Match(folder, @"^(?:season|сезон|s)[ ._-]*(\d{1,2})$", RegexOptions.IgnoreCase);
        string parent = seasonFolder.Success ? Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))) ?? folder : folder;
        var m = Regex.Match(name, @"(?i)(?:s(?<s>\d{1,2})[ ._-]*e(?<e>\d{1,3})|(?<s>\d{1,2})x(?<e>\d{1,3})|сезон[ ._-]*(?<s>\d{1,2})[ ._-]*серия[ ._-]*(?<e>\d{1,3}))");
        int? s = null, e = null; string title = parent;
        if (m.Success) { s = int.Parse(m.Groups["s"].Value); e = int.Parse(m.Groups["e"].Value); if (m.Index > 0) title = name[..m.Index]; if (Regex.IsMatch(name[(m.Index + m.Length)..], @"^(?:[ ._-]*e\d|[ ._-]*-\s*\d)", RegexOptions.IgnoreCase)) { s = null; e = null; } }
        else { m = Regex.Match(name, @"(?i)(?:серия|episode|^e)[ ._-]*(?<e>\d{1,3})"); if (m.Success && seasonFolder.Success) { s = int.Parse(seasonFolder.Groups[1].Value); e = int.Parse(m.Groups["e"].Value); } }
        var year = Regex.Match(title, @"\b(19\d{2}|20\d{2})\b"); int? y = year.Success ? int.Parse(year.Value) : null;
        title = Regex.Replace(title, @"\b(19\d{2}|20\d{2})\b", ""); title = Regex.Replace(title, @"[._]+", " "); title = Regex.Replace(title, @"[\[\]()]", ""); title = Regex.Replace(title, @"\s+", " ").Trim(' ', '-');
        return new(string.IsNullOrWhiteSpace(title) ? parent : title, y, s, e);
    }
    public static string Normalize(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
}
