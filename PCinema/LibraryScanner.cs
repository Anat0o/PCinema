using System.IO;
namespace PCinema;

public sealed class LibraryScanner(LibraryStore store, Func<string,FilePresence>? inspect = null) : IDisposable
{
    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".mkv", ".mp4", ".avi", ".mov", ".m4v", ".ts" };
    readonly List<FileSystemWatcher> watchers = []; readonly SemaphoreSlim gate = new(1); readonly CancellationTokenSource shutdown = new();
    public event Action<string>? Status; public event Action? Updated;
    public void Watch()
    {
        lock(watchers)
        {
            if(shutdown.IsCancellationRequested)return;
            var paths=store.Folders().Select(f=>f.Path).Where(Directory.Exists).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var w in watchers.ToArray())if(!paths.Contains(w.Path)){w.Dispose();watchers.Remove(w);}
            foreach(var path in paths.Where(p=>!watchers.Any(w=>string.Equals(w.Path,p,StringComparison.OrdinalIgnoreCase))))
                try{var w=new FileSystemWatcher(path){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.Size|NotifyFilters.LastWrite};w.Created+=Changed;w.Changed+=Changed;w.Deleted+=Changed;w.Renamed+=Changed;w.Error+=(_,_)=>Queue();w.EnableRaisingEvents=true;watchers.Add(w);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
    readonly HashSet<string> pendingPaths=new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string,bool> sourceStates=new(StringComparer.OrdinalIgnoreCase);
    bool fullRequested;DateTime lastFull=DateTime.MinValue;
    void Changed(object sender,FileSystemEventArgs e){Queue(e.FullPath);if(e is RenamedEventArgs renamed)Queue(renamed.OldFullPath);}
    int checkingSources;
    public async Task CheckSourcesAsync()
    {
        if(Interlocked.Exchange(ref checkingSources,1)!=0)return;
        try{
        var changed=await Task.Run(()=>{Watch();var result=new List<string>();foreach(var f in store.Folders()){var available=Directory.Exists(f.Path);if(!sourceStates.TryGetValue(f.Path,out var old)||old!=available)result.Add(f.Path);sourceStates[f.Path]=available;}return result;});
        if(DateTime.UtcNow-lastFull>=TimeSpan.FromMinutes(10))await ScanAsync();else if(changed.Count>0)await ScanCore(changed.ToArray());
        }finally{Volatile.Write(ref checkingSources,0);}
    }
    int revision;
    void Queue(string? path=null)
    {
        lock(pendingPaths){if(path==null)fullRequested=true;else pendingPaths.Add(path);}
        int current=Interlocked.Increment(ref revision);
        _=Task.Run(async()=>{try{await Task.Delay(2000,shutdown.Token);if(current!=revision)return;string[]? changes;lock(pendingPaths){changes=fullRequested?null:pendingPaths.ToArray();pendingPaths.Clear();fullRequested=false;}await ScanCore(changes);}catch(OperationCanceledException){}});
    }
    public Task ScanAsync()=>ScanCore(null);
    static bool Within(string file,string root)=>string.Equals(file,root,StringComparison.OrdinalIgnoreCase)||file.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    async Task ScanCore(string[]? changes)
    {
        try{await gate.WaitAsync(shutdown.Token);}catch(OperationCanceledException){return;}
        try
        {
            Status?.Invoke("Проверяем папки…"); bool changed = false;
            await Task.Run(async () =>
            {
                Watch();
                var existing = store.Query("SELECT id,path,available FROM episodes WHERE path IS NOT NULL AND path<>''",r=>new Episode{Id=r.GetInt64(0),Path=r.GetString(1),Available=r.GetBoolean(2)});
                var removed=store.RemovedFiles();
                var known = existing.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var ep in existing.Where(e=>changes==null||changes.Any(p=>Within(e.Path,p))))
                {
                    shutdown.Token.ThrowIfCancellationRequested();
                    var presence=(inspect ?? Inspect)(ep.Path);var available=presence==FilePresence.Present;
                    if(available!=ep.Available){store.Execute("UPDATE episodes SET available=$a WHERE id=$i",("$a",available),("$i",ep.Id));changed=true;}
                    // An offline drive must not undo an earlier confirmed deletion.
                    if(presence==FilePresence.Deleted&&!removed.Contains(ep.Id)){store.Execute("INSERT OR IGNORE INTO removed_files VALUES($i)",("$i",ep.Id));changed=true;}
                    else if(available&&removed.Contains(ep.Id)){store.Execute("DELETE FROM removed_files WHERE episode_id=$i",("$i",ep.Id));changed=true;}
                }
                foreach (var folder in (changes==null?store.Folders():changes.Select(p=>new LibraryFolder(0,p)).ToList()))
                {
                    if (!Directory.Exists(folder.Path)&&!File.Exists(folder.Path)) continue;
                    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                    List<string> paths; try { paths = (File.Exists(folder.Path)?new[]{folder.Path}:Directory.EnumerateFiles(folder.Path, "*", options)).Where(p => Extensions.Contains(Path.GetExtension(p))).ToList(); } catch (IOException) { continue; }
                    
                    var candidates = new List<(string Path, long Size, DateTime Write)>();
                    foreach (var p in paths) { if (known.Contains(p)) continue; try { var info = new FileInfo(p); candidates.Add((p, info.Length, info.LastWriteTimeUtc)); } catch (IOException) { } }
                    if (candidates.Count > 0) await Task.Delay(2000, shutdown.Token);
                    foreach (var (p, size, write) in candidates) { shutdown.Token.ThrowIfCancellationRequested(); try { var info = new FileInfo(p); if (size == 0 || info.Length != size || info.LastWriteTimeUtc != write) { Queue(p); continue; } using (var stream = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read)) { } store.Import(p, NameParser.Parse(p)); known.Add(p); changed = true; } catch (IOException) { Queue(p); } catch (UnauthorizedAccessException) { } }
                }
            }, shutdown.Token);
            if(changes==null)lastFull=DateTime.UtcNow;
            if (changed) Updated?.Invoke(); Status?.Invoke("Библиотека обновлена");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Status?.Invoke("Не удалось обновить библиотеку: " + ex.Message); }
        finally { gate.Release(); }
    }
    public static FilePresence Inspect(string path)
    {
        try
        {
            // A missing drive or UNC share is temporary unavailability, not deletion.
            var root=Path.GetPathRoot(Path.GetFullPath(path));
            if(string.IsNullOrEmpty(root)||!Directory.Exists(root))return FilePresence.Unavailable;
            try { return (File.GetAttributes(path)&FileAttributes.Directory)==0?FilePresence.Present:FilePresence.Deleted; }
            catch(FileNotFoundException){return Directory.Exists(root)?FilePresence.Deleted:FilePresence.Unavailable;}
            catch(DirectoryNotFoundException){return Directory.Exists(root)?FilePresence.Deleted:FilePresence.Unavailable;}
        }
        catch(IOException){}catch(UnauthorizedAccessException){}
        return FilePresence.Unavailable;
    }
    public void Dispose() { shutdown.Cancel(); lock(watchers){foreach (var w in watchers) w.Dispose();watchers.Clear();} }
}

public enum FilePresence { Present, Deleted, Unavailable }

