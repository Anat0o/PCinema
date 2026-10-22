using System.Windows;
namespace PCinema;

public partial class App : Application
{
    bool reporting;
    protected override void OnStartup(StartupEventArgs e) { base.OnStartup(e); DispatcherUnhandledException += (_, a) => { System.IO.Directory.CreateDirectory(LibraryStore.DataDirectory); System.IO.File.AppendAllText(System.IO.Path.Combine(LibraryStore.DataDirectory, "errors.log"), DateTime.Now + " " + a.Exception + Environment.NewLine); a.Handled = true; if (reporting) { Shutdown(1); return; } reporting = true; MessageBox.Show(a.Exception.Message, "PCinema — ошибка"); reporting = false; Shutdown(1); }; }
}
