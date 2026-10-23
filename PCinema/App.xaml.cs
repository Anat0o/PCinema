using System.Windows;
namespace PCinema;

public partial class App : Application
{
    bool reporting;
    protected override void OnStartup(StartupEventArgs e) { base.OnStartup(e); DispatcherUnhandledException += (_, a) => { _=Task.Run(()=>PerformanceLog.Write("app.unhandled",error:a.Exception)); a.Handled = true; if (reporting) { Shutdown(1); return; } reporting = true; MessageBox.Show("Произошла ошибка приложения. Диагностика сохранена локально.", "PCinema — ошибка"); reporting = false; Shutdown(1); }; }
}
