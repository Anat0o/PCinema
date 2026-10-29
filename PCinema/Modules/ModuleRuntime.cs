using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using PCinema.ModuleApi;
namespace PCinema.Modules;

public sealed class ModuleRuntime
{
    public ModulePackages Packages {get;}
    public Dictionary<string,string> Errors {get;}=[];
    public List<(string Owner,ModulePage Page)> Pages {get;}=[];
    public List<(string Owner,ModuleCommand Command)> SeriesCommands {get;}=[];
    public List<(string Owner,ModuleCommand Command)> EpisodeCommands {get;}=[];
    public Dictionary<string,(int Version,string[] Keys)> PortableSettings {get;}=[];
    readonly List<(string Id,IPCinemaModule Module,ModuleContext Context,ModuleLoadContext Load,CancellationTokenSource Lifetime)> active=[];
    readonly MainWindow host;
    readonly CancellationTokenSource lifetime=new();
    readonly Dictionary<string,string> running=[];
    readonly IModuleSkin builtIn=new BuiltInSkin();
    public IModuleSkin? Skin {get;private set;}
    public string? SkinOwner {get;private set;}
    public bool SafeMode {get;}
    public bool Started {get;private set;}
    readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Ready=>ready.Task;
    public bool Stopping {get;private set;}
    public ModuleRuntime(MainWindow host,bool safeMode){this.host=host;SafeMode=safeMode;Packages=new(Path.Combine(LibraryStore.DataDirectory,"Modules"));}
    public async Task StartAsync()
    {
        if(Started)return;Started=true;if(SafeMode){ready.TrySetResult();return;}
        try
        {
            var registry=await Task.Run(Packages.Read);
            foreach(var record in registry.Modules.Where(r=>r.Enabled&&!r.Removed&&r.Manifest.Kind is ModuleKind.Feature or ModuleKind.Skin))
            {
                if(Stopping)break;
                ModuleContext? context=null;IPCinemaModule? module=null;ModuleLoadContext? load=null;
                var moduleLifetime=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                var elapsed=System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    var m=record.Manifest;load=new ModuleLoadContext(Packages.PackageDirectory(record));
                    module=await Task.Run(()=>
                    {
                        var assembly=load.LoadFromAssemblyPath(ModulePackages.SafePath(Packages.PackageDirectory(record),m.EntryAssembly!));
                        return (IPCinemaModule)(Activator.CreateInstance(assembly.GetType(m.EntryType!,true)!)??throw new InvalidDataException("Не удалось создать модуль."));
                    });
                    context=new ModuleContext(m.Id,host,this);var registration=new Registration();module.Register(registration);
                    if(m.Kind!=ModuleKind.Skin&&registration.Skin!=null)throw new InvalidDataException("Рескин должен иметь тип Skin.");
                    var local=module;await Task.Run(()=>local.StartAsync(context,moduleLifetime.Token)).WaitAsync(TimeSpan.FromSeconds(15),lifetime.Token);
                    if(Stopping)throw new OperationCanceledException();
                    if(module is IPortableModuleSettings portable&&portable.PortableSettingsVersion>0)PortableSettings[m.Id]=(portable.PortableSettingsVersion,portable.PortableSettingKeys.Where(k=>System.Text.RegularExpressions.Regex.IsMatch(k,@"\A[a-zA-Z0-9_.-]{1,128}\z")).Distinct().ToArray());
                    Pages.AddRange(registration.Pages.Select(p=>(m.Id,p)));SeriesCommands.AddRange(registration.Series.Select(c=>(m.Id,c)));EpisodeCommands.AddRange(registration.Episodes.Select(c=>(m.Id,c)));
                    active.Add((m.Id,module,context,load,moduleLifetime));running[m.Id]=record.Version;
                    PerformanceLog.Write("module.start."+m.Id,elapsed.Elapsed.TotalMilliseconds);
                    if(registration.Skin!=null){Skin=registration.Skin;SkinOwner=m.Id;}
                }
                catch(Exception ex)
                {
                    try{moduleLifetime.Cancel();}catch(Exception cancellationError){PerformanceLog.Write("module.cancel."+record.Manifest.Id,error:cancellationError);}Errors[record.Manifest.Id]="Ошибка запуска: "+ex.GetType().Name;context?.Dispose();
                    if(module!=null){var failed=module;_=Task.Run(async()=>{try{await failed.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));}catch{}});}
                    load?.Unload();PerformanceLog.Write("module.start."+record.Manifest.Id,error:ex);
                }
            }
            if(!Stopping){await ApplyAppearanceAsync();if(!Stopping)host.RefreshModuleShell();}
        }
        catch(Exception ex){Errors["registry"]="Не удалось загрузить реестр: "+ex.GetType().Name;}
        finally { ready.TrySetResult(); }
    }
    public ModuleContext Context(string id)=>active.FirstOrDefault(x=>x.Id==id).Context??new ModuleContext(id,host,this);
    public bool NeedsRestart(ModuleRecord r)=>r.Manifest.Kind is ModuleKind.Feature or ModuleKind.Skin&&(running.GetValueOrDefault(r.Manifest.Id)!=(r.Enabled&&!r.Removed?r.Version:null));
    public async Task ApplyAppearanceAsync()
    {
        ThemeValues.Reset();if(SafeMode)return;
        if(Skin!=null)try{ThemeValues.Apply(Skin.ThemeTokens);}catch(Exception ex){Errors[SkinOwner!]="Некорректные ресурсы рескина: "+ex.GetType().Name;}
        var registry=await Task.Run(Packages.Read);
        foreach(var record in registry.Modules.Where(r=>r.Enabled&&!r.Removed&&r.Manifest.Kind is ModuleKind.Theme or ModuleKind.Font))
        {
            try
            {
                var dir=Packages.PackageDirectory(record);var m=record.Manifest;
                if(m.Kind==ModuleKind.Theme){var values=await Task.Run(()=>JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(ModulePackages.SafePath(dir,m.ThemeFile!)),ModulePackages.Json)!);ThemeValues.Apply(values);}
                else
                {
                    var path=ModulePackages.SafePath(dir,m.FontFile!);var font=new FontFamily(new Uri(Path.GetDirectoryName(path)!+Path.DirectorySeparatorChar),"./#"+m.FontFamily);
                    if(!font.GetTypefaces().Any())throw new InvalidDataException("Шрифт не загрузился.");Application.Current.Resources[ThemeKeys.Font]=font;
                }
                Errors.Remove(m.Id);
            }
            catch(Exception ex){Errors[record.Manifest.Id]="Ошибка оформления: "+ex.GetType().Name;}
        }
    }
    public FrameworkElement CreateView(ViewContext context)
    {
        if(Skin==null)return builtIn.CreateView(context);
        try{return Skin.CreateView(context)??context.BuiltInView;}
        catch(Exception ex){Errors[SkinOwner!]="Ошибка представления: "+ex.GetType().Name;PerformanceLog.Write("module.view."+SkinOwner,error:ex);MainWindow.DetachModuleView(context.BuiltInView);context.BuiltInView.Visibility=Visibility.Visible;return context.BuiltInView;}
    }
    public async Task ExecuteAsync(string id,Func<Task> call)
    {
        try{await call();}catch(Exception ex){Errors[id]="Ошибка действия: "+ex.GetType().Name;PerformanceLog.Write("module.action."+id,error:ex);await host.Notice("Ошибка модуля «"+id+"». Подробности в разделе «Модули».");}
    }
    public void Publish(AppEvent e){foreach(var item in active.ToArray())item.Context.Publish(e);}
    public async Task StopAsync()
    {
        if(Stopping)return;Stopping=true;try{lifetime.Cancel();}catch(Exception ex){PerformanceLog.Write("module.cancel",error:ex);}
        foreach(var item in active.AsEnumerable().Reverse())
        {
            try{using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(5));await Task.Run(()=>item.Module.StopAsync(stop.Token)).WaitAsync(stop.Token);}
            catch(Exception ex){PerformanceLog.Write("module.stop."+item.Id,error:ex);}
            item.Context.Dispose();item.Lifetime.Dispose();
        }
    }
    sealed class Registration : IModuleRegistration
    {
        public List<ModulePage> Pages=[];public List<ModuleCommand> Series=[];public List<ModuleCommand> Episodes=[];public IModuleSkin? Skin;
        public void AddPage(ModulePage page){if(string.IsNullOrWhiteSpace(page.Id)||Pages.Any(p=>p.Id==page.Id))throw new InvalidDataException("Повторяющийся ID страницы.");Pages.Add(page);}
        public void AddSeriesCommand(ModuleCommand command)=>Series.Add(command);
        public void AddEpisodeCommand(ModuleCommand command)=>Episodes.Add(command);
        public void SetSkin(IModuleSkin skin){if(Skin!=null)throw new InvalidOperationException();Skin=skin;}
    }
    sealed class ModuleLoadContext(string directory):AssemblyLoadContext(isCollectible:true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            if(name.Name==typeof(IPCinemaModule).Assembly.GetName().Name)return typeof(IPCinemaModule).Assembly;
            var file=Path.Combine(directory,name.Name+".dll");return File.Exists(file)?LoadFromAssemblyPath(file):null;
        }
        protected override nint LoadUnmanagedDll(string name){var path=Path.Combine(directory,Path.GetFileName(name));return File.Exists(path)?LoadUnmanagedDllFromPath(path):0;}
    }
}
