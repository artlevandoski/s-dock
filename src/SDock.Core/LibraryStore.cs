using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SDock.Core.Metadata;

namespace SDock.Core;

/// <summary>
/// SQLite persistence for the library and emulator profiles.
/// The schema is identical to the Milestone 1 Python build, so existing libraries open unchanged.
/// </summary>
public sealed class LibraryStore : IDisposable
{
    public const int SchemaVersion = 10;

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
            sort_title       TEXT,
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
        -- v2: launch routing. Kept apart from editions so library data stays independent of emulators.
        CREATE TABLE IF NOT EXISTS generation_defaults (
            generation TEXT PRIMARY KEY CHECK (generation IN ('switch1', 'switch2')),
            profile_id TEXT NOT NULL REFERENCES emulator_profiles(id) ON DELETE CASCADE
        );
        -- v4: descriptive metadata and artwork. IGDB fields and the user's edits live in separate columns.
        CREATE TABLE IF NOT EXISTS edition_metadata (
            edition_id       TEXT PRIMARY KEY REFERENCES editions(id) ON DELETE CASCADE,
            igdb_name        TEXT,
            igdb_slug        TEXT,
            igdb_url         TEXT,
            summary          TEXT,
            genres_json      TEXT,
            release_year     INTEGER,
            cover_file       TEXT,
            keyart_file      TEXT,
            fetched_at       TEXT,
            user_summary     TEXT,
            user_cover_file  TEXT,
            user_keyart_file TEXT,
            user_logo_file   TEXT,
            logo_file        TEXT,
            steamgriddb_id   INTEGER,
            -- v7: TitleDB
            tdb_title_id     TEXT,
            tdb_name         TEXT,
            tdb_summary      TEXT,
            tdb_publisher    TEXT,
            tdb_release_date INTEGER,
            tdb_genres_json  TEXT,
            tdb_players      INTEGER,
            tdb_icon_file    TEXT,
            tdb_banner_file  TEXT,
            tdb_art_json     TEXT,
            tdb_fetched_at   TEXT,
            -- v9: nx-versions
            nx_latest_update INTEGER,
            nx_update_date   INTEGER,
            nx_first_date    INTEGER,
            -- v10: your own carousel text
            user_short_summary TEXT
        );
        -- v3: manual library assignment (Main Library / Arcade Archives), apart from game records.
        CREATE TABLE IF NOT EXISTS edition_library_overrides (
            edition_id TEXT PRIMARY KEY REFERENCES editions(id) ON DELETE CASCADE,
            library    TEXT NOT NULL CHECK (library IN ('main', 'arcade'))
        );
        CREATE TABLE IF NOT EXISTS edition_launch_overrides (
            edition_id TEXT PRIMARY KEY REFERENCES editions(id) ON DELETE CASCADE,
            profile_id TEXT NOT NULL REFERENCES emulator_profiles(id) ON DELETE CASCADE
        );
        -- v6: games the user has hidden from their libraries.
        CREATE TABLE IF NOT EXISTS hidden_editions (
            edition_id TEXT PRIMARY KEY REFERENCES editions(id) ON DELETE CASCADE,
            hidden_at  TEXT NOT NULL
        );
        """;

    private readonly SqliteConnection _conn;

    public string DatabaseFile { get; }

    /// <summary>
    /// Portable root: emulator paths inside it are stored relative to it and resolved on read.
    /// Null stores every path as given (absolute).
    /// </summary>
    public string? PortableRoot { get; }

    public LibraryStore(string databaseFile, string? portableRoot = null)
    {
        PortableRoot = portableRoot is null ? null : Path.GetFullPath(portableRoot);
        DatabaseFile = Path.GetFullPath(databaseFile);
        Directory.CreateDirectory(Path.GetDirectoryName(DatabaseFile)!);
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseFile,
            Pooling = false, // release the file on Dispose
        }.ToString());
        _conn.Open();
        Exec("PRAGMA foreign_keys = ON");
        // Write-ahead logging makes the many small writes (last selection, filters) cheap; the log is
        // folded back into the database on close so the file stays self-contained and portable.
        Exec("PRAGMA journal_mode = WAL");
        Exec("PRAGMA synchronous = NORMAL");
        Exec(Schema);
        Migrate();
    }

    /// <summary>Schema version recorded in the database (1 = Milestone 1, 2 = launch routing).</summary>
    public int StoredSchemaVersion
    {
        get
        {
            using var cmd = Cmd("SELECT value FROM meta WHERE key = 'schema_version'");
            return int.TryParse(cmd.ExecuteScalar() as string, out var v) ? v : 0;
        }
    }

    private void Migrate()
    {
        var version = StoredSchemaVersion;
        if (version == 0)
        {
            // Brand-new database: tables were just created at the current version.
            Exec("INSERT INTO meta(key, value) VALUES ('schema_version', $v)", ("$v", SchemaVersion.ToString()));
            return;
        }
        if (version < 2)
        {
            // v1 -> v2: the routing tables come from the schema script. Keep Play working exactly as
            // before by making a lone existing profile the default for the generations it supports.
            var profiles = ListProfiles();
            if (profiles.Count == 1)
            {
                var p = profiles[0];
                foreach (var g in new[] { Generations.Switch1, Generations.Switch2 })
                    if (p.Supports(g))
                        SetGenerationDefault(g, p.Id);
            }
            Exec("UPDATE meta SET value = '2' WHERE key = 'schema_version'");
        }
        if (version < 3)
        {
            // v2 -> v3: library overrides table comes from the schema script; nothing to convert.
            Exec("UPDATE meta SET value = '3' WHERE key = 'schema_version'");
        }
        if (version < 4)
        {
            // v3 -> v4: metadata table comes from the schema script; nothing to convert.
            Exec("UPDATE meta SET value = '4' WHERE key = 'schema_version'");
        }
        if (version < 5)
        {
            // v4 -> v5: SteamGridDB clear logo columns on the existing metadata table.
            var cols = new HashSet<string>();
            using (var cmd = Cmd("SELECT name FROM pragma_table_info('edition_metadata')"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) cols.Add(r.GetString(0));
            if (!cols.Contains("logo_file")) Exec("ALTER TABLE edition_metadata ADD COLUMN logo_file TEXT");
            if (!cols.Contains("steamgriddb_id")) Exec("ALTER TABLE edition_metadata ADD COLUMN steamgriddb_id INTEGER");
            Exec("UPDATE meta SET value = '5' WHERE key = 'schema_version'");
        }
        if (version < 6)
        {
            // v5 -> v6: hidden games table comes from the schema script; nothing to convert.
            Exec("UPDATE meta SET value = '6' WHERE key = 'schema_version'");
        }
        if (version < 7)
        {
            // v6 -> v7: TitleDB columns on the existing metadata table.
            var cols = new HashSet<string>();
            using (var cmd = Cmd("SELECT name FROM pragma_table_info('edition_metadata')"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) cols.Add(r.GetString(0));
            foreach (var (name, type) in TdbColumns)
                if (!cols.Contains(name)) Exec($"ALTER TABLE edition_metadata ADD COLUMN {name} {type}");
            Exec("UPDATE meta SET value = '7' WHERE key = 'schema_version'");
        }
        if (version < 8)
        {
            // v7 -> v8: the user's sort title.
            using (var cmd = Cmd("SELECT COUNT(*) FROM pragma_table_info('editions') WHERE name = 'sort_title'"))
                if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                    Exec("ALTER TABLE editions ADD COLUMN sort_title TEXT");
            Exec("UPDATE meta SET value = '8' WHERE key = 'schema_version'");
        }
        if (version < 9)
        {
            // v8 -> v9: nx-versions columns on the metadata table.
            var cols = new HashSet<string>();
            using (var cmd = Cmd("SELECT name FROM pragma_table_info('edition_metadata')"))
            using (var r = cmd.ExecuteReader())
                while (r.Read()) cols.Add(r.GetString(0));
            foreach (var name in new[] { "nx_latest_update", "nx_update_date", "nx_first_date" })
                if (!cols.Contains(name)) Exec($"ALTER TABLE edition_metadata ADD COLUMN {name} INTEGER");
            Exec("UPDATE meta SET value = '9' WHERE key = 'schema_version'");
        }
        if (version < 10)
        {
            // v9 -> v10: the user's own carousel text.
            using (var cmd = Cmd("SELECT COUNT(*) FROM pragma_table_info('edition_metadata') WHERE name = 'user_short_summary'"))
                if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
                    Exec("ALTER TABLE edition_metadata ADD COLUMN user_short_summary TEXT");
            Exec("UPDATE meta SET value = '10' WHERE key = 'schema_version'");
        }
    }

    private static readonly (string Name, string Type)[] TdbColumns =
    [
        ("tdb_title_id", "TEXT"), ("tdb_name", "TEXT"), ("tdb_summary", "TEXT"), ("tdb_publisher", "TEXT"),
        ("tdb_release_date", "INTEGER"), ("tdb_genres_json", "TEXT"), ("tdb_players", "INTEGER"), ("tdb_icon_file", "TEXT"),
        ("tdb_banner_file", "TEXT"), ("tdb_art_json", "TEXT"), ("tdb_fetched_at", "TEXT"),
    ];

    private static string? JsonList(IReadOnlyList<string> list) =>
        list.Count == 0 ? null : JsonSerializer.Serialize(list.ToList(), CoreJsonContext.Default.ListString);

    private static List<string> ListJson(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize(json, CoreJsonContext.Default.ListString) ?? [];

    public void Dispose()
    {
        try { Exec("PRAGMA wal_checkpoint(TRUNCATE)"); } catch (SqliteException) { }
        _conn.Dispose();
    }

    private SqliteTransaction? _tx;

    /// <summary>
    /// Runs <paramref name="work"/> in one transaction (one disk sync instead of one per write).
    /// Nested calls join the outer transaction.
    /// </summary>
    public void InTransaction(Action work)
    {
        if (_tx is not null) { work(); return; }
        _tx = _conn.BeginTransaction();
        try
        {
            work();
            _tx.Commit();
        }
        catch
        {
            _tx.Rollback();
            throw;
        }
        finally
        {
            _tx.Dispose();
            _tx = null;
        }
    }

    public static string UtcNow() =>
        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ helpers
    private SqliteCommand Cmd(string sql, params (string Name, object? Value)[] args)
    {
        var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _tx;
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
            SortTitle = Str(r, "sort_title"),
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
        return list.OrderBy(e => e.SortKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Sets the title a game is sorted by; null or blank goes back to sorting by its title.</summary>
    public void SetSortTitle(string editionId, string? sortTitle) =>
        Exec("UPDATE editions SET sort_title = $s, updated_at = $t WHERE id = $id",
            ("$s", string.IsNullOrWhiteSpace(sortTitle) ? null : sortTitle.Trim()), ("$t", UtcNow()), ("$id", editionId));

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
        InTransaction(() =>
        {
            Exec("""
                INSERT INTO editions(id, title, generation, content_kind, console_title_id, igdb_id, created_at, updated_at)
                VALUES ($id, $title, $gen, $kind, $tid, NULL, $ts, $ts)
                """, ("$id", id), ("$title", title), ("$gen", generation), ("$kind", contentKind), ("$tid", consoleTitleId), ("$ts", ts));
            Exec("""
                INSERT INTO edition_files(edition_id, folder_id, path, path_key, role, size_bytes, added_at, last_seen_at)
                VALUES ($id, $folder, $path, $key, 'primary', $size, $ts, $ts)
                """, ("$id", id), ("$folder", folderId), ("$path", Path.GetFullPath(path)), ("$key", AppPaths.PathKey(path)),
                     ("$size", sizeBytes), ("$ts", ts));
        });
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
        InTransaction(() =>
        {
            foreach (var p in paths)
                Exec("UPDATE edition_files SET last_seen_at = $t WHERE path_key = $k", ("$t", ts), ("$k", AppPaths.PathKey(p)));
        });
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
    private EmulatorProfile ReadProfile(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Name = r.GetString(r.GetOrdinal("name")),
        Emulator = r.GetString(r.GetOrdinal("emulator")),
        Executable = AppPaths.FromStoredPath(r.GetString(r.GetOrdinal("executable")), PortableRoot),
        WorkingDir = AppPaths.FromStoredPath(Str(r, "working_dir") ?? "", PortableRoot),
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
            """, ("$id", p.Id), ("$name", p.Name), ("$emu", p.Emulator), ("$exe", AppPaths.ToStoredPath(p.Executable, PortableRoot)),
                 ("$wd", AppPaths.ToStoredPath(p.WorkingDir, PortableRoot)),
                 ("$gen", p.Generation), ("$args", JsonSerializer.Serialize(p.Args.ToList(), CoreJsonContext.Default.ListString)), ("$ts", ts));
    }

    /// <summary>
    /// Saves everything from Emulator Settings in one transaction: either all of it is written or,
    /// if anything fails, nothing changes (no half-saved state where a removal went through alone).
    /// </summary>
    public void SaveEmulatorSettings(IEnumerable<string> removedIds, IReadOnlyList<EmulatorProfile> profiles,
        string? switch1Default, string? switch2Default)
    {
        InTransaction(() =>
        {
            foreach (var id in removedIds)
                DeleteProfile(id);
            foreach (var p in profiles)
                SaveProfile(p);
            SetGenerationDefault(Generations.Switch1, switch1Default);
            SetGenerationDefault(Generations.Switch2, switch2Default);
        });
    }

    /// <summary>Deletes a profile. Defaults and per-game choices that used it are removed with it.</summary>
    public void DeleteProfile(string id) => Exec("DELETE FROM emulator_profiles WHERE id = $id", ("$id", id));

    // ------------------------------------------------------------ launch routing
    public string? GetGenerationDefault(string generation)
    {
        using var cmd = Cmd("SELECT profile_id FROM generation_defaults WHERE generation = $g", ("$g", generation));
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Sets (or with null clears) the default profile for Switch 1 or Switch 2.</summary>
    public void SetGenerationDefault(string generation, string? profileId)
    {
        if (generation is not (Generations.Switch1 or Generations.Switch2))
            throw new ArgumentException($"Defaults exist only for Switch 1 and Switch 2, not {generation}");
        if (profileId is null)
            Exec("DELETE FROM generation_defaults WHERE generation = $g", ("$g", generation));
        else
            Exec("INSERT INTO generation_defaults(generation, profile_id) VALUES ($g, $p) " +
                 "ON CONFLICT(generation) DO UPDATE SET profile_id = excluded.profile_id",
                ("$g", generation), ("$p", profileId));
    }

    public string? GetLaunchOverride(string editionId)
    {
        using var cmd = Cmd("SELECT profile_id FROM edition_launch_overrides WHERE edition_id = $e", ("$e", editionId));
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Sets (or with null clears) the profile a specific game always launches with.</summary>
    // ------------------------------------------------------------------ metadata
    public const string IgdbClientIdKey = "igdb_client_id";
    public const string IgdbClientSecretKey = "igdb_client_secret";

    /// <summary>The user's own Twitch/IGDB credentials, kept in the local data folder only.</summary>
    public (string ClientId, string ClientSecret) IgdbCredentials
    {
        get => (GetSetting(IgdbClientIdKey) ?? "", GetSetting(IgdbClientSecretKey) ?? "");
        set
        {
            SetSetting(IgdbClientIdKey, value.ClientId.Trim());
            SetSetting(IgdbClientSecretKey, value.ClientSecret.Trim());
        }
    }

    public const string SteamGridDbKeyName = "steamgriddb_api_key";

    /// <summary>The user's own SteamGridDB API key (clear logos and backgrounds), local data folder only.</summary>
    public string SteamGridDbKey
    {
        get => GetSetting(SteamGridDbKeyName) ?? "";
        set => SetSetting(SteamGridDbKeyName, value.Trim());
    }

    public bool HasSteamGridDbKey => SteamGridDbKey.Length > 0;

    public bool HasIgdbCredentials => IgdbCredentials is { ClientId.Length: > 0, ClientSecret.Length: > 0 };

    public void SetIgdbId(string editionId, long? igdbId) =>
        Exec("UPDATE editions SET igdb_id = $i, updated_at = $t WHERE id = $id", ("$i", igdbId), ("$t", UtcNow()), ("$id", editionId));

    private static EditionMetadata ReadMetadata(SqliteDataReader r) => new()
    {
        EditionId = r.GetString(r.GetOrdinal("edition_id")),
        IgdbName = Str(r, "igdb_name"),
        IgdbSlug = Str(r, "igdb_slug"),
        IgdbUrl = Str(r, "igdb_url"),
        Summary = Str(r, "summary"),
        Genres = Str(r, "genres_json") is { } g ? JsonSerializer.Deserialize(g, CoreJsonContext.Default.ListString) ?? [] : [],
        ReleaseYear = Long(r, "release_year") is { } y ? (int)y : null,
        CoverFile = Str(r, "cover_file"),
        KeyArtFile = Str(r, "keyart_file"),
        FetchedAt = Str(r, "fetched_at"),
        UserSummary = Str(r, "user_summary"),
        UserCoverFile = Str(r, "user_cover_file"),
        UserKeyArtFile = Str(r, "user_keyart_file"),
        UserLogoFile = Str(r, "user_logo_file"),
        LogoFile = Str(r, "logo_file"),
        SteamGridDbId = Long(r, "steamgriddb_id"),
        TdbTitleId = Str(r, "tdb_title_id"),
        TdbName = Str(r, "tdb_name"),
        TdbSummary = Str(r, "tdb_summary"),
        TdbPublisher = Str(r, "tdb_publisher"),
        TdbReleaseDate = Long(r, "tdb_release_date") is { } d ? (int)d : null,
        TdbGenres = ListJson(Str(r, "tdb_genres_json")),
        TdbPlayers = Long(r, "tdb_players") is { } p ? (int)p : null,
        TdbIconFile = Str(r, "tdb_icon_file"),
        TdbBannerFile = Str(r, "tdb_banner_file"),
        TdbArtUrls = ListJson(Str(r, "tdb_art_json")),
        TdbFetchedAt = Str(r, "tdb_fetched_at"),
        NxLatestUpdate = Long(r, "nx_latest_update"),
        NxUpdateDate = Long(r, "nx_update_date") is { } nd ? (int)nd : null,
        NxFirstDate = Long(r, "nx_first_date") is { } nf ? (int)nf : null,
        UserShortSummary = Str(r, "user_short_summary"),
    };

    public EditionMetadata GetMetadata(string editionId)
    {
        using var cmd = Cmd("SELECT * FROM edition_metadata WHERE edition_id = $e", ("$e", editionId));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadMetadata(r) : EditionMetadata.Empty(editionId);
    }

    /// <summary>Forgets everything downloaded or chosen for a game's details (its files and settings stay).</summary>
    public void DeleteMetadata(string editionId) =>
        Exec("DELETE FROM edition_metadata WHERE edition_id = $e", ("$e", editionId));

    public Dictionary<string, EditionMetadata> AllMetadata()
    {
        var result = new Dictionary<string, EditionMetadata>();
        using var cmd = Cmd("SELECT * FROM edition_metadata");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var m = ReadMetadata(r);
            result[m.EditionId] = m;
        }
        return result;
    }

    public void SaveMetadata(EditionMetadata m) => Exec("""
        INSERT INTO edition_metadata(edition_id, igdb_name, igdb_slug, igdb_url, summary, genres_json, release_year,
                                     cover_file, keyart_file, fetched_at, user_summary, user_cover_file,
                                     user_keyart_file, user_logo_file, logo_file, steamgriddb_id,
                                     tdb_title_id, tdb_name, tdb_summary, tdb_publisher, tdb_release_date, tdb_genres_json,
                                     tdb_players, tdb_icon_file, tdb_banner_file, tdb_art_json, tdb_fetched_at,
                                     nx_latest_update, nx_update_date, nx_first_date, user_short_summary)
        VALUES ($e, $n, $s, $u, $sum, $g, $y, $c, $k, $f, $us, $uc, $uk, $ul, $lf, $sg,
                $ti, $tn, $ts, $tp, $td, $tg, $tpl, $tic, $tb, $ta, $tf, $nxl, $nxu, $nxf, $uss)
        ON CONFLICT(edition_id) DO UPDATE SET
            igdb_name = excluded.igdb_name, igdb_slug = excluded.igdb_slug, igdb_url = excluded.igdb_url,
            summary = excluded.summary, genres_json = excluded.genres_json, release_year = excluded.release_year,
            cover_file = excluded.cover_file, keyart_file = excluded.keyart_file, fetched_at = excluded.fetched_at,
            user_summary = excluded.user_summary, user_cover_file = excluded.user_cover_file,
            user_keyart_file = excluded.user_keyart_file, user_logo_file = excluded.user_logo_file,
            logo_file = excluded.logo_file, steamgriddb_id = excluded.steamgriddb_id,
            tdb_title_id = excluded.tdb_title_id, tdb_name = excluded.tdb_name, tdb_summary = excluded.tdb_summary,
            tdb_publisher = excluded.tdb_publisher, tdb_release_date = excluded.tdb_release_date,
            tdb_genres_json = excluded.tdb_genres_json, tdb_players = excluded.tdb_players,
            tdb_icon_file = excluded.tdb_icon_file, tdb_banner_file = excluded.tdb_banner_file,
            tdb_art_json = excluded.tdb_art_json, tdb_fetched_at = excluded.tdb_fetched_at,
            nx_latest_update = excluded.nx_latest_update, nx_update_date = excluded.nx_update_date,
            nx_first_date = excluded.nx_first_date, user_short_summary = excluded.user_short_summary
        """, ("$e", m.EditionId), ("$n", m.IgdbName), ("$s", m.IgdbSlug), ("$u", m.IgdbUrl), ("$sum", m.Summary),
             ("$g", m.Genres.Count == 0 ? null : JsonSerializer.Serialize(m.Genres.ToList(), CoreJsonContext.Default.ListString)),
             ("$y", m.ReleaseYear), ("$c", m.CoverFile), ("$k", m.KeyArtFile), ("$f", m.FetchedAt),
             ("$us", m.UserSummary), ("$uc", m.UserCoverFile), ("$uk", m.UserKeyArtFile), ("$ul", m.UserLogoFile),
             ("$lf", m.LogoFile), ("$sg", m.SteamGridDbId),
             ("$ti", m.TdbTitleId), ("$tn", m.TdbName), ("$ts", m.TdbSummary), ("$tp", m.TdbPublisher),
             ("$td", m.TdbReleaseDate), ("$tg", JsonList(m.TdbGenres)), ("$tpl", m.TdbPlayers), ("$tic", m.TdbIconFile),
             ("$tb", m.TdbBannerFile), ("$ta", JsonList(m.TdbArtUrls)), ("$tf", m.TdbFetchedAt),
             ("$nxl", m.NxLatestUpdate), ("$nxu", m.NxUpdateDate), ("$nxf", m.NxFirstDate), ("$uss", m.UserShortSummary));

    // ----------------------------------------------------------------- libraries
    public const string IncludeNeogeoKey = "include_aca_neogeo";

    public bool IncludeAcaNeogeo
    {
        get => GetSetting(IncludeNeogeoKey) == "1";
        set => SetSetting(IncludeNeogeoKey, value ? "1" : "0");
    }

    public const string IncludeSegaAgesKey = "include_sega_ages";

    /// <summary>Optional: SEGA AGES titles in Arcade Archives (otherwise they stay in Main Library).</summary>
    public bool IncludeSegaAges
    {
        get => GetSetting(IncludeSegaAgesKey) == "1";
        set => SetSetting(IncludeSegaAgesKey, value ? "1" : "0");
    }

    public Dictionary<string, string> LibraryOverrides()
    {
        var result = new Dictionary<string, string>();
        using var cmd = Cmd("SELECT edition_id, library FROM edition_library_overrides");
        using var r = cmd.ExecuteReader();
        while (r.Read()) result[r.GetString(0)] = r.GetString(1);
        return result;
    }

    public string? GetLibraryOverride(string editionId)
    {
        using var cmd = Cmd("SELECT library FROM edition_library_overrides WHERE edition_id = $e", ("$e", editionId));
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Pins a game to a library, or with null returns it to automatic (title prefix) placement.</summary>
    public void SetLibraryOverride(string editionId, string? library)
    {
        if (library is null)
            Exec("DELETE FROM edition_library_overrides WHERE edition_id = $e", ("$e", editionId));
        else if (library is Libraries.Main or Libraries.Arcade)
            Exec("INSERT INTO edition_library_overrides(edition_id, library) VALUES ($e, $l) " +
                 "ON CONFLICT(edition_id) DO UPDATE SET library = excluded.library", ("$e", editionId), ("$l", library));
        else
            throw new ArgumentException($"Unknown library: {library}");
    }

    // ------------------------------------------------------------------ hidden games
    public HashSet<string> HiddenEditions()
    {
        var result = new HashSet<string>();
        using var cmd = Cmd("SELECT edition_id FROM hidden_editions");
        using var r = cmd.ExecuteReader();
        while (r.Read()) result.Add(r.GetString(0));
        return result;
    }

    /// <summary>Hides a game from its library (it stays in the database and keeps its settings) or shows it again.</summary>
    public void SetHidden(string editionId, bool hidden)
    {
        if (hidden)
            Exec("INSERT INTO hidden_editions(edition_id, hidden_at) VALUES ($e, $t) ON CONFLICT(edition_id) DO NOTHING",
                ("$e", editionId), ("$t", UtcNow()));
        else
            Exec("DELETE FROM hidden_editions WHERE edition_id = $e", ("$e", editionId));
    }

    /// <summary>The library each edition is shown in.</summary>
    public string LibraryOf(Edition edition, IReadOnlyDictionary<string, string>? overrides = null, bool? includeNeogeo = null,
        bool? includeSegaAges = null) =>
        ArcadeSeries.LibraryFor(edition.Title,
            overrides is null ? GetLibraryOverride(edition.Id) : overrides.GetValueOrDefault(edition.Id),
            includeNeogeo ?? IncludeAcaNeogeo, includeSegaAges ?? IncludeSegaAges);

    // ------------------------------------------------------------------ versions
    /// <summary>Adds another file (alternate version) to an edition.</summary>
    public long AddFile(string editionId, string path, long? sizeBytes = null)
    {
        if (FindEditionByPath(path) is { } owner)
            throw new InvalidOperationException(owner == editionId
                ? "That file is already one of this game's versions."
                : "That file already belongs to another library entry.");
        var ts = UtcNow();
        Exec("""
            INSERT INTO edition_files(edition_id, folder_id, path, path_key, role, size_bytes, added_at, last_seen_at)
            VALUES ($e, NULL, $p, $k, 'alternate', $s, $t, $t)
            """, ("$e", editionId), ("$p", Path.GetFullPath(path)), ("$k", AppPaths.PathKey(path)), ("$s", sizeBytes), ("$t", ts));
        using var cmd = Cmd("SELECT last_insert_rowid()");
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>Removes a version from the library (the file itself is not touched). The last file can't be removed.</summary>
    public void RemoveFile(long fileId)
    {
        using var find = Cmd("SELECT edition_id, role FROM edition_files WHERE id = $id", ("$id", fileId));
        using var r = find.ExecuteReader();
        if (!r.Read()) return;
        var editionId = r.GetString(0);
        var wasPrimary = r.GetString(1) == "primary";
        r.Close();
        using (var count = Cmd("SELECT COUNT(*) FROM edition_files WHERE edition_id = $e", ("$e", editionId)))
            if ((long)count.ExecuteScalar()! <= 1)
                throw new InvalidOperationException("A game needs at least one file. Remove the game instead.");
        Exec("DELETE FROM edition_files WHERE id = $id", ("$id", fileId));
        if (wasPrimary)
            Exec("UPDATE edition_files SET role = 'primary' WHERE id = (SELECT MIN(id) FROM edition_files WHERE edition_id = $e)",
                ("$e", editionId));
    }

    /// <summary>Makes a file the one Play launches; the previous main file becomes an alternate.</summary>
    public void SetPrimaryFile(long fileId)
    {
        using var find = Cmd("SELECT edition_id FROM edition_files WHERE id = $id", ("$id", fileId));
        if (find.ExecuteScalar() is not string editionId) return;
        InTransaction(() =>
        {
            Exec("UPDATE edition_files SET role = 'alternate' WHERE edition_id = $e", ("$e", editionId));
            Exec("UPDATE edition_files SET role = 'primary' WHERE id = $id", ("$id", fileId));
        });
    }

    public void SetLaunchOverride(string editionId, string? profileId)
    {
        if (profileId is null)
            Exec("DELETE FROM edition_launch_overrides WHERE edition_id = $e", ("$e", editionId));
        else
            Exec("INSERT INTO edition_launch_overrides(edition_id, profile_id) VALUES ($e, $p) " +
                 "ON CONFLICT(edition_id) DO UPDATE SET profile_id = excluded.profile_id",
                ("$e", editionId), ("$p", profileId));
    }
}
