using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PCinema.Sync;
namespace PCinema;

public sealed partial class LibraryStore
{
    readonly object syncGate=new();
    public event Action<bool>? SyncChanged;
    public T WithSyncLock<T>(Func<T> action) { lock(syncGate) return action(); }
    public string SyncDirectory=>Path.GetDirectoryName(new SqliteConnectionStringBuilder(connection).DataSource)!;
    void InitializeSync()
    {
        if(Query("SELECT name FROM sqlite_master WHERE name='sync_records'",r=>r.GetString(0)).Count>0)return;
        BackupSync("before-sync");
        Execute("BEGIN IMMEDIATE; CREATE TABLE IF NOT EXISTS sync_records(key TEXT PRIMARY KEY,value TEXT,stamp TEXT NOT NULL,deleted INTEGER NOT NULL DEFAULT 0); CREATE TABLE IF NOT EXISTS sync_identity(kind TEXT NOT NULL,id INTEGER NOT NULL,uuid TEXT NOT NULL,last_key TEXT,PRIMARY KEY(kind,id)); CREATE TABLE IF NOT EXISTS sync_meta(key TEXT PRIMARY KEY,value TEXT NOT NULL); CREATE TABLE IF NOT EXISTS sync_setting_versions(key TEXT PRIMARY KEY,changed TEXT NOT NULL); COMMIT;");
    }
    public void BackupSync(string label)
    {
        var folder=Path.Combine(SyncDirectory,"backups");Directory.CreateDirectory(folder);
        using var source=Open();using var destination=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(folder,label+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".db")}.ToString());destination.Open();source.BackupDatabase(destination);
    }
    public string? SyncMeta(string key)=>Query("SELECT value FROM sync_meta WHERE key=$k",r=>r.GetString(0),("$k",key)).FirstOrDefault();
    public void SyncMeta(string key,string value)=>Execute("INSERT INTO sync_meta VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v",("$k",key),("$v",value));
    public string DeviceId
    {
        get{lock(syncGate){var path=Path.Combine(SyncDirectory,"installation.id");if(File.Exists(path))return File.ReadAllText(path);var id=Guid.NewGuid().ToString("N");File.WriteAllText(path,id);return id;}}
    }
    public List<SyncEntry> SyncEntries()=>Query("SELECT key,value,stamp,deleted FROM sync_records",r=>new SyncEntry(r.GetString(0),r.IsDBNull(1)?null:r.GetString(1),JsonSerializer.Deserialize<ChangeStamp>(r.GetString(2))!,r.GetBoolean(3)));
    public void ObserveStamp(ChangeStamp stamp)
    {
        lock(syncGate){var prior=JsonSerializer.Deserialize<ChangeStamp>(SyncMeta("clock")??"null")??ChangeStamp.Zero;if(stamp.CompareTo(prior)>0)SyncMeta("clock",SyncMerge.Serialize(stamp));}
    }
    public ChangeStamp NextStamp(long? observed=null)
    {
        lock(syncGate)
        {
            var prior=JsonSerializer.Deserialize<ChangeStamp>(SyncMeta("clock")??"null")??ChangeStamp.Zero;
            long now=Math.Max(Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),observed??0),prior.Milliseconds);
            var stamp=new ChangeStamp(now,now==prior.Milliseconds?prior.Counter+1:0,DeviceId);SyncMeta("clock",SyncMerge.Serialize(stamp));return stamp;
        }
    }
    void PutSync(SyncEntry entry)=>Execute("INSERT INTO sync_records VALUES($k,$v,$s,$d) ON CONFLICT(key) DO UPDATE SET value=$v,stamp=$s,deleted=$d",("$k",entry.Key),("$v",entry.Value),("$s",SyncMerge.Serialize(entry.Stamp)),("$d",entry.Deleted));
    public void SetSyncValue(string key,string? value,bool baseline=false,long? changed=null)
    {
        lock(syncGate)
        {
            if(!SyncMerge.AllowedKey(key))throw new ArgumentException("Непереносимая настройка.");
            var old=Query("SELECT value,stamp,deleted FROM sync_records WHERE key=$k",r=>new SyncEntry(key,r.IsDBNull(0)?null:r.GetString(0),JsonSerializer.Deserialize<ChangeStamp>(r.GetString(1))!,r.GetBoolean(2)),("$k",key)).FirstOrDefault();if(old!=null&&old.Value==value&&old.Deleted==(value==null))return;
            var stamp=old==null&&baseline?new ChangeStamp(changed??0,0,DeviceId):NextStamp(changed);
            PutSync(new(key,value,stamp,value==null));SyncMeta("dirty","true");
        }
    }
    string StableId(string kind,long id)
    {
        var found=Query("SELECT uuid FROM sync_identity WHERE kind=$k AND id=$i",r=>r.GetString(0),("$k",kind),("$i",id)).FirstOrDefault();
        if(found!=null)return found;var uuid=Guid.NewGuid().ToString("N");Execute("INSERT INTO sync_identity(kind,id,uuid) VALUES($k,$i,$u)",("$k",kind),("$i",id),("$u",uuid));return uuid;
    }
    public void CaptureSync()
    {
        lock(syncGate)
        {
            foreach(var key in SyncMerge.Settings)
            {
                var value=Setting(key);if(value==null)continue;
                var stamp=Query("SELECT changed FROM sync_setting_versions WHERE key=$k",r=>r.GetString(0),("$k",key)).FirstOrDefault();
                SetSyncValue("setting:"+key,value,true,long.TryParse(stamp,out var time)?time:0);
            }
            foreach(var series in Load())foreach(var ep in series.Episodes.Where(e=>!string.IsNullOrEmpty(e.LastPlayed)))
            {
                string seriesKey=series.TmdbId is int tmdb?"tmdb:"+tmdb:"uuid:"+StableId("series",series.Id);
                string persistent=StableId("episode",ep.Id);
                string key=ep.Season.HasValue&&ep.Number.HasValue?seriesKey+":"+ep.Season+":"+ep.Number:"uuid:"+persistent;
                var previous=Query("SELECT last_key FROM sync_identity WHERE kind='episode' AND id=$i",r=>r.IsDBNull(0)?null:r.GetString(0),("$i",ep.Id)).FirstOrDefault();
                if(previous!=null&&previous!=key)SetSyncValue("alias:"+previous,key);
                Execute("UPDATE sync_identity SET last_key=$k WHERE kind='episode' AND id=$i",("$k",key),("$i",ep.Id));
                var data=new HistoryData(seriesKey,key,series.TmdbId,series.Title,series.Year,series.Overview,ep.Season,ep.Number,ep.Position,ep.Duration,ep.Watched,ep.LastPlayed);
                SetSyncValue("history:"+key,SyncMerge.Serialize(data),true,DateTimeOffset.TryParse(ep.LastPlayed,out var played)?played.ToUnixTimeMilliseconds():0);
            }
        }
    }
    public void ApplySync(IEnumerable<SyncEntry> incoming,bool replace=false)
    {
        lock(syncGate)
        {
            var merged=SyncMerge.Merge(incoming);foreach(var entry in merged)if(entry.Key.StartsWith("history:")&&!entry.Deleted)ValidateHistory(JsonSerializer.Deserialize<HistoryData>(entry.Value!)!);
            using var c=Open();using var tx=c.BeginTransaction();
            void Run(string sql,params (string,object?)[] args){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;foreach(var (k,v) in args)cmd.Parameters.AddWithValue(k,v??DBNull.Value);cmd.ExecuteNonQuery();}
            object? Scalar(string sql,params (string,object?)[] args){using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;foreach(var (k,v) in args)cmd.Parameters.AddWithValue(k,v??DBNull.Value);return cmd.ExecuteScalar();}
            if(replace){Run("DELETE FROM sync_records; DELETE FROM watch_history; UPDATE episodes SET position=0,duration=0,watched=0,last_played='';");foreach(var key in SyncMerge.Settings)Run("DELETE FROM settings WHERE key=$k",("$k",key));}
            foreach(var entry in merged)
            {
                Run("INSERT INTO sync_records VALUES($k,$v,$s,$d) ON CONFLICT(key) DO UPDATE SET value=$v,stamp=$s,deleted=$d",("$k",entry.Key),("$v",entry.Value),("$s",SyncMerge.Serialize(entry.Stamp)),("$d",entry.Deleted));
                if(entry.Key.StartsWith("setting:")&&SyncMerge.Settings.Contains(entry.Key[8..]))
                {
                    if(entry.Deleted)Run("DELETE FROM settings WHERE key=$k",("$k",entry.Key[8..]));
                    else Run("INSERT INTO settings VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=$v",("$k",entry.Key[8..]),("$v",entry.Value));
                }
            }
            var aliases=merged.Where(e=>e.Key.StartsWith("alias:")&&!e.Deleted).ToDictionary(e=>e.Key[6..],e=>e.Value!);
            string Resolve(string id){var seen=new HashSet<string>();while(aliases.TryGetValue(id,out var next)&&seen.Add(id))id=next;return id;}
            foreach(var entry in SyncMerge.Merge(merged.Where(e=>e.Key.StartsWith("history:")).Select(e=>e with{Key="history:"+Resolve(e.Key[8..])})))
            {
                if(entry.Deleted)
                {
                    var key=entry.Key[8..];var parts=key.Split(':');
                    var condition="e.id IN (SELECT id FROM sync_identity WHERE kind='episode' AND last_key=$key)";
                    if(parts.Length==4&&parts[0]=="tmdb"&&int.TryParse(parts[1],out _)&&int.TryParse(parts[2],out _)&&int.TryParse(parts[3],out _))condition="s.tmdb_id="+parts[1]+" AND e.season="+parts[2]+" AND e.number="+parts[3];
                    Run("DELETE FROM watch_history WHERE identity IN (SELECT "+IdentitySql+" FROM episodes e JOIN series s ON s.id=e.series_id WHERE "+condition+")",("$key",key));
                    Run("UPDATE episodes SET position=0,duration=0,watched=0,last_played='' WHERE id IN (SELECT e.id FROM episodes e JOIN series s ON s.id=e.series_id WHERE "+condition+")",("$key",key));continue;
                }
                var h=JsonSerializer.Deserialize<HistoryData>(entry.Value!)!;
                var resolved=entry.Key[8..];var segments=resolved.Split(':');
                if(resolved!=h.EpisodeKey&&segments.Length==4&&segments[0]=="tmdb"&&int.TryParse(segments[1],out var mappedTmdb)&&int.TryParse(segments[2],out var mappedSeason)&&int.TryParse(segments[3],out var mappedNumber))
                    h=h with{SeriesKey="tmdb:"+mappedTmdb,EpisodeKey=resolved,TmdbId=mappedTmdb,Season=mappedSeason,Number=mappedNumber};
                if(h.TmdbId.HasValue)foreach(var alias in aliases.Where(a=>Resolve(a.Key)==resolved))
                {
                    Run("UPDATE series SET tmdb_id=$tmdb WHERE id IN (SELECT series_id FROM episodes WHERE id IN (SELECT id FROM sync_identity WHERE kind='episode' AND last_key=$key))",("$tmdb",h.TmdbId),("$key",alias.Key));
                    Run("UPDATE sync_identity SET last_key=$new WHERE kind='episode' AND last_key=$old",("$new",resolved),("$old",alias.Key));
                }
                var sid=h.TmdbId.HasValue?Scalar("SELECT id FROM series WHERE tmdb_id=$t LIMIT 1",("$t",h.TmdbId)):Scalar("SELECT id FROM sync_identity WHERE kind='series' AND uuid=$u",("$u",h.SeriesKey.Replace("uuid:","")));
                if(sid==null||Scalar("SELECT id FROM series WHERE id=$i",("$i",sid))==null)
                {
                    Run("INSERT INTO series(search_title,title,year,tmdb_id,overview) VALUES($search,$n,$y,$t,$o)",("$search",h.TmdbId.HasValue?h.Title:"archive:"+h.SeriesKey),("$n",h.Title),("$y",h.Year),("$t",h.TmdbId),("$o",h.Overview));sid=Scalar("SELECT last_insert_rowid()");
                    if(!h.TmdbId.HasValue)Run("INSERT OR REPLACE INTO sync_identity(kind,id,uuid) VALUES('series',$i,$u)",("$i",sid),("$u",h.SeriesKey.Replace("uuid:","")));
                }
                var eid=h.Season.HasValue&&h.Number.HasValue?Scalar("SELECT id FROM episodes WHERE series_id=$s AND season=$season AND number=$n ORDER BY available DESC LIMIT 1",("$s",sid),("$season",h.Season),("$n",h.Number)):Scalar("SELECT id FROM sync_identity WHERE kind='episode' AND last_key=$k",("$k",h.EpisodeKey));
                if(eid==null||Scalar("SELECT id FROM episodes WHERE id=$i",("$i",eid))==null)
                {
                    Run("INSERT INTO episodes(series_id,season,number,path,available) VALUES($s,$season,$n,NULL,0)",("$s",sid),("$season",h.Season),("$n",h.Number));eid=Scalar("SELECT last_insert_rowid()");
                    Run("INSERT OR REPLACE INTO sync_identity(kind,id,uuid,last_key) VALUES('episode',$i,$u,$k)",("$i",eid),("$u",h.EpisodeKey.StartsWith("uuid:")?h.EpisodeKey[5..]:Guid.NewGuid().ToString("N")),("$k",h.EpisodeKey));
                }
                var identity=h.Season==null||h.Number==null?"file:"+eid:(h.TmdbId.HasValue?"tmdb:"+h.TmdbId:"local:"+sid)+":"+h.Season+":"+h.Number;
                Run("INSERT INTO watch_history VALUES($k,$p,$d,$w,$l) ON CONFLICT(identity) DO UPDATE SET position=$p,duration=$d,watched=$w,last_played=$l",("$k",identity),("$p",h.Position),("$d",h.Duration),("$w",h.Watched),("$l",h.LastPlayed));
                Run("INSERT INTO sync_meta VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value",("$key","remote-progress:"+eid),("$value",h.LastPlayed));
                Run("UPDATE episodes SET position=$p,duration=$d,watched=$w,last_played=$l WHERE id=$i",("$i",eid),("$p",h.Position),("$d",h.Duration),("$w",h.Watched),("$l",h.LastPlayed));
            }
            tx.Commit();if(merged.Count>0)ObserveStamp(merged.MaxBy(e=>e.Stamp)!.Stamp);
        }
    }
    static void ValidateHistory(HistoryData h)
    {
        if(h==null||h.Title==null||h.SeriesKey==null||h.EpisodeKey==null||h.Overview==null||h.Title.Length>2000||h.Overview.Length>100000||h.Position<0||h.Duration<0||h.Season<0||h.Number<0||h.TmdbId<=0||!DateTimeOffset.TryParse(h.LastPlayed,out _))throw new InvalidDataException("Некорректная история в облаке.");
    }
}
