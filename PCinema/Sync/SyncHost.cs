using System.IO;
using System.Text.Json;
using PCinema.ModuleApi;
using PCinema.Modules;
using PCinema.Sync;
namespace PCinema;

public partial class MainWindow
{
    public SyncService CloudSync {get;private set;}=null!;
    bool applyingCloud,deferredCloud;
    void InitializeCloudSync()
    {
        CloudSync=new(store,()=>Dispatcher.InvokeAsync(CaptureModulePreferences).Task.Unwrap(),()=>Dispatcher.InvokeAsync(ApplyCloudPreferences).Task.Unwrap());
        ContentRendered+=async(_,_)=>{await Modules.Ready;try{await ApplyCloudPreferences();}catch(Exception ex){PerformanceLog.Write("sync.preferences.failed",error:ex);}CloudSync.Start();};
    }
    public void ModulePreferencesChanged(){if(!applyingCloud)CloudSync?.SettingsChanged();}
    async Task CaptureModulePreferences()
    {
        if(deferredCloud)return;
        var registry=await Task.Run(Modules.Packages.Read);
        foreach(var record in registry.Modules)
        {
            var m=record.Manifest;await Task.Run(()=> { var key="module:"+m.Id;var value=SyncMerge.Serialize(new ModulePreference(m.Name,m.Kind,record.Enabled&&!record.Removed)); if(store.SyncMeta("observed:"+key)==null&&store.SyncEntries().Any(e=>e.Key==key))return;store.SetSyncValue(key,value,true);store.SyncMeta("observed:"+key,value); });
        }
        foreach(var (id,schema) in Modules.PortableSettings.ToArray())
        {
            foreach(var key in schema.Keys)
            {
                var value=await Modules.Context(id).ReadSettingAsync(key);await Task.Run(()=>store.SetSyncValue($"module-setting:{id}:{schema.Version}:{key}",value,true));
            }
        }
    }
    public async Task ApplyStoredCloudPreferences()=>await ApplyCloudPreferences();
    async Task ApplyCloudPreferences()
    {
        if(current is PlayerWindow){deferredCloud=true;return;}
        applyingCloud=true;
        try
        {
            AutoplayEnabled=store.Setting("autoplay_next")!="false";closeToTray=store.Setting("close_to_tray")=="true";
            var entries=await Task.Run(store.SyncEntries);var registry=await Task.Run(Modules.Packages.Read);
            // Only packages already installed and trusted locally can change enabled state.
            foreach(var e in entries.Where(e=>e.Key.StartsWith("module:")).OrderBy(e=>e.Stamp))
            {
                var id=e.Key[7..];var installed=registry.Modules.FirstOrDefault(r=>r.Manifest.Id==id&&!r.Removed);if(installed==null)continue;
                var preference=e.Deleted?null:JsonSerializer.Deserialize<ModulePreference>(e.Value!);bool enabled=preference?.Enabled==true;
                if(installed.Enabled!=enabled)await Task.Run(()=>Modules.Packages.Enable(id,enabled));
                store.SyncMeta("observed:"+e.Key,e.Value??"");
            }
            foreach(var (id,schema) in Modules.PortableSettings.ToArray())
            {
                bool changed=false;foreach(var key in schema.Keys)
                {
                    var entry=entries.FirstOrDefault(e=>e.Key==$"module-setting:{id}:{schema.Version}:{key}");if(entry==null)continue;var value=entry.Deleted?null:entry.Value;var context=Modules.Context(id);
                    if(await context.ReadSettingAsync(key)!=value){await context.WriteSettingAsync(key,value);changed=true;}
                }
                if(changed)Modules.Context(id).Publish(new AppEvent("settings.synced"));
            }
            await Modules.ApplyAppearanceAsync();await ReloadLibrary();current?.RefreshData();deferredCloud=false;
        }
        finally{applyingCloud=false;}
    }
    public async Task<IReadOnlyList<string>> MissingCloudModules()
    {
        var registry=await Task.Run(Modules.Packages.Read);var entries=await Task.Run(store.SyncEntries);
        return entries.Where(e=>e.Key.StartsWith("module:")&&!e.Deleted&&!registry.Modules.Any(r=>!r.Removed&&r.Manifest.Id==e.Key[7..])).Select(e=>JsonSerializer.Deserialize<ModulePreference>(e.Value!)!).Where(p=>p.Enabled).Select(p=>p.Name).ToArray();
    }
}
public sealed record ModulePreference(string Name,ModuleKind Kind,bool Enabled);
