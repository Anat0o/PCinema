using System.IO;
using System.Text.Json;
using PCinema.ModuleApi;
namespace PCinema.Modules;
public sealed class ModuleContext : IModuleContext,IDisposable
{
    readonly MainWindow host;readonly ModuleRuntime runtime;readonly object gate=new();readonly List<Action<AppEvent>> handlers=[];readonly SemaphoreSlim settingsLock=new(1);bool disposed;
    public string ModuleId{get;}public string DataDirectory{get;}
    public ModuleContext(string id,MainWindow host,ModuleRuntime runtime){ModuleId=id;this.host=host;this.runtime=runtime;DataDirectory=runtime.Packages.DataDirectory(id);Directory.CreateDirectory(DataDirectory);}
    void Check(){if(disposed||runtime.Stopping)throw new ObjectDisposedException(ModuleId);}
    void CheckSettings(){if(disposed)throw new ObjectDisposedException(ModuleId);}
    public Task<IReadOnlyList<SeriesInfo>> GetLibraryAsync(bool includeUnavailable,CancellationToken cancellationToken=default){Check();return host.ModuleLibraryAsync(includeUnavailable,cancellationToken);}
    public async Task<IReadOnlyList<SeriesInfo>> GetHistoryAsync(CancellationToken cancellationToken=default)=>Array.AsReadOnly((await GetLibraryAsync(true,cancellationToken)).Where(s=>s.Episodes.Any(e=>!string.IsNullOrEmpty(e.LastPlayed))).OrderByDescending(s=>s.Episodes.Max(e=>e.LastPlayed)).ToArray());
    public Task NavigateAsync(Route route,long? seriesId=null){Check();return host.Dispatcher.InvokeAsync(()=>host.ModuleNavigateAsync(route,seriesId)).Task.Unwrap();}
    public Task OpenPageAsync(string id){Check();return OnUIAsync(()=>host.OpenModulePage(ModuleId,id));}
    public Task GoBackAsync()=>OnUIAsync(host.Back);
    public Task PlayAsync(long seriesId,long episodeId){Check();return host.Dispatcher.InvokeAsync(()=>host.ModulePlayAsync(seriesId,episodeId)).Task.Unwrap();}
    public Task SetPausedAsync(bool paused)=>OnUIAsync(()=>host.ModulePause(paused));
    public Task SeekAsync(long milliseconds)=>OnUIAsync(()=>host.ModuleSeek(milliseconds));
    public Task ToggleFullscreenAsync()=>OnUIAsync(host.ToggleFullscreen);
    public Task TogglePictureInPictureAsync()=>OnUIAsync(host.TogglePictureInPicture);
    public Task NextEpisodeAsync()=>OnUIAsync(host.ModuleNextEpisode);
    public Task SetVolumeAsync(int percent)=>OnUIAsync(()=>host.ModuleVolume(Math.Clamp(percent,0,100)));
    public Task ShowPlayerOptionsAsync()=>OnUIAsync(host.ModulePlayerOptions);
    public PlayerState Player=>host.ModulePlayerState;
    public async Task<string?> ReadSettingAsync(string key){CheckSettings();await settingsLock.WaitAsync();try{return await Task.Run(()=>ReadSettings().GetValueOrDefault(key));}finally{settingsLock.Release();}}
    public async Task WriteSettingAsync(string key,string? value){CheckSettings();if(key.Length>128||(value?.Length??0)>65536)throw new ArgumentException("Настройка слишком большая.");await settingsLock.WaitAsync();try{await Task.Run(()=>{var data=ReadSettings();if(value==null)data.Remove(key);else data[key]=value;ModulePackages.WriteAtomic(Path.Combine(DataDirectory,"settings.json"),JsonSerializer.Serialize(data));});host.ModulePreferencesChanged();}finally{settingsLock.Release();}}
    Dictionary<string,string> ReadSettings(){var file=Path.Combine(DataDirectory,"settings.json");return File.Exists(file)?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(file))??[]:[];}
    public IDisposable Subscribe(Action<AppEvent> handler){Check();lock(gate)handlers.Add(handler);return new Subscription(()=>{lock(gate)handlers.Remove(handler);});}
    internal void Publish(AppEvent e){Action<AppEvent>[] list;lock(gate)list=handlers.ToArray();foreach(var handler in list){try{handler(e);}catch(Exception ex){Log("event",ex);}}}
    public Task OnUIAsync(Action action){Check();return host.Dispatcher.InvokeAsync(()=>{Check();action();}).Task;}
    public void Log(string message,Exception? error=null)=>PerformanceLog.Write("module."+ModuleId+"."+System.Text.RegularExpressions.Regex.Replace(message,@"[^a-zA-Z0-9_.-]","")[..Math.Min(40,System.Text.RegularExpressions.Regex.Replace(message,@"[^a-zA-Z0-9_.-]","").Length)],error:error);
    public void Dispose(){disposed=true;lock(gate)handlers.Clear();}
    sealed class Subscription(Action release):IDisposable{Action? action=release;public void Dispose()=>Interlocked.Exchange(ref action,null)?.Invoke();}
}
