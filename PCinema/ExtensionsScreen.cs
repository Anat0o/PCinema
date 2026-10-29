using System.Windows;
using System.Windows.Controls;
namespace PCinema;

public sealed class ExtensionsScreen : Screen
{
    public ExtensionsScreen(MainWindow host):base(host)
    {
        var root=new StackPanel{Margin=new(32,100,32,24)};root.Children.Add(Ui.Text("Расширения",32,true));root.Children.Add(Ui.Text("Дополнительные функции вашей PCinema."));
        foreach(var (owner,page) in host.Modules.Pages.Where(p=>!p.Page.IsSettings))
        {
            var row=new DockPanel();var open=Ui.Button("Открыть",()=>host.OpenModulePage(owner,page.Id));DockPanel.SetDock(open,Dock.Right);row.Children.Add(open);var copy=new StackPanel();copy.Children.Add(Ui.Text(page.Label,18,true));var author=Ui.Text(owner,12);author.SetResourceReference(TextBlock.ForegroundProperty,"App.Muted");copy.Children.Add(author);row.Children.Add(copy);root.Children.Add(Ui.Panel(row));
        }
        if(!host.Modules.Pages.Any(p=>!p.Page.IsSettings)){root.Children.Add(Ui.Text("Здесь появятся рабочие страницы установленных расширений. Управление пакетами находится в настройках."));var settings=Ui.Button("Настройки модулей",()=>host.OpenSettings("Модули"));settings.HorizontalAlignment=HorizontalAlignment.Left;root.Children.Add(settings);}
        Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
}
