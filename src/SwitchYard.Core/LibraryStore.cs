using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SwitchYard.Core;

/// <summary>
/// SQLite persistence for the library and emulator profiles.
/// The schema is identical to the Milestone 1 Python build, so existing libraries open unchanged.
/// </summary>
public sealed class LibraryStore : IDisposable
{
    public const int SchemaVersion = 1;

    private const string Schema = """
        CREATE TABLE IF NOT EXISTS meta (
            key   TEXT PRIMARY KEY,
            value TEXT
        );
        CREATE TABLE IF NOT EXISTS library_folders (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            path            TEXT NOT NULL,
            path_key        TEXT NOT NULL UNIQUE,
            generation      TEXT NOT NULL DEFAULT 'unclassified',
            added_at        TEXT NOT NULL,
            last_scanned_at TEXT
        );
        CREATE TABLE IF NOT EXISTS editions (
            id               TEXT PRIMARY KEY,
            title            TEXT NOT NULL,
            generation       TEXT NOT NULL DEFAULT 'unclassified'
                             CHECK (generation IN ('switch1', 'switch2', 'unclassified')),
            content_kind     TEXT NOT NULL DEFAULT 'base',
            console_title_id TEXT,
            igdb_id          INTEGER,
            created_at       TEXT NOT NULL,
            updated_at       TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS edition_files (
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            edition_id   TEXT NOT NULL REFERENCES editions(id) ON DELETE CASCADE,
            folder_id    INTEGER REFERENCES library_folders(id) ON DELETE SET NULL,
            path         TEXT NOT NULL,
            path_key     TEXT NOT NULL UNIQUE,
            role         TEXT NOT NULL DEFAULT 'primary',
            size_bytes   INTEGER,
            added_at     TEXT NOT NULL,
            last_seen_at TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_edition_files_edition ON edition_files(edition_id);
        CREATE TABLE IF NOT EXISTS emulator_profiles (
            id          TEXT PRIMARY KEY,
            name        TEXT NOT NULL,
            emulator    TEXT NOT NULL DEFAULT 'custom',
            executable  TEXT NOT NULL,
            working_dir TEXT NOT NULL DEFAULT '',
            generation  TEXT NOT NULL DEFAULT 'switch1',
            args_json   TEXT NOT NULL DEFAULT '[]',
            created_at  TEXT NOT NULL,
            updated_at  TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS settings (
            key   TEXT PRIMARY KEY,
            value TEXT
        );
        """;

    private readonly SqliteConnection _conn;

    public string DatabaseFile { get; }

    public LibraryStore(string databaseFile)
    {
        DatabaseFile = Path.GetFullPath(databaseFile);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFile)!);
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseFile,
            Pooling = false, // release the file on Dispose
        }.ToString());
        _conn.Open();
        Exec("PRAGMA foreign_keys = ON");
        Exec(Schema);
        Exec("INSERT OR IGNORE INTO meta(key, value) VALUES ('schema_version', $v)", ("$v", SchemaVersion.ToString()));
    }

    public void Dispose() => _conn.Dispose();

    public static string UtcNow() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ helpers
    private SqliteCommand Cmd(string sql, params (string Name, object? Value)[] args)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private int Exec(string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(sql, args);
        return cmd.ExecuteNonQuery();
    }

    private static string? Str(SqliteDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetString(r.GetOrdinal(col));

    private static long? Long(SqliteDataReader r, string col) =>
        r.IsDBNull(r.GetOrdinal(col)) ? null : r.GetInt64(r.GetOrdinal(col));

    // ------------------------------------------------------------------ folders
    public long UpsertFolder(string path, string generation)
    {
        var ts = UtcNow();
        var key = AppPaths.PathKey(path);
        Exec("""
            INSERT INTO library_folders(path, path_key, generation, added_at, last_scanned_at)
            VALUES ($p, $k, $g, $t, $t)
            ON CONFLICT(path_key) DO UPDATE SET
                generation = excluded.generation,
                last_scanned_at = excluded.last_scanned_at
            """, ("$p", Path.GetFullPath(path)), ("$k", key), ("$g", generation), ("$t", ts));
        using var cmd = Cmd("SELECT id FROM library_folders WHERE path_key = $k", ("$k", key));
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Generation last assigned to this folder, or null if it was never imported.</summary>
    public string? FolderGeneration(string path)
    {
        using var cmd = Cmd("SELECT generation FROM library_folders WHERE path_key = $k", ("$k", AppPaths.PathKey(path)));
        return cmd.ExecuteScalar() as string;
    }

    // ----------------------------------------------------------------- editions
    private Dictionary<string, List<EditionFile>> FilesByEdition(string? editionId = null)
    {
        var result = new Dictionary<string, List<EditionFile>>();
        using var cmd = editionId is null
            ? Cmd("SELECT id, edition_id, path, role FROM edition_files ORDER BY id")
            : Cmd("SELECT id, edition_id, path, role FROM edition_files WHERE edition_id = $e ORDER BY id", ("$e", editionId));
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var f = new EditionFile(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3));
            if (!result.TryGetValue(f.EditionId, out var list))
                result[f.EditionId] = list = [];
            list.Add(f);
        }
        return result;
    }

    private static Edition ReadEdition(SqliteDataReader r, Dictionary<string, List<EditionFile>> files)
    {
        var id = r.GetString(r.GetOrdinal("id"));
        return new Edition
        {
            Id = id,
            Title = r.GetString(r.GetOrdinal("title")),
            Generation = r.GetString(r.GetOrdinal("generation")),
            ContentKind = r.GetString(r.GetOrdinal("content_kind")),
            ConsoleTitleId = Str(r, "console_title_id"),
            IgdbId = Long(r, "igdb_id"),
            Files = files.TryGetValue(id, out var list) ? list : [],
        };
    }

    public List<Edition> ListEditions()
    {
        var files = FilesByEdition();
        // Sort in C# so ordering is case-insensitive for all text, like the Python build's NOCASE.
        using var cmd = Cmd("SELECT * FROM editions");
        using var r = cmd.ExecuteReader();
        var list = new List<Edition>();
        while (r.Read())
            list.Add(ReadEdition(r, files));
        return list.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
    }

    public Edition? GetEdition(string editionId)
    {
        using var cmd = Cmd("SELECT * FROM editions WHERE id = $id", ("$id", editionId));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadEdition(r, FilesByEdition(editionId)) : null;
    }

    public string? FindEditionByPath(string path)
    {
        using var cmd = Cmd("SELECT edition_id FROM edition_files WHERE path_key = $k", ("$k", AppPaths.PathKey(path)));
        return cmd.ExecuteScalar() as string;
    }

    public string AddEdition(string title, string generation, string path, string contentKind = ContentKinds.Base,
        string? consoleTitleId = null, long? folderId = null, long? sizeBytes = null)
    {
        if (!Generations.All.Contains(generation))
            throw new ArgumentException($"Unknown generation: {generation}", nameof(generation));
        var id = Guid.NewGuid().ToString();
        var ts = UtcNow();
        using var tx = _conn.BeginTransaction();
        using (var cmd = Cmd("""
            INSERT INTO editions(id, title, generation, content_kind, console_title_id, igdb_id, created_at, updated_at)
            VALUES ($id, $title, $gen, $kind, $tid, NULL, $ts, $ts)
            """, ("$id", id), ("$title", title), ("$gen", generation), ("$kind", contentKind), ("$tid", consoleTitleId), ("$ts", ts)))
        {
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        using (var cmd = Cmd("""
            INSERT INTO edition_files(edition_id, folder_id, path, path_key, role, size_bytes, added_at, last_seen_at)
            VALUES ($id, $folder, $path, $key, 'primary', $size, $ts, $ts)
            """, ("$id", id), ("$folder", folderId), ("$path", Path.GetFullPath(path)), ("$key", AppPaths.PathKey(path)),
                 ("$size", sizeBytes), ("$ts", ts)))
        {
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return id;
    }

    public void SetGeneration(string editionId, string generation)
    {
        if (!Generations.All.Contains(generation))
            throw new ArgumentException($"Unknown generation: {generation}", nameof(generation));
        Exec("UPDATE editions SET generation = $g, updated_at = $t WHERE id = $id",
            ("$g", generation), ("$t", UtcNow()), ("$id", editionId));
    }

    public void RelinkFile(long fileId, string newPath)
    {
        var key = AppPaths.PathKey(newPath);
        using (var cmd = Cmd("SELECT id FROM edition_files WHERE path_key = $k AND id != $id", ("$k", key), ("$id", fileId)))
        {
            if (cmd.ExecuteScalar() is not null)
                throw new InvalidOperationException("That file already belongs to another library entry.");
        }
        Exec("UPDATE edition_files SET path = $p, path_key = $k, last_seen_at = $t WHERE id = $id",
            ("$p", Path.GetFullPath(newPath)), ("$k", key), ("$t", UtcNow()), ("$id", fileId));
    }

    public void MarkSeen(IEnumerable<string> paths)
    {
        var ts = UtcNow();
        using var tx = _conn.BeginTransaction();
        foreach (var p in paths)
        {
            using var cmd = Cmd("UPDATE edition_files SET last_seen_at = $t WHERE path_key = $k", ("$t", ts), ("$k", AppPaths.PathKey(p)));
            cmd.Transaction = tx;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>Library files whose path is inside <paramref name="folder"/>.</summary>
    public List<EditionFile> FilesUnder(string folder)
    {
        var prefix = AppPaths.PathKey(folder) + Path.DirectorySeparatorChar;
        var result = new List<EditionFile>();
        using var cmd = Cmd("SELECT id, edition_id, path, path_key, role FROM edition_files");
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (r.GetString(3).StartsWith(prefix, StringComparison.Ordinal))
                result.Add(new EditionFile(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(4)));
        return result;
    }

    // ----------------------------------------------------------------- settings
    public string? GetSetting(string key)
    {
        using var cmd = Cmd("SELECT value FROM settings WHERE key = $k", ("$k", key));
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string? value) =>
        Exec("INSERT INTO settings(key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            ("$k", key), ("$v", value));

    // ----------------------------------------------------------------- profiles
    private static EmulatorProfile ReadProfile(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Name = r.GetString(r.GetOrdinal("name")),
        Emulator = r.GetString(r.GetOrdinal("emulator")),
        Executable = r.GetString(r.GetOrdinal("executable")),
        WorkingDir = Str(r, "working_dir") ?? "",
        Generation = r.GetString(r.GetOrdinal("generation")),
        Args = JsonSerializer.Deserialize(Str(r, "args_json") ?? "[]", CoreJsonContext.Default.ListString) ?? [],
    };

    public List<EmulatorProfile> ListProfiles()
    {
        using var cmd = Cmd("SELECT * FROM emulator_profiles ORDER BY created_at, name");
        using var r = cmd.ExecuteReader();
        var list = new List<EmulatorProfile>();
        while (r.Read())
            list.Add(ReadProfile(r));
        return list;
    }

    public EmulatorProfile? GetProfile(string id)
    {
        using var cmd = Cmd("SELECT * FROM emulator_profiles WHERE id = $id", ("$id", id));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadProfile(r) : null;
    }

    public void SaveProfile(EmulatorProfile p)
    {
        if (!Generations.ProfileOptions.Contains(p.Generation))
            throw new ArgumentException($"Unknown profile generation: {p.Generation}");
        var ts = UtcNow();
        Exec("""
            INSERT INTO emulator_profiles(id, name, emulator, executable, working_dir, generation, args_json, created_at, updated_at)
            VALUES ($id, $name, $emu, $exe, $wd, $gen, $args, $ts, $ts)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name, emulator = excluded.emulator,
                executable = excluded.executable, working_dir = excluded.working_dir,
                generation = excluded.generation, args_json = excluded.args_json,
                updated_at = excluded.updated_at
            """, ("$id", p.Id), ("$name", p.Name), ("$emu", p.Emulator), ("$exe", p.Executable), ("$wd", p.WorkingDir),
                 ("$gen", p.Generation), ("$args", JsonSerializer.Serialize(p.Args.ToList(), CoreJsonContext.Default.ListString)), ("$ts", ts));
    }

    public void DeleteProfile(string id) => Exec("DELETE FROM emulator_profiles WHERE id = $id", ("$id", id));
}
