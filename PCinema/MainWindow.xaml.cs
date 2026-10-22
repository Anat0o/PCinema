using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using System.Windows.Threading;
namespace PCinema;

public partial class MainWindow : Window
{
    readonly LibraryStore store = new();
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
    public MainWindow()
    {
        InitializeComponent(); SourceInitialized+=(_,_)=>WindowBounds.Attach(this,()=>IsFullscreen); ContentRendered+=(_,_)=>{_=Task.Run(()=>PlaybackEngine.Prepare(SubtitleAppearance.Load(store).Options()));}; KeyboardNavigation.SetTabNavigation(ModalLayer,KeyboardNavigationMode.Cycle); tmdb=new(store); scanner=new(store); vm=new(store); DataContext=vm;
        scanner.Status += s => Dispatcher.BeginInvoke(() => { if(!closed) vm.Status=s; });
        scanner.Updated += () => Dispatcher.BeginInvoke(async () => { if(closed) return; await ReloadLibrary(); if(current is not PlayerWindow)current?.RefreshData(); _=Enrich(); });
        Loaded += async (_,_) => { await ReloadLibrary(); await scanner.ScanAsync(); _=Enrich(); reconcile.Start(); };
        reconcile.Tick += async (_,_) => { await scanner.ScanAsync(); };
        Closing += (_,_) => { current?.Leave(); };
        Closed += (_,_) => { closed=true; cancelModal?.Invoke(); reconcile.Stop(); _=Task.Run(scanner.Dispose); tmdb.Dispose(); };
    }
    public async Task ReloadLibrary()
    {
        if(refreshing || closed) return;
        refreshing=true;
        try { var data=await Task.Run(() => { var loaded=store.LoadCatalog(); foreach(var s in loaded.OrderByDescending(x=>x.Id).Take(24)) LocalImageConverter.Load(s.Poster); var hero=loaded.Where(x=>x.State=="В процессе").OrderByDescending(x=>x.Episodes.Max(e=>e.LastPlayed)).FirstOrDefault() ?? loaded.OrderByDescending(x=>x.Id).FirstOrDefault(); LocalImageConverter.Load(hero?.Backdrop,1600); LocalImageConverter.Load(hero?.Logo,600); return loaded; }); if(!closed) vm.Reload(data); }
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
        history.Push(current); current=screen; Present();
    }
    void Present()
    {
        PageHost.Content=current; PageHost.Visibility=current==null ? Visibility.Collapsed : Visibility.Visible;
        Scroller.Visibility=current==null ? Visibility.Visible : Visibility.Collapsed;
        Navigation.Visibility=current is PlayerWindow ? Visibility.Collapsed : Visibility.Visible;
        BackButton.Visibility=history.Count>0 ? Visibility.Visible : Visibility.Collapsed;
        HomeButton.Style=(Style)FindResource(current==null ? "PrimaryButton" : typeof(Button));
        LibraryButton.ClearValue(Control.BackgroundProperty); LibraryButton.ClearValue(Control.BorderThicknessProperty);
        LibraryButton.Style=(Style)FindResource(current is LibraryScreen ? "PrimaryButton" : typeof(Button));
        if(current!=null) { Ui.Enter(current); current.Focus(); }
    }
    public void Back()
    {
        if(cancelModal!=null) { cancelModal(); return; }
        if(current is PlayerWindow p && p.ClosePanel()) return;
        if(IsFullscreen) { ToggleFullscreen(); return; }
        if(history.Count==0) return;
        current?.Leave(); current=history.Pop(); Present(); current?.RefreshData(); _=ReloadLibrary();
    }
    void BackClick(object sender,RoutedEventArgs e)=>Back();
    void GoHome(object sender,RoutedEventArgs e)
    {
        cancelModal?.Invoke(); current?.Leave(); history.Clear(); current=null; Present(); Scroller.ScrollToTop(); _=ReloadLibrary();
    }
    void GoLibrary(object sender,RoutedEventArgs e) { if(current is LibraryScreen) return; Navigate(new LibraryScreen(this,store)); }
    void Settings(object sender,RoutedEventArgs e) { if(current is SettingsWindow) return; Navigate(new SettingsWindow(this,store,tmdb)); }
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
        if(!IsFullscreen) { previousBounds=WindowState==WindowState.Normal ? new Rect(Left,Top,ActualWidth,ActualHeight) : RestoreBounds; previousState=WindowState; previousTopmost=Topmost; previousChrome=System.Windows.Shell.WindowChrome.GetWindowChrome(this); IsFullscreen=true; WindowState=WindowState.Normal; System.Windows.Shell.WindowChrome.SetWindowChrome(this,null); ChromeRow.Height=new(0); Topmost=true; WindowState=WindowState.Maximized; IsFullscreen=true; }
        else { IsFullscreen=false; WindowState=WindowState.Normal; System.Windows.Shell.WindowChrome.SetWindowChrome(this,previousChrome); ChromeRow.Height=new(32); Topmost=previousTopmost; if(!previousBounds.IsEmpty){Left=previousBounds.Left;Top=previousBounds.Top;Width=previousBounds.Width;Height=previousBounds.Height;} WindowState=previousState; IsFullscreen=false; }
    }
    void KeyPressed(object sender,KeyEventArgs e)
    {
        if(e.Key==Key.Escape) { Back(); e.Handled=true; }
        else if(current is PlayerWindow p && cancelModal==null) p.HandleKey(e);
    }
    void MinimizeWindow(object sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    void MaximizeWindow(object sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
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




