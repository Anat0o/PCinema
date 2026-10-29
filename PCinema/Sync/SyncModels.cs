using System.IO;
using System.Text.Json;
namespace PCinema.Sync;

public sealed record ChangeStamp(long Milliseconds,long Counter,string Device):IComparable<ChangeStamp>
{
    public static readonly ChangeStamp Zero=new(0,0,"");
    public int CompareTo(ChangeStamp? other){if(other==null)return 1;int c=Milliseconds.CompareTo(other.Milliseconds);if(c==0)c=Counter.CompareTo(other.Counter);return c!=0?c:string.CompareOrdinal(Device,other.Device);}
}
public sealed record SyncEntry(string Key,string? Value,ChangeStamp Stamp,bool Deleted=false);
public sealed record SyncDocument(int Schema,string Generation,ChangeStamp GenerationStamp,string Device,List<SyncEntry> Entries);
public sealed record HistoryData(string SeriesKey,string EpisodeKey,int? TmdbId,string Title,int? Year,string Overview,int? Season,int? Number,long Position,long Duration,bool Watched,string LastPlayed);
public enum ConnectChoice { Merge, RestoreCloud, ReplaceCloud }
public interface ISyncTransport
{
    Task<IReadOnlyList<SyncDocument>> ReadAsync(CancellationToken token);
    Task WriteAsync(SyncDocument document,CancellationToken token);
}
public static class SyncMerge
{
    public static List<SyncEntry> Merge(IEnumerable<SyncEntry> entries)=>entries.GroupBy(e=>e.Key,StringComparer.Ordinal).Select(g=>g.OrderByDescending(e=>e.Stamp).ThenByDescending(e=>e.Deleted).ThenByDescending(e=>e.Value,StringComparer.Ordinal).First()).OrderBy(e=>e.Key,StringComparer.Ordinal).ToList();
    public static void Validate(SyncDocument d)
    {
        if(d==null||d.Schema!=1)throw new InvalidDataException("Облачные данные созданы другой версией PCinema. Обновите приложение.");
        if(d.Entries==null||d.Entries.Count>100000||string.IsNullOrWhiteSpace(d.Generation)||d.Generation.Length>100||d.Device==null||d.Device.Length>100||d.GenerationStamp==null||d.GenerationStamp.Milliseconds<0||d.GenerationStamp.Counter<0)throw new InvalidDataException("Некорректные облачные данные.");
        foreach(var e in d.Entries)if(e==null||e.Key==null||e.Stamp==null||e.Stamp.Device==null||e.Key.Length>300||(e.Value?.Length??0)>131072||e.Stamp.Milliseconds<0||e.Stamp.Counter<0||!AllowedKey(e.Key))throw new InvalidDataException("Некорректная запись синхронизации.");
    }
    public static readonly HashSet<string> Settings=["autoplay_next","subtitles_disabled","subtitle_appearance","close_to_tray"];
    public static bool AllowedKey(string key)=>key.StartsWith("history:")||key.StartsWith("alias:")||key.StartsWith("module:")||key.StartsWith("module-setting:")||key.StartsWith("setting:")&&Settings.Contains(key[8..]);
    public static string Serialize<T>(T value)=>JsonSerializer.Serialize(value);
}
