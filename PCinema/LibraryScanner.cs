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
    void Changed(object sender, FileSystemEventArgs e) => Queue();
    int revision;
    void Queue() { int current = Interlocked.Increment(ref revision); _ = Task.Run(async () => { try { await Task.Delay(2000, shutdown.Token); if (current == revision) await ScanAsync(); } catch (OperationCanceledException) { } }); }
    public async Task ScanAsync()
    {
        if (!await gate.WaitAsync(0)) { Queue(); return; }
        try
        {
            Status?.Invoke("Проверяем папки…"); bool changed = false;
            await Task.Run(async () =>
            {
                Watch();
                var existing = store.Load().SelectMany(x => x.Episodes).ToList();
                var removed=store.RemovedFiles();
                var known = existing.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var ep in existing)
                {
                    shutdown.Token.ThrowIfCancellationRequested();
                    var presence=(inspect ?? Inspect)(ep.Path);var available=presence==FilePresence.Present;
                    if(available!=ep.Available){store.Execute("UPDATE episodes SET available=$a WHERE id=$i",("$a",available),("$i",ep.Id));changed=true;}
                    // An offline drive must not undo an earlier confirmed deletion.
                    if(presence==FilePresence.Deleted&&!removed.Contains(ep.Id)){store.Execute("INSERT OR IGNORE INTO removed_files VALUES($i)",("$i",ep.Id));changed=true;}
                    else if(available&&removed.Contains(ep.Id)){store.Execute("DELETE FROM removed_files WHERE episode_id=$i",("$i",ep.Id));changed=true;}
                }
                foreach (var folder in store.Folders())
                {
                    if (!Directory.Exists(folder.Path)) continue;
                    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                    List<string> paths; try { paths = Directory.EnumerateFiles(folder.Path, "*", options).Where(p => Extensions.Contains(Path.GetExtension(p))).ToList(); } catch (IOException) { continue; }
                    
                    var candidates = new List<(string Path, long Size, DateTime Write)>();
                    foreach (var p in paths) { if (known.Contains(p)) continue; try { var info = new FileInfo(p); candidates.Add((p, info.Length, info.LastWriteTimeUtc)); } catch (IOException) { } }
                    if (candidates.Count > 0) await Task.Delay(2000, shutdown.Token);
                    foreach (var (p, size, write) in candidates) { shutdown.Token.ThrowIfCancellationRequested(); try { var info = new FileInfo(p); if (size == 0 || info.Length != size || info.LastWriteTimeUtc != write) { Queue(); continue; } using (var stream = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read)) { } store.Import(p, NameParser.Parse(p)); known.Add(p); changed = true; } catch (IOException) { Queue(); } catch (UnauthorizedAccessException) { } }
                }
            }, shutdown.Token);
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

