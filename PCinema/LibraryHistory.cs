namespace PCinema;

public sealed partial class LibraryStore
{
    // Numbers are meaningful only within a series. Unknown episodes stay isolated.
    const string IdentitySql="CASE WHEN e.season IS NULL OR e.number IS NULL THEN 'file:'||e.id ELSE (CASE WHEN s.tmdb_id IS NULL THEN 'local:'||s.id ELSE 'tmdb:'||s.tmdb_id END)||':'||e.season||':'||e.number END";
    static string HistoryKey(Series s,Episode e)=>e.Season==null||e.Number==null?$"file:{e.Id}":$"{(s.TmdbId.HasValue?$"tmdb:{s.TmdbId}":$"local:{s.Id}")}:{e.Season}:{e.Number}";

    void InitializeHistory(int version)
    {
        Execute("CREATE TABLE IF NOT EXISTS watch_history(identity TEXT PRIMARY KEY,position INTEGER NOT NULL,duration INTEGER NOT NULL,watched INTEGER NOT NULL,last_played TEXT NOT NULL); CREATE TABLE IF NOT EXISTS removed_files(episode_id INTEGER PRIMARY KEY);");
        if(version>=3)return;
        Execute("BEGIN IMMEDIATE; INSERT INTO watch_history SELECT "+IdentitySql+",e.position,e.duration,e.watched,e.last_played FROM episodes e JOIN series s ON s.id=e.series_id WHERE 1 ORDER BY e.last_played,e.position ON CONFLICT(identity) DO UPDATE SET position=excluded.position,duration=excluded.duration,watched=excluded.watched,last_played=excluded.last_played WHERE excluded.last_played>=watch_history.last_played; PRAGMA user_version=3; COMMIT;");
    }

    // Move existing viewing history to a newly recognized identity, retaining a
    // newer target history (including an explicit restart) when both exist.
    void MergeHistory(Episode e)=>Execute("INSERT INTO watch_history SELECT "+IdentitySql+",$p,$d,$w,$l FROM episodes e JOIN series s ON s.id=e.series_id WHERE e.id=$i ON CONFLICT(identity) DO UPDATE SET position=excluded.position,duration=excluded.duration,watched=excluded.watched,last_played=excluded.last_played WHERE excluded.last_played>watch_history.last_played;",("$i",e.Id),("$p",e.Position),("$d",e.Duration),("$w",e.Watched),("$l",e.LastPlayed));

    public HashSet<long> RemovedFiles()=>Query("SELECT episode_id FROM removed_files",r=>r.GetInt64(0)).ToHashSet();

    public List<Series> LoadCatalog()=>LoadCatalog(false);
    public List<Series> LoadCatalog(bool includeRemoved)
    {
        var removed=RemovedFiles();var all=Load();var result=new List<Series>();
        var excluded=Query("SELECT value FROM settings WHERE key LIKE 'excluded_source:%'",r=>r.GetString(0));var active=Folders();
        bool Visible(Episode e)=>!string.IsNullOrEmpty(e.Path)&&!removed.Contains(e.Id)&&(!excluded.Any(p=>UnderSource(e.Path,p))||active.Any(f=>UnderSource(e.Path,f.Path)));
        if(includeRemoved)foreach(var ep in all.SelectMany(s=>s.Episodes))if(!Visible(ep))ep.Available=false;
        foreach(var group in all.GroupBy(s=>s.TmdbId.HasValue?$"tmdb:{s.TmdbId}":$"local:{s.Id}"))
        {
            var representative=group.OrderBy(s=>s.Id).First();
            representative.Episodes=group.SelectMany(s=>s.Episodes).Where(e=>includeRemoved||Visible(e))
                .GroupBy(e=>e.Season.HasValue&&e.Number.HasValue?$"{e.Season}:{e.Number}":$"file:{e.Id}")
                .Select(g=>g.OrderByDescending(e=>e.Available).ThenByDescending(e=>e.Id).First())
                .OrderBy(e=>e.Season).ThenBy(e=>e.Number).ThenBy(e=>e.Path).ToList();
            if(representative.Episodes.Count>0)result.Add(representative);
        }
        return result;
    }
    static bool UnderSource(string file,string folder)=>System.IO.Path.GetFullPath(file).StartsWith(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder))+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)
        || (System.IO.Path.GetPathRoot(folder)==folder&&System.IO.Path.GetFullPath(file).StartsWith(folder,StringComparison.OrdinalIgnoreCase));
    public void RemoveFolder(LibraryFolder folder)
    {
        Execute("BEGIN IMMEDIATE; DELETE FROM folders WHERE id=$i; INSERT INTO settings(key,value) VALUES($k,$p) ON CONFLICT(key) DO UPDATE SET value=excluded.value; COMMIT;",("$i",folder.Id),("$k","excluded_source:"+folder.Path.ToUpperInvariant()),("$p",folder.Path));
    }
}
