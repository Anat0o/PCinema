using System.Windows;
using System.Windows.Controls;
namespace PCinema.Sync;

public sealed class AccountPage:Screen
{
    readonly TextBlock state=Ui.Text(""),identity=Ui.Text(""),missing=Ui.Text("");readonly WrapPanel actions=new();readonly TextBlock monogram=new(){FontSize=22,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};readonly Button cancel;CancellationTokenSource? operation;
    public AccountPage(MainWindow host):base(host)
    {
        var panel=new StackPanel{Margin=new(16),HorizontalAlignment=HorizontalAlignment.Stretch};panel.Children.Add(Ui.Text("Аккаунт и синхронизация",28,true));
        var profile=new DockPanel();var avatar=new Border{Width=64,Height=64,CornerRadius=new(32),Margin=new(0,0,20,0),Child=monogram};avatar.SetResourceReference(Border.BackgroundProperty,"App.Hover");DockPanel.SetDock(avatar,Dock.Left);profile.Children.Add(avatar);identity.FontSize=18;identity.VerticalAlignment=VerticalAlignment.Center;profile.Children.Add(identity);panel.Children.Add(Ui.Panel(profile));
        var sync=new StackPanel();sync.Children.Add(Ui.Text("Состояние синхронизации",18,true));sync.Children.Add(state);sync.Children.Add(actions);panel.Children.Add(Ui.Panel(sync));
        var privacy=Ui.Text("История и переносимые предпочтения хранятся в скрытой папке PCinema в Google Drive. Видео, пути папок, токены и пакеты модулей остаются на устройстве.",13);privacy.SetResourceReference(TextBlock.ForegroundProperty,"App.Muted");panel.Children.Add(privacy);panel.Children.Add(missing);
        var auto=new CheckBox{Content="Автоматическая синхронизация",IsChecked=host.CloudSync.Automatic,Margin=new(0,16,0,16)};auto.SetResourceReference(ForegroundProperty,"App.Text");auto.Checked+=(_,_)=>host.CloudSync.SetAutomatic(true);auto.Unchecked+=(_,_)=>host.CloudSync.SetAutomatic(false);panel.Children.Add(auto);
        cancel=Ui.Button("Отменить текущий вход",()=>operation?.Cancel());cancel.HorizontalAlignment=HorizontalAlignment.Left;panel.Children.Add(cancel);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Loaded+=(_,_)=>{host.CloudSync.Changed+=OnChanged;Refresh();};Unloaded+=(_,_)=>{host.CloudSync.Changed-=OnChanged;operation?.Cancel();};
    }
    void OnChanged()=>Dispatcher.BeginInvoke(Refresh);
    void Refresh()
    {
        var service=Host.CloudSync;identity.Text=service.Account.Identity is {} account?account.Name+"\n"+account.Email:"Вход не выполнен";
        monogram.Text=service.Account.Identity is {} person&&!string.IsNullOrWhiteSpace(person.Name)?System.Globalization.StringInfo.GetNextTextElement(person.Name).ToUpperInvariant():"P";
        state.Text=service.Status+(service.LastSuccess is {} time&&DateTimeOffset.TryParse(time,out var date)?"\nПоследняя синхронизация: "+date.ToLocalTime().ToString("g"):"")+(service.Pending?"\nЕсть локальные изменения для отправки.":"");
        cancel.Visibility=service.Busy?Visibility.Visible:Visibility.Collapsed;
        actions.Children.Clear();actions.IsEnabled=!service.Busy;
        if(service.Account.Identity==null)actions.Children.Add(Ui.Button("Войти через Google",()=>_=Run(async token=>{await service.SignInAsync(token);await ChooseMode(token);}),true));
        else
        {
            actions.Children.Add(Ui.Button(service.RequiresChoice?"Выбрать способ подключения":"Синхронизировать сейчас",()=>_=Run(async token=>{if(service.RequiresChoice)await ChooseMode(token);else await service.SynchronizeAsync(token);}),true));
            actions.Children.Add(Ui.Button("Выйти",()=>_=Run(_=>service.SignOutAsync())));
        }
        _=RefreshMissing();
    }
    async Task RefreshMissing(){try{var names=await Host.MissingCloudModules();missing.Text=names.Count>0?"Для восстановления оформления или функций установите вручную: "+string.Join(", ",names):"";}catch{missing.Text="";}}
    async Task Run(Func<CancellationToken,Task> action)
    {
        operation?.Dispose();operation=new();try{await action(operation.Token);}catch(OperationCanceledException){state.Text="Действие отменено. Локальные данные сохранены.";}catch(Exception ex){state.Text=ex is InvalidOperationException or System.IO.InvalidDataException or System.Net.Http.HttpRequestException?ex.Message:"Не удалось подключить синхронизацию.";PerformanceLog.Write("sync.account.failed",error:ex);}finally{actions.IsEnabled=true;}
    }
    async Task ChooseMode(CancellationToken token)
    {
        var choices=new List<string>{"Объединить локальную и облачную историю","Восстановить из облака","Заменить облако данными этого ПК"};
        var selected=await Host.Choose("Как подключить этот компьютер?",choices,s=>s);if(selected==null)return;
        int index=choices.IndexOf(selected);if(index>0&&!await Host.Confirm(index==1?"Заменить синхронизируемую историю и предпочтения на этом ПК облачной копией? Локальная резервная копия будет сохранена. Видео и папки останутся на месте.":"Заменить облачную копию данными этого ПК? Другие компьютеры запросят выбор восстановления. Локальная резервная копия будет сохранена."))return;
        await Host.CloudSync.ConnectAsync((ConnectChoice)index,token);Refresh();
    }
}
