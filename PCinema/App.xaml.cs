using System.Windows;
using System.IO;
using PCinema.Modules;
namespace PCinema;

public partial class App : Application
{
    bool reporting;
    public bool CleanExit {get;set;}
    public bool RestartRequested {get;set;}
    string? sessionMarker;
    SingleInstance? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance=new SingleInstance(LibraryStore.DataDirectory);
        if(!instance.IsPrimary){instance.ActivateExisting();Shutdown();return;}
        DispatcherUnhandledException += (_, a) => { _=Task.Run(()=>PerformanceLog.Write("app.unhandled",error:a.Exception)); a.Handled = true; if (reporting) { Shutdown(1); return; } reporting = true; MessageBox.Show("Произошла ошибка приложения. Диагностика сохранена локально.", "PCinema — ошибка"); reporting = false; Shutdown(1); };
        ThemeValues.Reset();
        var moduleRoot=Path.Combine(LibraryStore.DataDirectory,"Modules");Directory.CreateDirectory(moduleRoot);sessionMarker=Path.Combine(moduleRoot,"session.running");
        bool safe=e.Args.Contains("--safe-mode");
        if(!safe&&File.Exists(sessionMarker))safe=MessageBox.Show("Предыдущий запуск завершился аварийно. Запустить PCinema без сторонних модулей и оформления?","Восстановление PCinema",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
        File.WriteAllText(sessionMarker,DateTime.UtcNow.ToString("O"));
        try{var packages=new ModulePackages(moduleRoot);foreach(var r in packages.Read().Modules){var clear=Path.Combine(moduleRoot,"clear-"+r.Manifest.Id);if(File.Exists(clear)){var data=packages.DataDirectory(r.Manifest.Id);if(Directory.Exists(data))Directory.Delete(data,true);File.Delete(clear);}if(r.Removed){var folder=Path.GetDirectoryName(packages.PackageDirectory(r))!;if(Directory.Exists(folder))Directory.Delete(folder,true);}}}catch(Exception ex){PerformanceLog.Write("module.cleanup.failed",error:ex);}
        var window=new MainWindow(safe);MainWindow=window;ShutdownMode=ShutdownMode.OnMainWindowClose;window.Show();
        instance.Listen(()=>{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(()=>window.RestoreFromTray());});
    }
    protected override void OnExit(ExitEventArgs e){if(CleanExit&&sessionMarker!=null)File.Delete(sessionMarker);instance?.Dispose();base.OnExit(e);if(RestartRequested)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true});}
}
