using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PCinema.ModuleApi;
using PCinema.Modules;
namespace PCinema;
public partial class MainWindow
{
    public ModuleRuntime Modules {get;private set;}=null!;
    FrameworkElement? customShell;FrameworkElement? homePresentation;
    readonly Dictionary<Screen,FrameworkElement> presentations=[];
    PlayerState modulePlayerState=new(null,false,0,0);
    public PlayerState ModulePlayerState=>Volatile.Read(ref modulePlayerState);
    public async Task<IReadOnlyList<SeriesInfo>> ModuleLibraryAsync(bool archived,CancellationToken token)
    {
        await Progress.DrainAsync();return await Task.Run<IReadOnlyList<SeriesInfo>>(()=>{token.ThrowIfCancellationRequested();return Array.AsReadOnly(store.LoadCatalog(archived).Select(s=>new SeriesInfo(s.Id,s.TmdbId,s.Title,s.Overview,s.Poster,s.Year,Array.AsReadOnly(s.Episodes.Select(e=>new EpisodeInfo(e.Id,s.Id,e.Season,e.Number,e.DisplayTitle,e.Available,e.Position,e.Duration,e.Watched,e.LastPlayed)).ToArray()))).ToArray());},token);
    }
    public async Task ModuleNavigateAsync(Route route,long? id)
    {
        if(current is PlayerWindow) {current.Leave();current=null;}
        switch(route)
        {
            case Route.Home:GoHome(this,new());break;
            case Route.Library:GoLibrary(this,new());break;
            case Route.History:GoHistory(this,new());break;
            case Route.Settings:Settings(this,new());break;
            case Route.Modules:OpenModules(this,new());break;
            case Route.Series:
                var item=(await Task.Run(()=>store.LoadCatalog(true))).FirstOrDefault(s=>s.Id==id);if(item!=null)ShowSeries(item);break;
        }
    }
    public async Task ModulePlayAsync(long seriesId,long episodeId)
    {
        var item=(await Task.Run(()=>store.LoadCatalog(true))).FirstOrDefault(s=>s.Id==seriesId);var ep=item?.Episodes.FirstOrDefault(e=>e.Id==episodeId);
        if(item==null||ep==null||!ep.Available){await Notice("Локальный файл недоступен.");return;}
        if(current is PlayerWindow){current.Leave();current=null;}Play(item,ep);
    }
    public void ModulePause(bool pause){if(current is PlayerWindow p)p.ModuleSetPause(pause);}
    public void ModuleSeek(long position){if(current is PlayerWindow p)p.ModuleSetPosition(position);}
    public void ModulePlayerOptions(){if(current is PlayerWindow p)p.ModuleShowOptions();}
    public void ModuleNextEpisode(){if(current is PlayerWindow p)p.ModuleNext();}
    public void ModuleVolume(int volume){if(current is PlayerWindow p)p.ModuleSetVolume(volume);}
    internal void ClearModulePlayer(){CloudSync?.RequestNow();var old=ModulePlayerState;Volatile.Write(ref modulePlayerState,new(null,false,0,0));if(old.EpisodeId.HasValue)Modules.Publish(new("playback.closed",EpisodeId:old.EpisodeId));}
    internal void PublishPlayerState(long episodeId,bool playing,long position,long duration)
    {
        var old=ModulePlayerState;Volatile.Write(ref modulePlayerState,new(episodeId,playing,position,duration));
        if(old.EpisodeId!=episodeId)Modules.Publish(new("playback.opened",EpisodeId:episodeId));
        if(old.IsPlaying&&!playing)CloudSync?.RequestNow();
        if(old.IsPlaying!=playing)Modules.Publish(new(playing?"playback.playing":"playback.paused",EpisodeId:episodeId));
    }
    void OpenModuleNavigation(object sender,RoutedEventArgs e)=>OpenSettings("Модули");
    void OpenModules(object sender,RoutedEventArgs e)=>OpenSettings("Модули");
    public void OpenModulePage(string owner,string id)
    {
        var definition=Modules.Pages.FirstOrDefault(p=>p.Owner==owner&&p.Page.Id==id);
        if(definition.Page==null)return;
        if(definition.Page.IsSettings){OpenSettings("Модули",owner,id);return;}
        _=Modules.ExecuteAsync(owner,()=>{if(current is PlayerWindow)current.Leave();Navigate(new ModulePageScreen(this,owner,definition.Page));return Task.CompletedTask;});
    }
    public void RefreshModuleShell()
    {
        PageHost.Content=null;foreach(var screen in presentations.Keys)Detach(screen);presentations.Clear();homePresentation=null;Present();
    }
    void ApplyModulePresentation()
    {
        if(Modules==null)return;
        bool protectedPage=current is ModuleManagerScreen or SettingsScreen or SettingsWindow||Modules.SafeMode;
        if(Modules.Skin!=null&&!protectedPage)
        {
            Navigation.Visibility=Visibility.Collapsed;
            if(current==null)
            {
                if(homePresentation==null){HomeHost.Content=null;homePresentation=Modules.CreateView(new(ViewKind.Home,Scroller,Modules.Context(Modules.SkinOwner!)));}
                HomeHost.Content=homePresentation;
            }
            else if(current is not PlayerWindow && current is not ModulePageScreen)
            {
                if(!presentations.TryGetValue(current,out var presentation))
                {
                    var kind=current is LibraryScreen?ViewKind.Library:current is HistoryScreen?ViewKind.History:current is SeriesWindow?ViewKind.Series:(ViewKind?)null;
                    if(kind.HasValue){PageHost.Content=null;presentation=Modules.CreateView(new(kind.Value,current,Modules.Context(Modules.SkinOwner!),(current as SeriesWindow)?.ModuleSeriesId));presentations[current]=presentation;}
                }
                if(presentation!=null)PageHost.Content=presentation;
            }
            if(customShell==null)
            {
                Shell.Children.Remove(Workspace);
                try{customShell=Modules.Skin.CreateShell(new(Workspace,Modules.Context(Modules.SkinOwner!)));if(customShell==null)throw new InvalidOperationException();}
                catch(Exception ex){Modules.Errors[Modules.SkinOwner!]="Ошибка оболочки: "+ex.GetType().Name;Detach(Workspace);customShell=Workspace;Navigation.Visibility=current is PlayerWindow?Visibility.Collapsed:Visibility.Visible;}
                Grid.SetRow(customShell,1);Shell.Children.Add(customShell);
            }
        }
        else
        {
            if(customShell!=null){Shell.Children.Remove(customShell);if(customShell is ContentControl c)c.Content=null;Detach(Workspace);customShell=null;Grid.SetRow(Workspace,1);Shell.Children.Add(Workspace);}
            if(homePresentation!=null){HomeHost.Content=null;Detach(Scroller);HomeHost.Content=Scroller;homePresentation=null;}
        }
    }
    static void Detach(FrameworkElement element){if(element.Parent is Panel p)p.Children.Remove(element);else if(element.Parent is ContentControl c)c.Content=null;else if(element.Parent is Decorator d)d.Child=null;}
    internal static void DetachModuleView(FrameworkElement element)=>Detach(element);
    public async Task RestartForModules()
    {
        if(!await Confirm("Перезапустить PCinema? История будет сохранена."))return;
        restartModules=true;exitRequested=true;Close();
    }
    public async Task<bool> Confirm(string text)
    {
        var panel=new DockPanel();var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var yes=new Button{Content="Подтвердить"};var no=new Button{Content="Отмена"};buttons.Children.Add(no);buttons.Children.Add(yes);DockPanel.SetDock(buttons,Dock.Bottom);panel.Children.Add(buttons);
        panel.Children.Add(new ScrollViewer{MaxHeight=430,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=Ui.Text(text,17)});
        return await Modal<string>(panel,finish=>{yes.Click+=(_,_)=>finish("yes");no.Click+=(_,_)=>finish(null);})=="yes";
    }
    bool restartModules;
}
public sealed class ModulePageScreen:Screen
{
    readonly string owner;readonly ModulePage page;
    public ModulePageScreen(MainWindow host,string owner,ModulePage page):base(host){this.owner=owner;this.page=page;Content=new Border{Padding=new(36,72,36,24),Child=page.Create(host.Modules.Context(owner))};}
    // Pages can be reached again through Back. Use Loaded/Unloaded for view subscriptions;
    // module-wide resources belong to StopAsync.
}
