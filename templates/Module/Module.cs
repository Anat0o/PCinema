using System.Windows.Controls;
using PCinema.ModuleApi;
namespace MyModule;
public sealed class Module : IPCinemaModule
{
    public void Register(IModuleRegistration registration) => registration.AddPage(new("hello", "Мой модуль", context =>
    {
        var view = new StackPanel();
        view.SetResourceReference(TextBlock.FontFamilyProperty, ThemeKeys.Font);
        view.Children.Add(new TextBlock { Text = "Мой первый модуль", FontSize = 28 });
        var button = new Button { Content = "В библиотеку" };
        button.Click += async (_, _) => await context.NavigateAsync(Route.Library);
        view.Children.Add(button);
        return view;
    }));
    public Task StartAsync(IModuleContext context, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
