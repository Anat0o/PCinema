using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PCinema.ModuleApi;
namespace PCinema.Examples;
public sealed class SleepTimerModule:IPCinemaModule,IPortableModuleSettings
{
    public int PortableSettingsVersion=>1;
    public IReadOnlyCollection<string> PortableSettingKeys=>new[]{"minutes"};
    IModuleContext? context;readonly SleepCountdown timer=new();DateTimeOffset? due;
    public void Register(IModuleRegistration registration)
    {
        registration.AddPage(new("sleep","Таймер сна",_=>CreatePage()));
        registration.AddPage(new("settings","Настройки таймера сна",_=>CreatePage(),true));
        registration.AddSeriesCommand(new("sleep30","Таймер сна · 30 минут",(_,_,_)=>Arm(30)));
        registration.AddEpisodeCommand(new("sleep15","Таймер сна · 15 минут",(_,_,_)=>Arm(15)));
    }
    public Task StartAsync(IModuleContext context,CancellationToken cancellationToken){this.context=context;return Task.CompletedTask;}
    public async Task StopAsync(CancellationToken cancellationToken){due=null;await timer.StopAsync().WaitAsync(cancellationToken);}
    async Task Arm(int minutes)
    {
        await context!.WriteSettingAsync("minutes",minutes.ToString());
        due=DateTimeOffset.Now.AddMinutes(minutes);_=Observe(timer.Arm(TimeSpan.FromMinutes(minutes),async()=>{await context.SetPausedAsync(true);due=null;}));
    }
    async Task Observe(Task task)
    {
        try{await task;}
        catch(OperationCanceledException){}catch(Exception ex){context!.Log("timer.failed",ex);}
    }
    FrameworkElement CreatePage()
    {
        var root=new StackPanel{MaxWidth=600,HorizontalAlignment=HorizontalAlignment.Left};root.SetResourceReference(TextBlock.FontFamilyProperty,ThemeKeys.Font);
        root.Children.Add(new TextBlock{Text="Таймер сна",FontSize=30,Margin=new(0,0,0,18)});
        root.Children.Add(new TextBlock{Text="Через указанное время видео встанет на паузу. Таймер работает, пока PCinema запущена.",TextWrapping=TextWrapping.Wrap});
        var minutes=new TextBox{Text="30",Width=140,HorizontalAlignment=HorizontalAlignment.Left};root.Children.Add(minutes);
        var status=new TextBlock{Margin=new(0,16,0,0)};root.Children.Add(status);
        var start=new Button{Content="Запустить"};start.Click+=async(_,_)=>{try{if(int.TryParse(minutes.Text,out int n)&&n>=1&&n<=720)await Arm(n);else status.Text="Введите от 1 до 720 минут.";}catch(Exception ex){status.Text="Не удалось запустить таймер.";context!.Log("arm.failed",ex);}};root.Children.Add(start);
        var cancel=new Button{Content="Отменить"};cancel.Click+=async(_,_)=>{due=null;await Observe(timer.StopAsync());};root.Children.Add(cancel);
        var clock=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};clock.Tick+=(_,_)=>status.Text=due.HasValue?"Осталось: "+(due.Value-DateTimeOffset.Now).ToString(@"hh\:mm\:ss"):"Таймер выключен";
        root.Loaded+=async(_,_)=>{clock.Start();try{minutes.Text=await context!.ReadSettingAsync("minutes")??"30";}catch(Exception ex){context!.Log("settings.read",ex);}};root.Unloaded+=(_,_)=>clock.Stop();return root;
    }
}
