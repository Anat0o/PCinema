using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.Windows.Threading;
namespace PCinema;

public partial class MainWindow : Window
{
    readonly LibraryStore store = new();
    public PlaybackSession Playback {get;}=new();
    public ProgressWriter Progress {get;}
    public bool AutoplayEnabled {get;private set;}=true;
    bool shutdownPending,shutdownReady;
    readonly DispatcherTimer responsiveness=new(){Interval=TimeSpan.FromMilliseconds(50)};
    long lastPulse;
    readonly LibraryScanner scanner;
    readonly TmdbService tmdb;
    readonly LibraryViewModel vm;
    readonly Stack<Screen?> history = new();
    readonly DispatcherTimer reconcile = new() { Interval = TimeSpan.FromSeconds(30) };
    Screen? current;
    Action? cancelModal;
    bool metadataBusy, closed, refreshing;
    Rect previousBounds; WindowState previousState; bool previousTopmost; System.Windows.Shell.WindowChrome? previousChrome;
    public bool IsFullscreen { get; private set; }
    public MainWindow(bool safeMode=false)
    {
        Progress=new(store.SaveProgress);Progress.Failed+=()=>Dispatcher.BeginInvoke(()=>{if(vm!=null)vm.Status="Не удалось сохранить историю просмотра.";}); InitializeComponent(); Modules=new(this,safeMode); ContentRendered+=async(_,_)=>await Modules.StartAsync(); SourceInitialized+=(_,_)=>WindowBounds.Attach(this,()=>IsFullscreen); ContentRendered+=async(_,_)=>{try{var config=await Task.Run(()=>(Options:SubtitleAppearance.Load(store).Options(),Software:store.Setting("software_decoding")=="true",Auto:store.Setting("autoplay_next")!="false",Tray:store.Setting("close_to_tray")=="true"));AutoplayEnabled=config.Auto;closeToTray=config.Tray;await Playback.PrepareAsync(config.Options,config.Software,0);}catch(Exception ex){_=Task.Run(()=>PerformanceLog.Write("player.warmup.failed",error:ex));}}; KeyboardNavigation.SetTabNavigation(ModalLayer,KeyboardNavigationMode.Cycle); tmdb=new(store); scanner=new(store); vm=new(store); DataContext=vm; InitializeCloudSync();
        scanner.Status += s => Dispatcher.BeginInvoke(() => { if(!closed) vm.Status=s; });
        scanner.Updated += () => Dispatcher.BeginInvoke(async () => { if(closed) return; await ReloadLibrary(); if(current is not PlayerWindow)current?.RefreshData(); _=Enrich(); });
        Loaded += async (_,_) => { await ReloadLibrary(); await scanner.ScanAsync(); _=Enrich(); reconcile.Start(); };
        reconcile.Tick += async (_,_) => { await scanner.CheckSourcesAsync(); };
        lastPulse=System.Diagnostics.Stopwatch.GetTimestamp();
        responsiveness.Tick+=(_,_)=>{var now=System.Diagnostics.Stopwatch.GetTimestamp();var delay=System.Diagnostics.Stopwatch.GetElapsedTime(lastPulse,now).TotalMilliseconds-50;lastPulse=now;if(delay>50)_=Task.Run(()=>PerformanceLog.Write("ui.dispatch-delay",delay));};responsiveness.Start();
        Closing+=async(_,e)=>
        {
            if(shutdownReady)return;e.Cancel=true;if(!exitRequested&&closeToTray&&!shutdownPending&&HideToTray())return;if(shutdownPending)return;shutdownPending=true;current?.Leave();
            try{await preferenceWrites.DrainAsync();await Progress.FlushAsync();}catch{await Notice("Не удалось сохранить историю просмотра. Последние изменения могут быть потеряны.");}
            await CloudSync.StopAsync();await Modules.StopAsync();await Progress.FlushAsync();await Playback.DisposeAsync();((App)Application.Current).CleanExit=true;((App)Application.Current).RestartRequested=restartModules;shutdownReady=true;Close();
        };
        Closed += (_,_) => { closed=true;tray?.Dispose();responsiveness.Stop(); cancelModal?.Invoke(); reconcile.Stop(); _=Task.Run(scanner.Dispose); tmdb.Dispose(); };
    }
    public async Task ReloadLibrary()
    {
        if(refreshing || closed) return;
        refreshing=true;
        try { await Progress.DrainAsync(); var data=await Task.Run(store.LoadCatalog); if(!closed) vm.Reload(data); Modules?.Publish(new("library.changed")); }
        finally { refreshing=false; }
    }
    async Task Enrich()
    {
        if(metadataBusy || closed) return; metadataBusy=true;
        try { if(tmdb.Connected) { vm.Status="Ищем постеры и описания…"; await Task.Run(() => tmdb.AutoMatch()); await ReloadLibrary(); if(current is not PlayerWindow)current?.RefreshData(); } vm.Status="Библиотека обновлена"; }
        catch(Exception ex) { if(!closed) vm.Status="Локальная библиотека доступна. "+ex.Message; }
        finally { metadataBusy=false; }
    }
    public void Navigate(Screen screen)
    {
        if(current is PlayerWindow){current.Leave();current=null;}
        history.Push(current); current=screen; Present();
    }
    void Present()
    {
        PageHost.Content=current!=null&&presentations.TryGetValue(current,out var themed)?themed:current; PageHost.Visibility=current==null ? Visibility.Collapsed : Visibility.Visible;
        HomeHost.Visibility=current==null ? Visibility.Visible : Visibility.Collapsed;
        Navigation.Visibility=current is PlayerWindow ? Visibility.Collapsed : Visibility.Visible;
        BackButton.Visibility=history.Count>0 ? Visibility.Visible : Visibility.Collapsed;
        HomeButton.Tag=current==null ? "Active" : null;
        LibraryButton.Tag=current is LibraryScreen or SeriesWindow ? "Active" : null;
        HistoryButton.Tag=current is HistoryScreen ? "Active" : null;
        var account=current is SettingsScreen {Section:"Аккаунт и синхронизация"};
        SettingsButton.Tag=current is SettingsScreen && !account ? "Active" : null;
        AccountButton.Tag=account ? "Active" : null;
        ApplyModulePresentation();
        if(deferredCloud&&current is not PlayerWindow)_=ApplyCloudPreferences();
        if(current!=null) { Ui.Enter(current); current.Focus(); }
    }
    public void Back()
    {
        if(cancelModal!=null) { cancelModal(); return; }
        if(current is PlayerWindow p && p.ClosePanel()) return;
        if(IsPictureInPicture){ExitPictureInPicture();return;}
        if(IsFullscreen) { ToggleFullscreen(); return; }
        if(history.Count==0) return;
        current?.Leave(); current=history.Pop(); Present(); current?.RefreshData(); _=ReloadLibrary();
    }
    void BackClick(object sender,RoutedEventArgs e)=>Back();
    void GoHome(object sender,RoutedEventArgs e)
    {
        cancelModal?.Invoke(); current?.Leave(); history.Clear(); current=null; Present(); Scroller.ScrollToTop(); _=ReloadLibrary();
    }
    void GoHistory(object sender,RoutedEventArgs e){if(current is not HistoryScreen)Navigate(new HistoryScreen(this,store));}
    void GoLibrary(object sender,RoutedEventArgs e) { if(current is LibraryScreen) return; Navigate(new LibraryScreen(this,store)); }
    void AccountSettings(object sender,RoutedEventArgs e)=>OpenSettings("Аккаунт и синхронизация");
    void Settings(object sender,RoutedEventArgs e) => OpenSettings();
    public void OpenSettings(string section="Общие",string? moduleOwner=null,string? modulePage=null)=>Navigate(new SettingsScreen(this,store,tmdb,section,moduleOwner,modulePage));
    public void SetAutoplay(bool value)=>AutoplayEnabled=value;
    public void SettingsChanged() { _=scanner.ScanAsync(); _=ReloadLibrary(); _=Enrich(); }
    async void AddFolder(object sender,RoutedEventArgs e)
    {
        var d=new OpenFolderDialog {Title="Выберите папку с сериалами",Multiselect=true};
        if(d.ShowDialog(this)!=true) return;
        foreach(var path in d.FolderNames) store.AddFolder(path);
        await scanner.ScanAsync();
    }
    void OpenSeries(object sender,RoutedEventArgs e) { if((sender as FrameworkElement)?.DataContext is Series s) ShowSeries(s); }
    public void ShowSeries(Series s)=>Navigate(new SeriesWindow(this,store,tmdb,s));
    void HeroInfo(object sender,RoutedEventArgs e) { if(vm.Hero is {} s) ShowSeries(s); }
    void PlayHero(object sender,RoutedEventArgs e) { if(vm.Hero is {} s && PlaybackSelection.Continue(s) is {} ep) Play(s,ep); else AddFolder(sender,e); }
    public void Play(Series s,Episode ep)
    {
        if(current is PlayerWindow) return;
        Navigate(new PlayerWindow(this,store,s,ep));
    }
    public void ToggleFullscreen()
    {
        if(IsPictureInPicture)ExitPictureInPicture();
        if(!IsFullscreen) { previousBounds=WindowState==WindowState.Normal ? new Rect(Left,Top,ActualWidth,ActualHeight) : RestoreBounds; previousState=WindowState; previousTopmost=Topmost; previousChrome=System.Windows.Shell.WindowChrome.GetWindowChrome(this); IsFullscreen=true; WindowState=WindowState.Normal; System.Windows.Shell.WindowChrome.SetWindowChrome(this,null); ChromeRow.Height=new(0); Topmost=true; WindowState=WindowState.Maximized; IsFullscreen=true; }
        else { IsFullscreen=false; WindowState=WindowState.Normal; System.Windows.Shell.WindowChrome.SetWindowChrome(this,previousChrome); ChromeRow.Height=new(32); Topmost=previousTopmost; if(!previousBounds.IsEmpty){Left=previousBounds.Left;Top=previousBounds.Top;Width=previousBounds.Width;Height=previousBounds.Height;} WindowState=previousState; IsFullscreen=false; }
    }
    void KeyPressed(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.M && Keyboard.Modifiers==(ModifierKeys.Control|ModifierKeys.Shift)){OpenModules(this,new());e.Handled=true;return;}
        if(e.Key==Key.Escape) { Back(); e.Handled=true; }
        else if(current is PlayerWindow p && cancelModal==null) p.HandleKey(e);
    }
    void MinimizeWindow(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    void MaximizeWindow(object sender,RoutedEventArgs e){if(IsPictureInPicture){ExitPictureInPicture();return;}WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;}
    void CloseWindow(object sender,RoutedEventArgs e)=>Close();
    void DismissModal(object sender,RoutedEventArgs e)=>cancelModal?.Invoke();
    Task<T?> Modal<T>(FrameworkElement content,Action<Action<T?>> connect)
    {
        cancelModal?.Invoke();
        var done=new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restore=Keyboard.FocusedElement;
        void Finish(T? value) { cancelModal=null; ModalContent.Content=null; ModalLayer.Visibility=Visibility.Collapsed; done.TrySetResult(value); if(restore!=null) Keyboard.Focus(restore); }
        cancelModal=()=>Finish(default); connect(Finish); ModalContent.Content=content; ModalLayer.Visibility=Visibility.Visible; Ui.Enter(ModalLayer);
        return done.Task;
    }
    public Task<string?> Ask(string title,string value="")
    {
        var p=new StackPanel(); p.Children.Add(Ui.Text(title,22,true));
        var text=new TextBox {Text=value}; p.Children.Add(text);
        var save=new Button {Content="Сохранить",Style=(Style)FindResource("PrimaryButton"),HorizontalAlignment=HorizontalAlignment.Right}; p.Children.Add(save);
        var task=Modal<string>(p,finish=> { save.Click+=(_,_)=>finish(text.Text); text.KeyDown+=(_,e)=>{if(e.Key==Key.Enter) {finish(text.Text);e.Handled=true;}}; });
        text.Focus(); text.SelectAll(); return task;
    }
    public Task<T?> Choose<T>(string title,List<T> items,Func<T,string> label) where T:class
    {
        var p=new DockPanel(); var header=Ui.Text(items.Count==0 ? "Список пуст" : title,22,true); DockPanel.SetDock(header,Dock.Top); p.Children.Add(header);
        var b=new Button {Content="Выбрать",HorizontalAlignment=HorizontalAlignment.Right}; DockPanel.SetDock(b,Dock.Bottom);p.Children.Add(b);
        var list=new ListBox {Height=300,ItemsSource=items.Select(label).ToList()};p.Children.Add(list);
        return Modal<T>(p,finish=> { void Select() {if(list.SelectedIndex>=0) finish(items[list.SelectedIndex]);} b.Click+=(_,_)=>Select();list.MouseDoubleClick+=(_,_)=>Select(); });
    }
    public async Task Notice(string message)
    {
        var p=new StackPanel();p.Children.Add(Ui.Text(message,17));
        var b=new Button {Content="Понятно",HorizontalAlignment=HorizontalAlignment.Right};p.Children.Add(b);
        await Modal<string>(p,finish=>b.Click+=(_,_)=>finish("ok"));
    }
}
public static class PlaybackSelection
{
    public static Episode? Continue(Series s)=>s.Episodes.Where(e=>e.Available&&!e.Watched&&e.Position>0).OrderByDescending(e=>e.LastPlayed).FirstOrDefault() ?? s.Episodes.FirstOrDefault(e=>e.Available&&!e.Watched) ?? s.Episodes.FirstOrDefault(e=>e.Available);
}




