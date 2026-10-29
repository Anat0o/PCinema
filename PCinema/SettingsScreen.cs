using System.Windows;
using System.Windows.Controls;
using PCinema.Modules;
namespace PCinema;

public sealed class SettingsScreen:Screen
{
    public string Section {get;}
    readonly ContentControl body=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    public SettingsScreen(MainWindow host,LibraryStore store,TmdbService tmdb,string selected="Общие",string? owner=null,string? page=null):base(host)
    {
        Section=selected;
        var root=new DockPanel{Margin=new(32,72,32,16)};var tabs=new StackPanel{Width=190,Margin=new(0,0,24,0)};DockPanel.SetDock(tabs,Dock.Left);root.Children.Add(tabs);root.Children.Add(body);Content=root;
        foreach(var name in new[]{"Общие","Библиотека","Воспроизведение","Модули","Аккаунт и синхронизация"}){var button=Ui.Button(name,()=>host.OpenSettings(name),name==selected);button.HorizontalContentAlignment=HorizontalAlignment.Left;button.Content=new TextBlock{Text=name,TextWrapping=TextWrapping.Wrap};tabs.Children.Add(button);}
        if(selected=="Модули")
        {
            var definition=host.Modules.Pages.FirstOrDefault(x=>x.Owner==owner&&x.Page.Id==page&&x.Page.IsSettings);
            if(definition.Page!=null)
            {
                var panel=new DockPanel();var back=Ui.Button("← Все модули",()=>host.OpenSettings("Модули"));DockPanel.SetDock(back,Dock.Top);panel.Children.Add(back);
                try{panel.Children.Add(definition.Page.Create(host.Modules.Context(owner!)));}catch(Exception ex){host.Modules.Errors[owner!]="Ошибка настроек: "+ex.GetType().Name;panel.Children.Add(Ui.Text("Не удалось открыть настройки модуля."));}body.Content=panel;
            }
            else body.Content=new ModuleManagerScreen(host);
        }
        else if(selected=="Аккаунт и синхронизация")body.Content=new Sync.AccountPage(host);
        else body.Content=new SettingsWindow(host,store,tmdb,selected);
    }
    public override void RefreshData(){if(body.Content is Screen screen)screen.RefreshData();}
}
