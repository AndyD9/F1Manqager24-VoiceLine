using System.Text;

namespace F1M24VoiceManager.Core;

/// <summary>Reads and writes F1VoiceMapper's mappings.txt (D:source=donor / E:source=donor).</summary>
public static class MappingFile
{
    public static IReadOnlyDictionary<int, VoiceMapping> Parse(string text)
    {
        var result = new Dictionary<int, VoiceMapping>();
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine;
            int comment = line.IndexOfAny(['#', ';']);
            if (comment >= 0)
            {
                line = line[..comment];
            }
            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var role = VoiceRole.Driver;
            if (line.StartsWith("D:", StringComparison.OrdinalIgnoreCase))
            {
                line = line[2..];
            }
            else if (line.StartsWith("E:", StringComparison.OrdinalIgnoreCase))
            {
                role = VoiceRole.Engineer;
                line = line[2..];
            }

            string[] parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out int source) && int.TryParse(parts[1], out int donor)
                && source > 0 && donor > 0)
            {
                result[source] = new VoiceMapping(role, source, donor);
            }
        }
        return result;
    }

    public static IReadOnlyDictionary<int, VoiceMapping> Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path)) : new Dictionary<int, VoiceMapping>();

    public static string Format(IEnumerable<VoiceMapping> mappings, Func<int, string?>? nameOf = null)
    {
        var sb = new StringBuilder();
        sb.Append("# Généré par F1M24 Voice Manager — une ligne par réattribution.\r\n");
        sb.Append("# D:pilote_source=pilote_donneur  E:ingénieur_source=ingénieur_donneur\r\n");
        foreach (var m in mappings.OrderBy(m => m.Role).ThenBy(m => m.SourceStaffId))
        {
            sb.Append(m.Role == VoiceRole.Driver ? "D:" : "E:").Append(m.SourceStaffId).Append('=').Append(m.DonorStaffId);
            string? source = nameOf?.Invoke(m.SourceStaffId);
            string? donor = nameOf?.Invoke(m.DonorStaffId);
            if (source is not null || donor is not null)
            {
                sb.Append(" # ").Append(source ?? m.SourceStaffId.ToString()).Append(" -> ").Append(donor ?? m.DonorStaffId.ToString());
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Writes atomically (temp file + replace) so the in-game watcher never reads a partial file.</summary>
    public static void Save(string path, IEnumerable<VoiceMapping> mappings, Func<int, string?>? nameOf = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, Format(mappings, nameOf), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                // The DLL may be reading the file for a hot reload; retry briefly.
                Thread.Sleep(100);
            }
        }
    }
}
