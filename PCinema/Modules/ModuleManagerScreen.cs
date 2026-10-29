using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using PCinema.ModuleApi;
using System.IO;
namespace PCinema.Modules;
public sealed class ModuleManagerScreen:Screen
{
    readonly StackPanel rows=new();readonly TextBlock status=Ui.Text("");bool busy;
    public ModuleManagerScreen(MainWindow host):base(host)
    {
        SetResourceReference(FontFamilyProperty,ThemeKeys.Font);Background=System.Windows.Media.Brushes.Transparent;
        foreach(var (key,value) in ThemeValues.Defaults)
        {
            if(value.StartsWith('#'))Resources[key]=new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));
        }
        Resources[ThemeKeys.FontSize]=14d;Resources[ThemeKeys.Radius]=new CornerRadius(16);Resources["App.ButtonRadius"]=new CornerRadius(12);
        var root=new StackPanel{Margin=new(16,12,16,24)};root.Children.Add(Ui.Text("Модули",32,true));
        root.Children.Add(Ui.Text(host.Modules.SafeMode?"Безопасный режим: сторонние модули и оформление отключены на этот запуск.":"Устанавливайте пакеты .pcmodule. Программные модули выполняются с правами приложения."));
        var actions=new WrapPanel();actions.Children.Add(Ui.Button("Установить из файла…",()=>_=Install(),true));actions.Children.Add(Ui.Button("Перезапустить",()=>_=Host.RestartForModules()));root.Children.Add(actions);root.Children.Add(status);root.Children.Add(rows);Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Loaded+=(_,_)=>_=Refresh();
    }
    async Task Run(Func<Task> action){if(busy)return;busy=true;IsEnabled=false;try{await action();}catch(Exception ex){status.Text=ex.Message;}finally{busy=false;IsEnabled=true;}}
    async Task Install()=>await Run(async()=>
    {
        var dialog=new OpenFileDialog{Filter="Модуль PCinema|*.pcmodule"};if(dialog.ShowDialog(Host)!=true)return;
        var manifest=await Task.Run(()=>Host.Modules.Packages.Inspect(dialog.FileName));
        var code=manifest.Kind is ModuleKind.Feature or ModuleKind.Skin;
        var description=$"{manifest.Name} · {manifest.Version}\nАвтор: {manifest.Author}\n{string.Join(", ",manifest.Capabilities??[])}\n"+(code?"Этот модуль выполняет код с правами PCinema. Возможности не ограничены песочницей. Устанавливайте только если доверяете автору.":"Пакет оформления без исполняемого кода.");
        if(!await Host.Confirm(description+"\nУстановить?"))return;
        await Task.Run(()=>Host.Modules.Packages.Install(dialog.FileName,code));await Host.ApplyStoredCloudPreferences();Host.ModulePreferencesChanged();await Host.Modules.ApplyAppearanceAsync();status.Text="Пакет установлен. Новый модуль можно включить ниже.";await Refresh();
    });
    async Task ShowPages(bool settings)
    {
        var pages=Host.Modules.Pages.Where(p=>p.Page.IsSettings==settings).ToList();var selected=await Host.Choose("Страницы модулей",pages.Select(p=>p.Owner+" / "+p.Page.Id).ToList(),id=>{var p=pages.First(x=>x.Owner+" / "+x.Page.Id==id);return p.Page.Label;});if(selected!=null){var p=pages.First(x=>x.Owner+" / "+x.Page.Id==selected);Host.OpenModulePage(p.Owner,p.Page.Id);}
    }
    async Task Refresh()
    {
        try
        {
            var registry=await Task.Run(Host.Modules.Packages.Read);rows.Children.Clear();
            foreach(var missing in await Host.MissingCloudModules())rows.Children.Add(Ui.Text("Требуется ручная установка: "+missing));
            foreach(var (id,error) in Host.Modules.Errors)rows.Children.Add(Ui.Text(id+": "+error));
            foreach(var record in registry.Modules.Where(r=>!r.Removed))
            {
                var m=record.Manifest;var panel=new StackPanel();panel.Children.Add(Ui.Text(m.Name+" · "+record.Version,20,true));panel.Children.Add(Ui.Text(m.Author+" · "+m.Kind+" · "+(record.Enabled?"Включён":"Выключен")+(Host.Modules.NeedsRestart(record)?" · Нужен перезапуск":"")));
                var workingPages=new WrapPanel();
                foreach(var page in Host.Modules.Pages.Where(p=>p.Owner==m.Id&&!p.Page.IsSettings))
                    workingPages.Children.Add(Ui.Button("Открыть: "+page.Page.Label,()=>Host.OpenModulePage(m.Id,page.Page.Id)));
                if(workingPages.Children.Count>0)panel.Children.Add(workingPages);
                var bar=new WrapPanel();
                bar.Children.Add(Ui.Button(record.Enabled?"Выключить":"Включить",()=>_=Run(async()=>{await Task.Run(()=>Host.Modules.Packages.Enable(m.Id,!record.Enabled));Host.ModulePreferencesChanged();await Host.Modules.ApplyAppearanceAsync();await Refresh();})));
                if(record.PreviousVersion!=null)bar.Children.Add(Ui.Button("Откатить к "+record.PreviousVersion,()=>_=Run(async()=>{await Task.Run(()=>Host.Modules.Packages.Rollback(m.Id));Host.ModulePreferencesChanged();await Host.Modules.ApplyAppearanceAsync();await Refresh();})));
                bar.Children.Add(Ui.Button("Удалить",()=>_=Run(async()=>{if(!await Host.Confirm("Удалить «"+m.Name+"»? Настройки сохранятся. Программный модуль окончательно отключится после перезапуска."))return;await Task.Run(()=>Host.Modules.Packages.Remove(m.Id));Host.ModulePreferencesChanged();await Host.Modules.ApplyAppearanceAsync();await Refresh();})));
                bar.Children.Add(Ui.Button("Очистить данные",()=>_=Run(async()=>{if(!await Host.Confirm("Очистить настройки и данные только этого модуля при следующем запуске?"))return;await Task.Run(()=>File.WriteAllText(Path.Combine(Host.Modules.Packages.Root,"clear-"+m.Id),""));status.Text="Данные будут очищены при следующем запуске.";})));
                foreach(var page in Host.Modules.Pages.Where(p=>p.Owner==m.Id&&p.Page.IsSettings))bar.Children.Add(Ui.Button(page.Page.Label,()=>Host.OpenModulePage(m.Id,page.Page.Id)));
                var buttons=bar.Children.OfType<Button>().ToArray();bar.Children.Clear();bar.Children.Add(buttons[0]);var more=Ui.Button("⋯",()=>{});more.ToolTip="Параметры и действия модуля";more.Click+=(_,_)=>{var menu=new ContextMenu{PlacementTarget=more};foreach(var action in buttons.Skip(1)){var item=new MenuItem{Header=action.Content};item.Click+=(_,_)=>action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));menu.Items.Add(item);}menu.IsOpen=true;};bar.Children.Add(more);
                var line=new DockPanel();bar.MaxWidth=180;DockPanel.SetDock(bar,Dock.Right);line.Children.Add(bar);var icon=Ui.Text("◇",24,true);icon.Margin=new(0,0,16,0);DockPanel.SetDock(icon,Dock.Left);line.Children.Add(icon);line.Children.Add(panel);rows.Children.Add(Ui.Panel(line));
            }
            if(registry.Modules.All(r=>r.Removed))rows.Children.Add(Ui.Text("Модули ещё не установлены. Выберите файл .pcmodule."));
        }
        catch(Exception ex)
        {
            status.Text="Не удалось открыть модули: "+ex.Message;rows.Children.Clear();
            rows.Children.Add(Ui.Button("Восстановить пустой реестр",()=>_=Run(async()=>
            {
                if(!await Host.Confirm("Создать пустой реестр модулей? Старый реестр будет сохранён в резервную копию. Библиотека, история и данные модулей сохранятся. Потребуется перезапуск и повторная установка пакетов."))return;
                await Task.Run(Host.Modules.Packages.ResetRegistry);status.Text="Реестр восстановлен. Перезапустите приложение.";
            })));
        }
    }
}
