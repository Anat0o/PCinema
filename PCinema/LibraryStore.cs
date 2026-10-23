using Microsoft.Data.Sqlite;
using System.IO;
namespace PCinema;

public sealed partial class LibraryStore
{
    public static string DataDirectory => Environment.GetEnvironmentVariable("PCINEMA_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCinema");
    readonly string connection;
    public LibraryStore(string? directory = null)
    {
        directory ??= DataDirectory; Directory.CreateDirectory(directory); connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "library.db") }.ToString();
        var version = Query("PRAGMA user_version", r => r.GetInt32(0)).Single();
        if (version > 3) throw new InvalidOperationException("Эта библиотека создана более новой версией PCinema. Обновите приложение.");
        Execute("PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS folders(id INTEGER PRIMARY KEY,path TEXT UNIQUE COLLATE NOCASE); CREATE TABLE IF NOT EXISTS series(id INTEGER PRIMARY KEY,search_title TEXT NOT NULL, title TEXT NOT NULL,year INTEGER,tmdb_id INTEGER,overview TEXT,poster TEXT,backdrop TEXT); CREATE TABLE IF NOT EXISTS episodes(id INTEGER PRIMARY KEY,series_id INTEGER NOT NULL,season INTEGER,number INTEGER,path TEXT UNIQUE COLLATE NOCASE,available INTEGER DEFAULT 1,position INTEGER DEFAULT 0,duration INTEGER DEFAULT 0,watched INTEGER DEFAULT 0,last_played TEXT DEFAULT '',added TEXT DEFAULT CURRENT_TIMESTAMP); CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY,value TEXT); CREATE TABLE IF NOT EXISTS series_details(series_id INTEGER PRIMARY KEY,genres TEXT NOT NULL DEFAULT '',rating REAL NOT NULL DEFAULT 0,logo TEXT); CREATE TABLE IF NOT EXISTS episode_details(episode_id INTEGER PRIMARY KEY,title TEXT NOT NULL DEFAULT '',still TEXT); CREATE INDEX IF NOT EXISTS idx_episodes_series ON episodes(series_id);");
        InitializeHistory(version);
    }
    SqliteConnection Open() { var c = new SqliteConnection(connection); c.Open(); return c; }
    public void Execute(string sql, params (string, object?)[] args) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; foreach (var (key, value) in args) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value); cmd.ExecuteNonQuery(); }
    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; foreach (var (key, value) in args) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value); using var r = cmd.ExecuteReader(); var list = new List<T>(); while (r.Read()) list.Add(map(r)); return list; }
    public List<LibraryFolder> Folders() => Query("SELECT id,path FROM folders", r => new LibraryFolder(r.GetInt64(0), r.GetString(1)));
    public void AddFolder(string path) => Execute("INSERT OR IGNORE INTO folders(path) VALUES($p)", ("$p", Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))));
    public string? Setting(string key) => Query("SELECT value FROM settings WHERE key=$k", r => r.GetString(0), ("$k", key)).FirstOrDefault();
    public void Setting(string key, string value) => Execute("INSERT INTO settings VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value", ("$k", key), ("$v", value));
    public List<Series> Load()
    {
        var all = Query("SELECT id,search_title,title,year,tmdb_id,overview,poster,backdrop FROM series", r => new Series { Id = r.GetInt64(0), SearchTitle = r.GetString(1), Title = r.GetString(2), Year = r.IsDBNull(3) ? null : r.GetInt32(3), TmdbId = r.IsDBNull(4) ? null : r.GetInt32(4), Overview = r.IsDBNull(5) ? "Подключите TMDB в настройках, чтобы получить описание и постеры." : r.GetString(5), Poster = r.IsDBNull(6) ? null : r.GetString(6), Backdrop = r.IsDBNull(7) ? null : r.GetString(7) });
        var episodes = Query("SELECT id,series_id,season,number,path,available,position,duration,watched,last_played FROM episodes ORDER BY season,number,path", r => new Episode { Id = r.GetInt64(0), SeriesId = r.GetInt64(1), Season = r.IsDBNull(2) ? null : r.GetInt32(2), Number = r.IsDBNull(3) ? null : r.GetInt32(3), Path = r.GetString(4), Available = r.GetBoolean(5), Position = r.GetInt64(6), Duration = r.GetInt64(7), Watched = r.GetBoolean(8), LastPlayed = r.GetString(9) });
        var grouped = episodes.ToLookup(x => x.SeriesId);
        var extras = Query("SELECT series_id,genres,rating,logo FROM series_details", r => new { Id=r.GetInt64(0), Genres=r.GetString(1), Rating=r.GetDouble(2), Logo=r.IsDBNull(3)?null:r.GetString(3) }).ToDictionary(x=>x.Id);
        var episodeExtras = Query("SELECT episode_id,title,still FROM episode_details", r=> new {Id=r.GetInt64(0),Title=r.GetString(1),Still=r.IsDBNull(2)?null:r.GetString(2)}).ToDictionary(x=>x.Id);
        foreach(var ep in episodes) if(episodeExtras.TryGetValue(ep.Id,out var detail)) { ep.Title=detail.Title; ep.Still=detail.Still; }
        var history=Query("SELECT identity,position,duration,watched,last_played FROM watch_history",r=>new {Key=r.GetString(0),Position=r.GetInt64(1),Duration=r.GetInt64(2),Watched=r.GetBoolean(3),LastPlayed=r.GetString(4)}).ToDictionary(x=>x.Key);
        var seriesById=all.ToDictionary(s=>s.Id);
        foreach(var ep in episodes) if(history.TryGetValue(HistoryKey(seriesById[ep.SeriesId],ep),out var h)){ep.Position=h.Position;ep.Duration=h.Duration;ep.Watched=h.Watched;ep.LastPlayed=h.LastPlayed;}
        foreach (var s in all) { s.Episodes = grouped[s.Id].ToList(); if(extras.TryGetValue(s.Id,out var detail)) {s.Genres=detail.Genres;s.Rating=detail.Rating;s.Logo=detail.Logo;} } return all;
    }
    public void Import(string path, ParsedName parsed)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = "SELECT id FROM episodes WHERE path=$p"; cmd.Parameters.AddWithValue("$p", path); if (cmd.ExecuteScalar() != null) { cmd.CommandText = "UPDATE episodes SET available=1 WHERE path=$p; DELETE FROM removed_files WHERE episode_id IN (SELECT id FROM episodes WHERE path=$p)"; cmd.ExecuteNonQuery(); tx.Commit(); return; }
        cmd.Parameters.Clear(); cmd.CommandText = "SELECT id FROM series WHERE search_title=$t COLLATE NOCASE AND year IS $y LIMIT 1"; cmd.Parameters.AddWithValue("$t", parsed.Title); cmd.Parameters.AddWithValue("$y", (object?)parsed.Year ?? DBNull.Value); var id = cmd.ExecuteScalar();
        if (id == null) { cmd.CommandText = "INSERT INTO series(search_title,title,year) VALUES($t,$t,$y); SELECT last_insert_rowid();"; id = cmd.ExecuteScalar(); }
        cmd.Parameters.Clear(); cmd.CommandText = "INSERT INTO episodes(series_id,season,number,path) VALUES($i,$s,$n,$p)"; cmd.Parameters.AddWithValue("$i", id!); cmd.Parameters.AddWithValue("$s", (object?)parsed.Season ?? DBNull.Value); cmd.Parameters.AddWithValue("$n", (object?)parsed.Number ?? DBNull.Value); cmd.Parameters.AddWithValue("$p", path); cmd.ExecuteNonQuery(); tx.Commit();
    }
    public void SaveProgress(Episode e)
    {
        e.LastPlayed=DateTime.UtcNow.ToString("O");SaveProgress(ProgressSnapshot.From(e));
    }
    public void SaveProgress(ProgressSnapshot snapshot)
    {
        Execute("BEGIN IMMEDIATE; UPDATE episodes SET position=$p,duration=$d,watched=$w,last_played=$l WHERE id=$i AND last_played<=$l; INSERT INTO watch_history(identity,position,duration,watched,last_played) SELECT "+IdentitySql+",$p,$d,$w,$l FROM episodes e JOIN series s ON s.id=e.series_id WHERE e.id=$i AND e.last_played<=$l ON CONFLICT(identity) DO UPDATE SET position=excluded.position,duration=excluded.duration,watched=excluded.watched,last_played=excluded.last_played WHERE excluded.last_played>=watch_history.last_played; COMMIT;", ("$p", snapshot.Position), ("$d", snapshot.Duration), ("$w", snapshot.Watched), ("$l", snapshot.ChangedAt), ("$i", snapshot.EpisodeId));
    }
    public void Relink(Episode e, string path)
    {
        path = Path.GetFullPath(path); using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT id,position,duration,watched,last_played FROM episodes WHERE path=$p COLLATE NOCASE AND id<>$i"; cmd.Parameters.AddWithValue("$p", path); cmd.Parameters.AddWithValue("$i", e.Id);
        long? duplicate = null; using (var reader = cmd.ExecuteReader()) { if (reader.Read()) { duplicate = reader.GetInt64(0); if (string.CompareOrdinal(reader.GetString(4), e.LastPlayed) > 0) { e.Position = reader.GetInt64(1); e.LastPlayed = reader.GetString(4); } e.Duration = Math.Max(e.Duration, reader.GetInt64(2)); e.Watched |= reader.GetBoolean(3); } }
        if (duplicate.HasValue) { cmd.Parameters.Clear(); cmd.CommandText = "DELETE FROM removed_files WHERE episode_id=$id; DELETE FROM episodes WHERE id=$id"; cmd.Parameters.AddWithValue("$id", duplicate.Value); cmd.ExecuteNonQuery(); }
        cmd.Parameters.Clear(); cmd.CommandText = "UPDATE episodes SET path=$p,available=1,position=$pos,duration=$d,watched=$w,last_played=$l WHERE id=$i; DELETE FROM removed_files WHERE episode_id=$i; DELETE FROM series WHERE NOT EXISTS(SELECT 1 FROM episodes WHERE series_id=series.id);";
        cmd.Parameters.AddWithValue("$p", path); cmd.Parameters.AddWithValue("$pos", e.Position); cmd.Parameters.AddWithValue("$d", e.Duration); cmd.Parameters.AddWithValue("$w", e.Watched); cmd.Parameters.AddWithValue("$l", e.LastPlayed); cmd.Parameters.AddWithValue("$i", e.Id); cmd.ExecuteNonQuery(); tx.Commit();
        MergeHistory(e);
    }
    public void Correct(Episode e, int season, int number)
    {
        var saved=Load().SelectMany(s=>s.Episodes).First(x=>x.Id==e.Id);
        Execute("UPDATE episodes SET season=$s,number=$n WHERE id=$i", ("$s", season), ("$n", number), ("$i", e.Id));
        MergeHistory(saved);
    }
    public void Metadata(long id, Metadata m, string? poster, string? backdrop)
    {
        var saved=Load().First(s=>s.Id==id).Episodes;
        Execute("UPDATE series SET title=$t,year=$y,tmdb_id=$m,overview=$o,poster=COALESCE($p,poster),backdrop=COALESCE($b,backdrop) WHERE id=$i", ("$t", m.Name), ("$y", m.Year), ("$m", m.Id), ("$o", m.Overview), ("$p", poster), ("$b", backdrop), ("$i", id));
        foreach(var ep in saved)MergeHistory(ep);
    }
}


