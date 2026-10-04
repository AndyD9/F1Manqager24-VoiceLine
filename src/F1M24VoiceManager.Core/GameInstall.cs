using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace F1M24VoiceManager.Core;

public enum Ue4ssLayout
{
    Missing,
    /// <summary>UE4SS 3.x: Win64\ue4ss\UE4SS.dll and Win64\ue4ss\Mods.</summary>
    Subfolder,
    /// <summary>Legacy: Win64\UE4SS.dll and Win64\Mods.</summary>
    Root,
}

public sealed partial class GameInstall
{
    private const string RelativeWin64 = @"steamapps\common\F1 Manager 2024\F1Manager24\Binaries\Win64";
    public const string ProcessName = "F1Manager24";

    public string Win64Directory { get; }

    public GameInstall(string win64Directory) => Win64Directory = Path.GetFullPath(win64Directory);

    public string ExecutablePath => Path.Combine(Win64Directory, "F1Manager24.exe");

    public Ue4ssLayout Ue4ss =>
        File.Exists(Path.Combine(Win64Directory, "ue4ss", "UE4SS.dll")) ? Ue4ssLayout.Subfolder
        : File.Exists(Path.Combine(Win64Directory, "UE4SS.dll")) ? Ue4ssLayout.Root
        : Ue4ssLayout.Missing;

    public string? ModsDirectory => Ue4ss switch
    {
        Ue4ssLayout.Subfolder => Path.Combine(Win64Directory, "ue4ss", "Mods"),
        Ue4ssLayout.Root => Path.Combine(Win64Directory, "Mods"),
        _ => null,
    };

    public static bool IsGameRunning()
    {
        Process[] processes = Process.GetProcessesByName(ProcessName);
        foreach (Process p in processes)
        {
            p.Dispose();
        }
        return processes.Length > 0;
    }

    public static GameInstall? FromDirectory(string directory)
    {
        foreach (string candidate in new[] { directory, Path.Combine(directory, RelativeWin64),
                     Path.Combine(directory, @"F1Manager24\Binaries\Win64") })
        {
            // The root F1Manager24.exe is only a launcher; require the real Binaries\Win64 one.
            if (File.Exists(Path.Combine(candidate, "F1Manager24.exe"))
                && Path.GetFileName(Path.GetFullPath(candidate)).Equals("Win64", StringComparison.OrdinalIgnoreCase))
            {
                return new GameInstall(candidate);
            }
        }
        return null;
    }

    public static GameInstall? Locate()
    {
        foreach (string steam in SteamRoots())
        {
            if (FromDirectory(Path.Combine(steam, RelativeWin64)) is { } direct)
            {
                return direct;
            }
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                continue;
            }
            foreach (Match m in LibraryPathRegex().Matches(File.ReadAllText(vdf)))
            {
                string library = m.Groups[1].Value.Replace(@"\\", @"\");
                if (FromDirectory(Path.Combine(library, RelativeWin64)) is { } found)
                {
                    return found;
                }
            }
        }
        return null;
    }

    private static IEnumerable<string> SteamRoots()
    {
        var roots = new List<string>();
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string user)
        {
            roots.Add(user.Replace('/', '\\'));
        }
        if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) is string machine)
        {
            roots.Add(machine);
        }
        roots.Add(@"C:\Program Files (x86)\Steam");
        roots.Add(@"C:\Program Files\Steam");
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();
}
