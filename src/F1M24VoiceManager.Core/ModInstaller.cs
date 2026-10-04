using System.Security.Cryptography;

namespace F1M24VoiceManager.Core;

public enum ModState
{
    Ue4ssMissing,
    NotInstalled,
    Outdated,
    UpToDate,
}

/// <summary>Installs the F1VoiceMapper UE4SS mod (DLL + Lua loader) and locates its mappings.txt.</summary>
public sealed class ModInstaller
{
    public const string ModFolderName = "F1VoiceMapper";

    /// <summary>Files shipped with the app (relative to the payload folder) and copied into the mod folder.</summary>
    public static readonly string[] PayloadFiles = ["F1VoiceMapper.dll", @"Scripts\main.lua", "enabled.txt"];

    private readonly GameInstall _game;
    private readonly string _payloadDirectory;
    private readonly Func<bool> _isGameRunning;

    public ModInstaller(GameInstall game, string payloadDirectory, Func<bool>? isGameRunning = null)
    {
        _game = game;
        _payloadDirectory = payloadDirectory;
        _isGameRunning = isGameRunning ?? GameInstall.IsGameRunning;
    }

    public string? ModDirectory => _game.ModsDirectory is { } mods ? Path.Combine(mods, ModFolderName) : null;

    public string? MappingsPath => ModDirectory is { } dir ? Path.Combine(dir, "mappings.txt") : null;

    public string? LogPath => ModDirectory is { } dir ? Path.Combine(dir, "F1VoiceMapper.log") : null;

    public ModState State
    {
        get
        {
            if (ModDirectory is not { } dir)
            {
                return ModState.Ue4ssMissing;
            }
            if (!File.Exists(Path.Combine(dir, "F1VoiceMapper.dll")))
            {
                return ModState.NotInstalled;
            }
            return PayloadFiles.All(f => SameContent(Path.Combine(_payloadDirectory, f), Path.Combine(dir, f)))
                ? ModState.UpToDate
                : ModState.Outdated;
        }
    }

    /// <summary>Copies the payload into the mod folder. Never touches mappings.txt.</summary>
    public void Install()
    {
        if (ModDirectory is not { } dir)
        {
            throw new InvalidOperationException(
                "UE4SS n'est pas installé dans le jeu. Installez UE4SS 3.x avant d'utiliser le gestionnaire de voix.");
        }
        foreach (string file in PayloadFiles)
        {
            if (!File.Exists(Path.Combine(_payloadDirectory, file)))
            {
                throw new FileNotFoundException("Fichier du mod manquant dans l'application : " + file);
            }
        }

        bool dllChanges = !SameContent(Path.Combine(_payloadDirectory, "F1VoiceMapper.dll"), Path.Combine(dir, "F1VoiceMapper.dll"));
        if (dllChanges && _isGameRunning())
        {
            throw new InvalidOperationException(
                "F1 Manager 2024 est ouvert : le mod de voix doit être installé ou mis à jour jeu fermé. " +
                "Fermez le jeu puis cliquez à nouveau sur Appliquer.");
        }

        foreach (string file in PayloadFiles)
        {
            string target = Path.Combine(dir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(_payloadDirectory, file), target, overwrite: true);
        }
    }

    private static bool SameContent(string a, string b)
    {
        if (!File.Exists(a) || !File.Exists(b))
        {
            return false;
        }
        if (new FileInfo(a).Length != new FileInfo(b).Length)
        {
            return false;
        }
        using FileStream fa = File.OpenRead(a);
        using FileStream fb = new(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(fa).AsSpan().SequenceEqual(SHA256.HashData(fb));
    }
}
