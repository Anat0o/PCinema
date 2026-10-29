using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PCinema.ModuleApi;
namespace PCinema.Examples;
public sealed class CompactModule:IPCinemaModule,IModuleSkin
{
    public IReadOnlyDictionary<string,string> ThemeTokens=>new Dictionary<string,string>{{ThemeKeys.Panel,"#1C242D"},{ThemeKeys.Accent,"#9DE4C6"},{ThemeKeys.Radius,"10"}};
    public void Register(IModuleRegistration registration)=>registration.SetSkin(this);
    public Task StartAsync(IModuleContext context,CancellationToken token)=>Task.CompletedTask;
    public Task StopAsync(CancellationToken token)=>Task.CompletedTask;
    static Button Button(string text,Func<Task> action){var b=new Button{Content=text,Padding=new(14,8,14,8),Margin=new(0,3,0,3),HorizontalContentAlignment=HorizontalAlignment.Left};b.Click+=async(_,_)=>{try{await action();}catch{}};return b;}
    public FrameworkElement CreateShell(ShellContext c)
    {
        var dock=new DockPanel();var nav=new StackPanel{Width=165,Margin=new(12)};nav.Children.Add(new TextBlock{Text="PCINEMA\nCOMPACT",FontSize=19,FontWeight=FontWeights.Bold,Margin=new(8,12,0,24)});
        nav.Children.Add(Button("← Назад",c.Host.GoBackAsync));
        foreach(var entry in new[]{("Главная",Route.Home),("Библиотека",Route.Library),("История",Route.History),("Настройки",Route.Settings)})nav.Children.Add(Button(entry.Item1,()=>c.Host.NavigateAsync(entry.Item2)));
        DockPanel.SetDock(nav,Dock.Left);dock.Children.Add(nav);dock.Children.Add(c.Content);dock.SetResourceReference(TextBlock.FontFamilyProperty,ThemeKeys.Font);
        var clock=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(350)};clock.Tick+=(_,_)=>nav.Visibility=c.Host.Player.EpisodeId.HasValue?Visibility.Collapsed:Visibility.Visible;dock.Loaded+=(_,_)=>clock.Start();dock.Unloaded+=(_,_)=>clock.Stop();return dock;
    }
    public FrameworkElement CreateView(ViewContext c)
    {
        if(c.Kind==ViewKind.Player)return Player(c);
        var root=new DockPanel{Margin=new(24,20,24,24)};
        var title=new TextBlock{Text=c.Kind switch{ViewKind.Home=>"Продолжить просмотр",ViewKind.History=>"История",ViewKind.Series=>"Сериал",_=>"Библиотека"},FontSize=28,Margin=new(0,0,0,16)};DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);
        var tools=new StackPanel();var filter=new TextBox{ToolTip="Поиск",Margin=new(0,0,0,12)};tools.Children.Add(new TextBlock{Text="Поиск",Margin=new(0,0,0,4)});tools.Children.Add(filter);
        // Built-in tools remain available without duplicating metadata/editing logic in the skin.
        var details=new Expander{Header="Все действия и стандартный вид",Margin=new(0,0,0,10),Content=c.BuiltInView};details.SetResourceReference(Control.ForegroundProperty,ThemeKeys.Text);tools.Children.Add(details);DockPanel.SetDock(tools,Dock.Bottom);root.Children.Add(tools);
        var rows=new ListBox{HorizontalContentAlignment=HorizontalAlignment.Stretch};root.Children.Add(rows);IReadOnlyList<SeriesInfo> data=[];bool active=false;IDisposable? subscription=null;
        void Fill()
        {
            rows.Items.Clear();foreach(var s in data.Where(s=>s.Title.Contains(filter.Text,StringComparison.OrdinalIgnoreCase)))
            {
                var panel=new StackPanel();panel.Children.Add(Button(s.Title,()=>c.Host.NavigateAsync(Route.Series,s.Id)));
                if(c.Kind==ViewKind.Series)
                {
                    panel.Children.Add(new TextBlock{Text=s.Overview,TextWrapping=TextWrapping.Wrap,Margin=new(0,8,0,14)});
                    foreach(var e in s.Episodes.OrderBy(e=>e.Season).ThenBy(e=>e.Number))
                    {
                        var button=Button($"S{e.Season:00} · E{e.Number:00}   {e.Title}   {(e.Watched?"✓":"")}",()=>c.Host.PlayAsync(s.Id,e.Id));button.IsEnabled=e.Available;panel.Children.Add(button);
                    }
                }
                else
                {
                    var last=s.Episodes.Where(e=>!string.IsNullOrEmpty(e.LastPlayed)).OrderByDescending(e=>e.LastPlayed).FirstOrDefault();
                    panel.Children.Add(new TextBlock{Text=last!=null?$"Последний просмотр: сезон {last.Season}, серия {last.Number}":$"Серий: {s.Episodes.Count}",Margin=new(14,0,0,8)});
                    var next=s.Episodes.Where(e=>e.Available).OrderByDescending(e=>e.LastPlayed).FirstOrDefault();if(next!=null)panel.Children.Add(Button("Продолжить",()=>c.Host.PlayAsync(s.Id,next.Id)));
                }
                var border=new Border{Child=panel,Padding=new(12),Margin=new(0,0,0,8),CornerRadius=new(10)};border.SetResourceReference(Border.BackgroundProperty,ThemeKeys.Panel);rows.Items.Add(border);
            }
        }
        async Task Reload()
        {
            try{var result=c.Kind==ViewKind.History||c.Kind==ViewKind.Home?await c.Host.GetHistoryAsync():await c.Host.GetLibraryAsync(true);if(!active)return;data=c.Kind==ViewKind.Series?result.Where(x=>x.Id==c.SeriesId).ToArray():result;Fill();}catch(Exception ex){c.Host.Log("view.load",ex);}
        }
        filter.TextChanged+=(_,_)=>Fill();root.Loaded+=async(_,_)=>{active=true;subscription?.Dispose();subscription=c.Host.Subscribe(e=>{if(e.Name=="library.changed")_=Reload();});await Reload();};root.Unloaded+=(_,_)=>{active=false;subscription?.Dispose();};return root;
    }
    static FrameworkElement Player(ViewContext c)
    {
        var root=new Grid();c.BuiltInView.Visibility=Visibility.Collapsed;root.Children.Add(c.BuiltInView);
        var bar=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom,Margin=new(20,0,20,18)};root.Children.Add(bar);
        var position=new Slider{Minimum=0,Maximum=1};bar.Children.Add(position);bool dragging=false;
        async void Commit(){if(!dragging)return;dragging=false;try{await c.Host.SeekAsync((long)position.Value);}catch(Exception ex){c.Host.Log("seek.failed",ex);}}
        position.PreviewMouseLeftButtonDown+=(_,_)=>dragging=true;position.PreviewMouseLeftButtonUp+=(_,_)=>Commit();position.LostMouseCapture+=(_,_)=>Commit();
        var buttons=new WrapPanel();bar.Children.Add(buttons);
        buttons.Children.Add(Button("← Библиотека",()=>c.Host.NavigateAsync(Route.Library)));
        buttons.Children.Add(Button("−10",()=>c.Host.SeekAsync(c.Host.Player.Position-10000)));
        buttons.Children.Add(Button("Пауза / Смотреть",()=>c.Host.SetPausedAsync(c.Host.Player.IsPlaying)));
        buttons.Children.Add(Button("+10",()=>c.Host.SeekAsync(c.Host.Player.Position+10000)));
        buttons.Children.Add(Button("Звук и субтитры",()=>c.Host.ShowPlayerOptionsAsync()));buttons.Children.Add(Button("Полный экран",()=>c.Host.ToggleFullscreenAsync()));
        buttons.Children.Add(Button("PiP",c.Host.TogglePictureInPictureAsync));buttons.Children.Add(Button("Следующая серия",c.Host.NextEpisodeAsync));
        var volume=new Slider{Minimum=0,Maximum=100,Value=80,Width=100,ToolTip="Громкость",Margin=new(8)};volume.ValueChanged+=async(_,_)=>{try{await c.Host.SetVolumeAsync((int)volume.Value);}catch(Exception ex){c.Host.Log("volume.failed",ex);}};buttons.Children.Add(volume);
        var clock=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(250)};clock.Tick+=(_,_)=>{var state=c.Host.Player;position.Maximum=Math.Max(1,state.Duration);if(!dragging)position.Value=state.Position;};root.Loaded+=(_,_)=>clock.Start();root.Unloaded+=(_,_)=>clock.Stop();return root;
    }
}
