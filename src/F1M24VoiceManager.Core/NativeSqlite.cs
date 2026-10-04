using System.Runtime.InteropServices;

namespace F1M24VoiceManager.Core;

/// <summary>Minimal read-only wrapper over Windows' built-in winsqlite3.dll (no NuGet dependency).</summary>
internal sealed class NativeSqlite : IDisposable
{
    private const int SqliteOk = 0;
    private const int SqliteRow = 100;
    private const int SqliteDone = 101;
    private const int SqliteNull = 5;
    private const int OpenReadOnly = 1;

    private IntPtr _db;

    public NativeSqlite(string path)
    {
        int rc = sqlite3_open_v2(path, out _db, OpenReadOnly, IntPtr.Zero);
        if (rc != SqliteOk)
        {
            sqlite3_close(_db);
            throw new InvalidDataException($"Impossible d'ouvrir la base de la sauvegarde (code SQLite {rc}).");
        }
    }

    public sealed class Row
    {
        private readonly IntPtr _stmt;

        internal Row(IntPtr stmt) => _stmt = stmt;

        public int Int(int column) => sqlite3_column_int(_stmt, column);

        public int? NullableInt(int column) =>
            sqlite3_column_type(_stmt, column) == SqliteNull ? null : sqlite3_column_int(_stmt, column);

        public string Text(int column)
        {
            IntPtr p = sqlite3_column_text(_stmt, column);
            return p == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(p) ?? string.Empty;
        }
    }

    public List<T> Query<T>(string sql, Func<Row, T> map)
    {
        if (sqlite3_prepare_v2(_db, sql, -1, out IntPtr stmt, IntPtr.Zero) != SqliteOk)
        {
            throw new InvalidDataException("Table attendue absente de la sauvegarde : " + Error());
        }
        try
        {
            var results = new List<T>();
            var row = new Row(stmt);
            int rc;
            while ((rc = sqlite3_step(stmt)) == SqliteRow)
            {
                results.Add(map(row));
            }
            if (rc != SqliteDone)
            {
                throw new InvalidDataException("Lecture de la sauvegarde interrompue : " + Error());
            }
            return results;
        }
        finally
        {
            sqlite3_finalize(stmt);
        }
    }

    public bool TableExists(string name) =>
        Query($"SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = '{name.Replace("'", "''")}'", _ => true).Count > 0;

    public void Dispose()
    {
        if (_db != IntPtr.Zero)
        {
            sqlite3_close(_db);
            _db = IntPtr.Zero;
        }
    }

    private string Error()
    {
        IntPtr p = sqlite3_errmsg(_db);
        return p == IntPtr.Zero ? "erreur SQLite inconnue" : Marshal.PtrToStringUTF8(p) ?? "erreur SQLite inconnue";
    }

    private const string Lib = "winsqlite3.dll";

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int nByte, out IntPtr stmt, IntPtr tail);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr stmt);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr stmt);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_int(IntPtr stmt, int column);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_type(IntPtr stmt, int column);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr stmt, int column);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr db);
}
