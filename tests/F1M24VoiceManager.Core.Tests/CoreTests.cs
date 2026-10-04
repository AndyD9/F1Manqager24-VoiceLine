using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using F1M24VoiceManager.Core;

namespace F1M24VoiceManager.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("f1m24vm-test-").FullName;

    public string File(string relative) => System.IO.Path.Combine(Path, relative);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public class MappingFileTests
{
    [Fact]
    public void Parse_HandlesPrefixesCommentsAndInvalidLines()
    {
        var map = MappingFile.Parse("""
            # header
            D:17=10 # Perez -> Verstappen
            E:682 = 51 ; engineer
            552=130
            garbage
            E:5
            D:0=4
            d:1=10
            """);

        Assert.Equal(4, map.Count);
        Assert.Equal(new VoiceMapping(VoiceRole.Driver, 17, 10), map[17]);
        Assert.Equal(new VoiceMapping(VoiceRole.Engineer, 682, 51), map[682]);
        Assert.Equal(new VoiceMapping(VoiceRole.Driver, 552, 130), map[552]);
        Assert.Equal(new VoiceMapping(VoiceRole.Driver, 1, 10), map[1]);
    }

    [Fact]
    public void FormatThenParse_RoundTrips()
    {
        var input = new[]
        {
            new VoiceMapping(VoiceRole.Engineer, 320, 51),
            new VoiceMapping(VoiceRole.Driver, 1, 10),
        };
        string text = MappingFile.Format(input, id => id == 1 ? "Lewis Hamilton" : null);

        Assert.Contains("D:1=10 # Lewis Hamilton -> 10", text);
        Assert.True(text.IndexOf("D:1=10", StringComparison.Ordinal) < text.IndexOf("E:320=51", StringComparison.Ordinal));
        Assert.Equal(input.OrderBy(m => m.SourceStaffId), MappingFile.Parse(text).Values.OrderBy(m => m.SourceStaffId));
    }

    [Fact]
    public void Save_WritesAtomicallyWithoutLeftoverTempFile()
    {
        using var dir = new TempDir();
        string path = dir.File(@"mod\mappings.txt");
        MappingFile.Save(path, [new VoiceMapping(VoiceRole.Driver, 1, 10)]);
        MappingFile.Save(path, [new VoiceMapping(VoiceRole.Driver, 2, 12)]);

        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal([2], MappingFile.Load(path).Keys);
    }
}

public class SaveReaderTests
{
    [Theory]
    [InlineData("[STRING_LITERAL:Value=|Câmara|]", "Câmara")]
    [InlineData("[StaffName_Surname_Hamilton]", "Hamilton")]
    [InlineData("[StaffName_Forename_Male_Lewis1]", "Lewis")]
    [InlineData("Plain", "Plain")]
    public void DecodeName_HandlesGameEncodings(string raw, string expected) =>
        Assert.Equal(expected, SaveReader.DecodeName(raw));

    [Fact]
    public void Read_SyntheticSave_ReturnsStaffTeamsAndEngineerAssignments()
    {
        using var dir = new TempDir();
        string save = SyntheticSave.Create(dir.File("save1.sav"));

        CareerData career = SaveReader.Read(save);

        Assert.Equal(1, career.PlayerTeamId);
        Assert.Equal(4, career.Staff.Count); // placeholder excluded

        StaffMember hamilton = career.Staff.Single(s => s.StaffId == 1);
        Assert.Equal("Lewis Hamilton", hamilton.Name);
        Assert.Equal(VoiceRole.Driver, hamilton.Role);
        Assert.Equal("Ferrari", hamilton.TeamName);
        Assert.Equal(1, hamilton.PosInTeam);
        Assert.True(hamilton.IsRaceSeat);
        Assert.True(hamilton.HasRecordedVoice);

        StaffMember generated = career.Staff.Single(s => s.StaffId == 699);
        Assert.Equal("Lorenzo Gallo", generated.Name);
        Assert.True(generated.IsGenerated);
        Assert.False(generated.HasRecordedVoice);
        Assert.Null(generated.TeamId);

        StaffMember dudley = career.Staff.Single(s => s.StaffId == 320);
        Assert.Equal(VoiceRole.Engineer, dudley.Role);
        Assert.Equal(320, career.EngineerOfDriver[1]);
    }

    [Fact]
    public void Read_NotASave_ThrowsFriendlyError()
    {
        using var dir = new TempDir();
        string path = dir.File("bad.sav");
        File.WriteAllBytes(path, new byte[1024]);

        var ex = Assert.Throws<InvalidDataException>(() => SaveReader.Read(path));
        Assert.Contains("sauvegarde", ex.Message);
    }

    [Fact]
    public void Read_RealSaves_WhenAvailable()
    {
        var saves = SaveReader.FindSaves();
        if (saves.Count == 0)
        {
            return; // not on a machine with the game
        }
        CareerData career = SaveReader.Read(saves[0].Path);
        Assert.NotEmpty(career.Staff);
        Assert.Contains(career.Staff, s => s.Role == VoiceRole.Engineer);
        Assert.NotNull(career.PlayerTeamId);
    }
}

public class BackupManagerTests
{
    [Fact]
    public void CreateListRestore_RoundTripsIncludingAbsentFile()
    {
        using var dir = new TempDir();
        var backups = new BackupManager(dir.File("backup"));
        string mappings = dir.File(@"mod\mappings.txt");

        Backup empty = backups.Create(mappings); // no file yet
        Directory.CreateDirectory(Path.GetDirectoryName(mappings)!);
        File.WriteAllText(mappings, "D:1=10\n");
        Backup first = backups.Create(mappings);
        File.WriteAllText(mappings, "D:1=12\n");

        var list = backups.List();
        Assert.Equal(2, list.Count);
        Assert.Equal(first.Directory, list[0].Directory);
        Assert.False(list[1].HadMappings);

        backups.Restore(first, mappings);
        Assert.Equal("D:1=10\n", File.ReadAllText(mappings));

        backups.Restore(empty, mappings);
        Assert.False(File.Exists(mappings));
    }
}

public class ModInstallerTests
{
    private static (TempDir dir, GameInstall game, string payload) FakeGame(bool withUe4ss = true)
    {
        var dir = new TempDir();
        string win64 = dir.File(@"F1Manager24\Binaries\Win64");
        Directory.CreateDirectory(win64);
        File.WriteAllText(Path.Combine(win64, "F1Manager24.exe"), "exe");
        if (withUe4ss)
        {
            Directory.CreateDirectory(Path.Combine(win64, "ue4ss", "Mods"));
            File.WriteAllText(Path.Combine(win64, "ue4ss", "UE4SS.dll"), "ue4ss");
        }
        string payload = dir.File("payload");
        Directory.CreateDirectory(Path.Combine(payload, "Scripts"));
        File.WriteAllText(Path.Combine(payload, "F1VoiceMapper.dll"), "dll v1");
        File.WriteAllText(Path.Combine(payload, "Scripts", "main.lua"), "-- lua");
        File.WriteAllText(Path.Combine(payload, "enabled.txt"), "1");
        return (dir, new GameInstall(win64), payload);
    }

    [Fact]
    public void Install_UsesUe4ssSubfolderLayoutAndKeepsMappings()
    {
        var (dir, game, payload) = FakeGame();
        using (dir)
        {
            bool running = false;
            var installer = new ModInstaller(game, payload, () => running);
            Assert.Equal(Ue4ssLayout.Subfolder, game.Ue4ss);
            Assert.Equal(ModState.NotInstalled, installer.State);
            Assert.EndsWith(@"ue4ss\Mods\F1VoiceMapper\mappings.txt", installer.MappingsPath);

            installer.Install();
            File.WriteAllText(installer.MappingsPath!, "D:1=10\n");
            Assert.Equal(ModState.UpToDate, installer.State);

            File.WriteAllText(Path.Combine(payload, "F1VoiceMapper.dll"), "dll v2");
            Assert.Equal(ModState.Outdated, installer.State);

            running = true;
            var ex = Assert.Throws<InvalidOperationException>(installer.Install);
            Assert.Contains("Fermez le jeu", ex.Message);
            Assert.Equal(ModState.Outdated, installer.State);

            running = false;
            installer.Install();
            Assert.Equal(ModState.UpToDate, installer.State);
            Assert.Equal("D:1=10\n", File.ReadAllText(installer.MappingsPath!));
        }
    }

    [Fact]
    public void Install_WithoutUe4ss_ExplainsWhatIsMissing()
    {
        var (dir, game, payload) = FakeGame(withUe4ss: false);
        using (dir)
        {
            var installer = new ModInstaller(game, payload, () => false);
            Assert.Equal(ModState.Ue4ssMissing, installer.State);
            var ex = Assert.Throws<InvalidOperationException>(installer.Install);
            Assert.Contains("UE4SS", ex.Message);
        }
    }

    [Fact]
    public void FromDirectory_AcceptsGameRootOrWin64()
    {
        var (dir, game, _) = FakeGame();
        using (dir)
        {
            Assert.Equal(game.Win64Directory, GameInstall.FromDirectory(game.Win64Directory)!.Win64Directory);
            Assert.Equal(game.Win64Directory, GameInstall.FromDirectory(dir.Path)!.Win64Directory);
            Assert.Null(GameInstall.FromDirectory(dir.File("payload")));
        }
    }
}

/// <summary>Builds a minimal .sav: junk + marker + sizes + zlib(SQLite db), like the game's GVAS layout.</summary>
internal static class SyntheticSave
{
    private static readonly byte[] Marker =
        [0, 5, 0, 0, 0, (byte)'N', (byte)'o', (byte)'n', (byte)'e', 0, 5, 0, 0, 0, (byte)'N', (byte)'o', (byte)'n', (byte)'e', 0];

    public static string Create(string savePath)
    {
        string db = savePath + ".db";
        Exec(db, """
            CREATE TABLE Staff_BasicData (StaffID INTEGER, FirstName TEXT, LastName TEXT, IsGeneratedStaff INTEGER);
            CREATE TABLE Staff_GameData (StaffID INTEGER, StaffType INTEGER);
            CREATE TABLE Staff_Contracts (StaffID INTEGER, ContractType INTEGER, TeamID INTEGER, PosInTeam INTEGER);
            CREATE TABLE Teams (TeamID INTEGER, TeamName TEXT, Formula INTEGER);
            CREATE TABLE Player (TeamID INTEGER);
            CREATE TABLE Staff_RaceEngineerDriverAssignments (RaceEngineerID INTEGER, DriverID INTEGER, IsCurrentAssignment INTEGER);
            INSERT INTO Staff_BasicData VALUES
              (1, '[StaffName_Forename_Male_Lewis]', '[StaffName_Surname_Hamilton]', 0),
              (2, '[StaffName_Forename_Male_Charles]', '[StaffName_Surname_Leclerc]', 0),
              (320, '[StaffName_Forename_Male_Marcus]', '[StaffName_Surname_Dudley]', 0),
              (699, '[STRING_LITERAL:Value=|Lorenzo|]', '[STRING_LITERAL:Value=|Gallo|]', 1),
              (900, 'Placeholder', 'Placeholder', 0),
              (901, 'Team', 'Principal', 0);
            INSERT INTO Staff_GameData VALUES (1, 0), (2, 0), (320, 2), (699, 0), (900, 0), (901, 1);
            INSERT INTO Staff_Contracts VALUES (1, 0, 1, 1), (2, 0, 1, 2), (320, 0, 1, 1), (2, 3, 2, 1);
            INSERT INTO Teams VALUES (1, 'Ferrari', 1), (2, 'McLaren', 1);
            INSERT INTO Player VALUES (1);
            INSERT INTO Staff_RaceEngineerDriverAssignments VALUES (320, 1, 1), (50, 1, 0);
            """);

        byte[] raw = File.ReadAllBytes(db);
        File.Delete(db);
        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                z.Write(raw);
            }
            compressed = ms.ToArray();
        }

        using var output = new MemoryStream();
        output.Write(new byte[64]); // GVAS header stand-in
        output.Write(Marker);
        output.Write(new byte[4]);
        Span<byte> sizes = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(sizes, compressed.Length);
        BinaryPrimitives.WriteInt32LittleEndian(sizes[4..], raw.Length);
        output.Write(sizes);
        output.Write(compressed);
        output.Write(new byte[32]);
        File.WriteAllBytes(savePath, output.ToArray());
        return savePath;
    }

    private static void Exec(string path, string sql)
    {
        Assert.Equal(0, sqlite3_open_v2(path, out IntPtr db, 0x6 /* READWRITE | CREATE */, IntPtr.Zero));
        try
        {
            Assert.Equal(0, sqlite3_exec(db, sql, IntPtr.Zero, IntPtr.Zero, out _));
        }
        finally
        {
            sqlite3_close(db);
        }
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, out IntPtr errmsg);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);
}
