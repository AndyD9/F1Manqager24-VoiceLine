using System.Buffers.Binary;
using System.IO.Compression;

namespace F1M24VoiceManager.Core;

/// <summary>
/// Reads staff data from an F1 Manager 2024 career save (read-only).
/// The .sav embeds a zlib-compressed SQLite database after a fixed marker.
/// </summary>
public static class SaveReader
{
    private static readonly byte[] DatabaseMarker =
        [0, 5, 0, 0, 0, (byte)'N', (byte)'o', (byte)'n', (byte)'e', 0, 5, 0, 0, 0, (byte)'N', (byte)'o', (byte)'n', (byte)'e', 0];

    private const string StaffQuery = """
        SELECT b.StaffID, b.FirstName, b.LastName, b.IsGeneratedStaff, g.StaffType,
               c.TeamID, COALESCE(t.TeamName, ''), t.Formula, c.PosInTeam
        FROM Staff_BasicData b
        INNER JOIN Staff_GameData g ON g.StaffID = b.StaffID
        LEFT JOIN Staff_Contracts c ON c.StaffID = b.StaffID AND c.ContractType = 0
        LEFT JOIN Teams t ON t.TeamID = c.TeamID
        WHERE g.StaffType IN (0, 2)
        """;

    public static IReadOnlyList<CareerSave> FindSaves(string? folder = null)
    {
        folder ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "F1Manager24", "Saved", "SaveGames");
        if (!Directory.Exists(folder))
        {
            return [];
        }
        return Directory.EnumerateFiles(folder, "*.sav")
            .Where(p => Path.GetFileName(p).StartsWith("save", StringComparison.OrdinalIgnoreCase)
                     || Path.GetFileName(p).StartsWith("autosave", StringComparison.OrdinalIgnoreCase))
            .Select(p => new CareerSave(p, File.GetLastWriteTime(p)))
            .OrderByDescending(s => s.LastWriteTime)
            .ToList();
    }

    public static CareerData Read(string savePath)
    {
        string dbPath = ExtractDatabase(savePath);
        try
        {
            using var db = new NativeSqlite(dbPath);
            return Read(db);
        }
        finally
        {
            TryDelete(dbPath);
        }
    }

    internal static CareerData Read(NativeSqlite db)
    {
        var staff = db.Query(StaffQuery, r => new StaffMember(
                StaffId: r.Int(0),
                FirstName: DecodeName(r.Text(1)),
                LastName: DecodeName(r.Text(2)),
                Role: r.Int(4) == 0 ? VoiceRole.Driver : VoiceRole.Engineer,
                IsGenerated: r.Int(3) != 0,
                TeamId: r.NullableInt(5),
                TeamName: r.Text(6),
                Formula: r.NullableInt(7),
                PosInTeam: r.NullableInt(8)))
            .Where(s => !(s.FirstName.Equals("Placeholder", StringComparison.OrdinalIgnoreCase)
                       && s.LastName.Equals("Placeholder", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        int? playerTeam = db.TableExists("Player")
            ? db.Query("SELECT TeamID FROM Player LIMIT 1", r => r.NullableInt(0)).FirstOrDefault()
            : null;

        var engineerOfDriver = new Dictionary<int, int>();
        if (db.TableExists("Staff_RaceEngineerDriverAssignments"))
        {
            foreach (var (engineer, driver) in db.Query(
                         "SELECT RaceEngineerID, DriverID FROM Staff_RaceEngineerDriverAssignments WHERE IsCurrentAssignment = 1",
                         r => (r.Int(0), r.Int(1))))
            {
                engineerOfDriver[driver] = engineer;
            }
        }

        return new CareerData(playerTeam, staff, engineerOfDriver);
    }

    /// <summary>Decompresses the embedded database to a temporary file and returns its path.</summary>
    public static string ExtractDatabase(string savePath)
    {
        byte[] data = File.ReadAllBytes(savePath);
        int offset = FindDatabaseOffset(data);
        if (offset < 0)
        {
            throw new InvalidDataException("Ce fichier n'est pas une sauvegarde de carrière F1 Manager 2024 reconnue.");
        }
        int compressed = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset, 4));
        int uncompressed = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 4, 4));

        string path = Path.Combine(Path.GetTempPath(), $"F1M24VoiceManager-{Guid.NewGuid():N}.db");
        using (var input = new MemoryStream(data, offset + 16, compressed, writable: false))
        using (var zlib = new ZLibStream(input, CompressionMode.Decompress))
        using (var output = File.Create(path))
        {
            zlib.CopyTo(output);
            if (output.Length < uncompressed)
            {
                output.Dispose();
                TryDelete(path);
                throw new InvalidDataException("La base de la sauvegarde est tronquée.");
            }
            output.SetLength(uncompressed);
        }
        return path;
    }

    internal static int FindDatabaseOffset(ReadOnlySpan<byte> data)
    {
        int start = 0;
        while (true)
        {
            int i = data[start..].IndexOf(DatabaseMarker);
            if (i < 0)
            {
                return -1;
            }
            i += start;
            int offset = i + DatabaseMarker.Length + 4;
            if (offset + 16 <= data.Length)
            {
                int compressed = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
                int uncompressed = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4));
                if (compressed > 0 && uncompressed > 0 && compressed <= data.Length - (offset + 16))
                {
                    return offset;
                }
            }
            start = i + 1;
        }
    }

    /// <summary>Decodes "[STRING_LITERAL:Value=|Name|]" and "[StaffName_Surname_Name1]" forms.</summary>
    public static string DecodeName(string value)
    {
        const string literal = "[STRING_LITERAL:Value=|";
        if (value.StartsWith(literal, StringComparison.Ordinal))
        {
            int end = value.IndexOf('|', literal.Length);
            if (end > literal.Length)
            {
                return value[literal.Length..end];
            }
        }
        if (value.StartsWith('[') && value.EndsWith(']'))
        {
            value = value[1..^1];
            int cut = value.LastIndexOf('_');
            if (cut >= 0 && cut + 1 < value.Length)
            {
                value = value[(cut + 1)..];
            }
        }
        if (value.Length > 1 && char.IsDigit(value[^1]))
        {
            return value[..^1];
        }
        return value;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
