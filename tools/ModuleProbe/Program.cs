using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PCinema;
using PCinema.Modules;
using PCinema.ModuleApi;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        // Integration test host: creates WPF trees without showing a desktop window.
        if(args.Length<2)throw new ArgumentException("Usage: ModuleProbe <isolated-data-directory> <packages-directory> [safe|disabled|failure|prepare]");
        Environment.SetEnvironmentVariable("PCINEMA_DATA_DIR",Path.GetFullPath(args[0]));
        var mode=args.ElementAtOrDefault(2)??"enabled";
        var packages=new ModulePackages(Path.Combine(Path.GetFullPath(args[0]),"Modules"));
        foreach(var file in Directory.GetFiles(Path.GetFullPath(args[1]),"*.pcmodule"))
        {
            var m=packages.Inspect(file);
            if(!packages.Read().Modules.Any(x=>x.Manifest.Id==m.Id))packages.Install(file,true);
            packages.Enable(m.Id,mode!="disabled");
        }
        if(mode=="prepare")return 0;
        if(mode=="failure")
        {
            var source=Path.Combine(args[0],"broken.pcmodule");
            using(var zip=ZipFile.Open(source,ZipArchiveMode.Create))
            {
                using(var w=new StreamWriter(zip.CreateEntry("manifest.json").Open()))w.Write(JsonSerializer.Serialize(new ModuleManifest("test.broken","Broken","Tests","1.0.0",ModuleKind.Feature,EntryAssembly:"fixture.dll",EntryType:"FailureModule"),ModulePackages.Json));
                zip.CreateEntryFromFile(typeof(FailureModule).Assembly.Location,"fixture.dll");
            }
            packages.Install(source,true);packages.Enable("test.broken",true);
        }
        var app=new App();app.InitializeComponent();ThemeValues.Reset();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var frame=new DispatcherFrame();Exception? failure=null;
        Dispatcher.CurrentDispatcher.BeginInvoke(async()=>
        {
            try
            {
                var host=new MainWindow(mode=="safe");var timer=Stopwatch.StartNew();await host.Modules.StartAsync();var startup=timer.Elapsed.TotalMilliseconds;
                if(mode is "safe" or "disabled")Check(host.Modules.Skin==null&&host.Modules.Pages.Count==0,"Extensions must be disabled");
                else
                {
                    Check(host.Modules.Skin!=null,"Skin loaded");Check(host.Modules.Pages.Count==2,"Timer pages registered");
                    if(mode=="failure")Check(host.Modules.Errors.ContainsKey("test.broken"),"Failure is reported");
                    else Check(host.Modules.Errors.Count==0,"No module startup errors");
                    foreach(var route in new[]{Route.Library,Route.History,Route.Home,Route.Modules,Route.Library,Route.Home})await host.ModuleNavigateAsync(route,null);
                    var content=new Grid();var view=host.Modules.CreateView(new(ViewKind.Player,content,host.Modules.Context("pcinema.compact")));
                    Check(!ReferenceEquals(content,view),"Alternative player controls");
                    host.OpenModulePage("pcinema.sleep-timer","sleep");host.Back();
                    await host.ModuleNavigateAsync(Route.Modules,null);
                    Check(!host.Modules.Errors.Any(x=>x.Key!="test.broken"),"View navigation succeeded");
                    var context=host.Modules.Context("pcinema.sleep-timer");await context.WriteSettingAsync("probe","saved");Check(await context.ReadSettingAsync("probe")=="saved","Own settings");
                    int events=0;var subscription=context.Subscribe(_=>events++);host.Modules.Publish(new("probe"));subscription.Dispose();host.Modules.Publish(new("probe"));Check(events==1,"Subscription disposal");
                }
                await host.Modules.StopAsync();await host.Progress.FlushAsync();await host.Playback.DisposeAsync();
                Console.WriteLine(JsonSerializer.Serialize(new{mode,startupMilliseconds=startup,errors=host.Modules.Errors,passed=true}));
            }
            catch(Exception ex){failure=ex;Console.Error.WriteLine(ex);}
            finally{frame.Continue=false;}
        });
        Dispatcher.PushFrame(frame);return failure==null?0:1;
    }
    static void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);}
}
