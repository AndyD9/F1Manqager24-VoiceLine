namespace F1M24VoiceManager.Core;

public sealed record Backup(string Directory, DateTime CreatedAt, bool HadMappings)
{
    public string Label => CreatedAt.ToString("dd/MM/yyyy HH:mm:ss") + (HadMappings ? "" : " (aucune réattribution)");
}

/// <summary>Timestamped copies of mappings.txt taken before every change (backup\yyyy-MM-dd_HHmmss\).</summary>
public sealed class BackupManager
{
    private const string Format = "yyyy-MM-dd_HHmmss";
    private const string EmptyMarker = "NO_MAPPINGS";

    public string Root { get; }

    public BackupManager(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "F1M24VoiceManager", "backup");
    }

    public Backup Create(string mappingsPath)
    {
        DateTime now = DateTime.Now;
        string dir = Path.Combine(Root, now.ToString(Format));
        for (int i = 1; Directory.Exists(dir); i++)
        {
            dir = Path.Combine(Root, $"{now.ToString(Format)}-{i}");
        }
        Directory.CreateDirectory(dir);

        bool had = File.Exists(mappingsPath);
        if (had)
        {
            File.Copy(mappingsPath, Path.Combine(dir, "mappings.txt"));
        }
        else
        {
            File.WriteAllText(Path.Combine(dir, EmptyMarker), "");
        }
        return new Backup(dir, now, had);
    }

    public IReadOnlyList<Backup> List()
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }
        var backups = new List<Backup>();
        foreach (string dir in Directory.EnumerateDirectories(Root))
        {
            string name = Path.GetFileName(dir);
            string stamp = name.Length >= Format.Length ? name[..Format.Length] : name;
            if (DateTime.TryParseExact(stamp, Format, null, System.Globalization.DateTimeStyles.None, out DateTime at))
            {
                backups.Add(new Backup(dir, at, File.Exists(Path.Combine(dir, "mappings.txt"))));
            }
        }
        return backups.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Directory).ToList();
    }

    /// <summary>Puts mappings.txt back as it was when the backup was taken.</summary>
    public void Restore(Backup backup, string mappingsPath)
    {
        string source = Path.Combine(backup.Directory, "mappings.txt");
        if (File.Exists(source))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(mappingsPath)!);
            string temp = mappingsPath + ".tmp";
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, mappingsPath, overwrite: true);
        }
        else if (File.Exists(mappingsPath))
        {
            File.Delete(mappingsPath);
        }
    }
}
