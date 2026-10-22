using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using Microsoft.Win32;
namespace PCinema;

public record TrackOption(int Id,string Name);
public partial class PlayerWindow : Screen
{
    readonly LibraryStore store; readonly Series series;Episode episode;PlaybackEngine? engine;MediaPlayer? player;
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    bool seeking,updating,tracksUpdating,closing,restored,ended,initialized,controlsVisible=true;DateTime lastSave,lastActivity=DateTime.UtcNow;int trackCount=-1;
    readonly ProgressService progress;
    readonly SubtitlePreferences subtitles;
    readonly AutoplayController autoplay=new();
    readonly DispatcherTimer clickTimer=new();
    bool appearanceReady;bool? resumePaused;int? resumeAudio,resumeSubtitle;string? externalSubtitle;
    [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
    public PlayerWindow(MainWindow host,LibraryStore store,Series series,Episode episode):base(host)
    {
        this.store=store;this.series=series;this.episode=episode;progress=new(store);subtitles=new(store);
        InitializeComponent();InitializeAppearance();Loaded+=(_,_)=>{if(!initialized){initialized=true;Initialize();}};timer.Tick+=(_,_)=>Tick();
        clickTimer.Interval=TimeSpan.FromMilliseconds(GetDoubleClickTime());clickTimer.Tick+=(_,_)=>{clickTimer.Stop();Pause(this,new RoutedEventArgs());};
        Position.LostMouseCapture+=(_,_)=>{if(seeking){seeking=false;Save();}};
    }
    Task nativeOperation=Task.CompletedTask;
    bool loading;
    bool resumeMuted;
    async void Initialize()
    {
        try
        {
            loading=true;Overlay.Background=Brushes.Black;PlayerStatus.Text="Открываем плеер…";
            var options=SubtitleAppearance.Load(store).Options();
            var created=await PlaybackEngine.AcquireAsync(options);
            if(closing){await Task.Run(created.Dispose);return;}
            engine=created;player=engine.Player;Video.MediaPlayer=player;player.Volume=(int)VolumeSlider.Value;player.Mute=resumeMuted;
            player.EndReached+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(closing||loading||!ReferenceEquals(engine,created))return;ended=true;episode.Watched=true;episode.Position=Math.Max(episode.Position,episode.Duration);Save();var hasNext=NextEpisode()!=null;autoplay.Start(store.Setting("autoplay_next")!="false",hasNext,DateTime.UtcNow);CancelAutoplayButton.Visibility=autoplay.Pending?Visibility.Visible:Visibility.Collapsed;PlayerStatus.Text=hasNext?"Серия закончилась. Можно включить следующую.":"Сериал закончился.";SetControls(true);});
            player.EncounteredError+=(_,_)=>Dispatcher.BeginInvoke(()=>{if(!closing&&ReferenceEquals(engine,created)){PlayerStatus.Text="Не удалось воспроизвести файл. Проверьте доступность и формат.";SetControls(true);}});
            await Start(episode);if(closing||player==null)return;if(externalSubtitle!=null)player.AddSlave(MediaSlaveType.Subtitle,new Uri(externalSubtitle).AbsoluteUri,!subtitles.Disabled);timer.Start();Overlay.Focus();
        }
        catch(Exception ex){if(!closing)PlayerStatus.Text="Ошибка плеера: "+ex.Message;}
        finally{loading=false;Overlay.Background=new SolidColorBrush(Color.FromArgb(1,0,0,0));}
    }
    async Task Start(Episode ep)
    {
        if(player==null||engine==null)return;autoplay.Cancel();CancelAutoplayButton.Visibility=Visibility.Collapsed;episode=ep;restored=false;ended=false;trackCount=-1;lastSave=DateTime.UtcNow;
        EpisodeTitle.Text=series.Title;EpisodeNumber.Text=ep.Label;PlayerStatus.Text="Открываем файл…";loading=true;var active=engine;var disabled=subtitles.Disabled;
        try{
            nativeOperation=Task.Run(()=>{active.Player.Stop();if(!File.Exists(ep.Path))throw new FileNotFoundException("Файл недоступен. Подключите диск.");if(!active.Play(ep.Path,disabled))throw new IOException("Не удалось открыть видео.");});
            await nativeOperation;
            if(!closing){NextButton.IsEnabled=NextEpisode()!=null;lastActivity=DateTime.UtcNow;}
        }catch(Exception ex){if(!closing)PlayerStatus.Text=ex.Message;}finally{loading=false;}
    }
    void Tick()
    {
        if(player==null||closing||loading)return;
        if(autoplay.Pending)
        {
            PlayerStatus.Text=$"Следующая серия через {autoplay.Remaining(DateTime.UtcNow)} с…";
            if(autoplay.TakeDue(DateTime.UtcNow)){CancelAutoplayButton.Visibility=Visibility.Collapsed;var next=NextEpisode();if(next!=null){Switch(next);return;}PlayerStatus.Text="Следующая серия недоступна.";}
        }
        // VLC can select a default/forced track after asynchronous stream discovery.
        if(subtitles.Disabled && player.Spu>=0)player.SetSpu(-1);
        if(!restored&&player.IsSeekable&&player.Length>0){if(episode.Position>0)player.Time=Math.Min(episode.Position,Math.Max(0,player.Length-1000));restored=true;PlayerStatus.Text="";if(resumePaused.HasValue){player.SetPause(resumePaused.Value);resumePaused=null;}}
        if(resumeAudio is int audio && player.AudioTrackDescription.Any(t=>t.Id==audio)){player.SetAudioTrack(audio);resumeAudio=null;}
        if(resumeSubtitle is int sub && player.SpuDescription.Any(t=>t.Id==sub)){player.SetSpu(subtitles.Disabled?-1:sub);resumeSubtitle=null;}
        if(controlsVisible)
        {
            if(!seeking){updating=true;Position.Maximum=Math.Max(1,player.Length);Position.Value=Math.Max(0,player.Time);updating=false;}
            Clock.Text=$"{TimeSpan.FromMilliseconds(Math.Max(0,player.Time)):hh\\:mm\\:ss} / {TimeSpan.FromMilliseconds(Math.Max(0,player.Length)):hh\\:mm\\:ss}";
            PauseButton.Content=player.IsPlaying?"\uE769":"\uE768";
        }
        if(player.AudioTrackCount+player.SpuCount!=trackCount)
        {
            trackCount=player.AudioTrackCount+player.SpuCount;tracksUpdating=true;
            AudioTracks.ItemsSource=player.AudioTrackDescription.Select(t=>new TrackOption(t.Id,t.Id<0?"Выключено":t.Name)).ToArray();AudioTracks.DisplayMemberPath="Name";AudioTracks.SelectedValuePath="Id";AudioTracks.SelectedValue=player.AudioTrack;
            SubtitleTracks.ItemsSource=player.SpuDescription.Select(t=>new TrackOption(t.Id,t.Id<0?"Выключено":t.Name)).ToArray();SubtitleTracks.DisplayMemberPath="Name";SubtitleTracks.SelectedValuePath="Id";SubtitleTracks.SelectedValue=player.Spu;tracksUpdating=false;
        }
        if(SubtitleTracks.SelectedValue is int selected && selected!=player.Spu)
        {
            tracksUpdating=true;SubtitleTracks.SelectedValue=player.Spu;tracksUpdating=false;
        }
        if(DateTime.UtcNow-lastSave>=TimeSpan.FromSeconds(5))Save();
        if(player.IsPlaying&&!seeking&&MenuPanel.Visibility!=Visibility.Visible&&DateTime.UtcNow-lastActivity>TimeSpan.FromSeconds(3))SetControls(false);
        if(!player.IsPlaying)SetControls(true);
    }
    void Save()
    {
        if(player==null||loading)return;
        progress.Save(episode,restored&&!ended&&player.Time>=0?player.Time:episode.Position,Math.Max(episode.Duration,player.Length),ended);lastSave=DateTime.UtcNow;
    }
    public override void Leave()
    {
        if(closing)return;closing=true;autoplay.Cancel();Save();timer.Stop();clickTimer.Stop();Video.MediaPlayer=null;var old=engine;engine=null;player=null;if(old!=null)_=Release(old,nativeOperation);
        if(Host.IsFullscreen)Host.ToggleFullscreen();
    }
    static async Task Release(PlaybackEngine old,Task pending)
    {
        try{await pending.ConfigureAwait(false);}catch{ /* Release even after a failed opening operation. */ }
        await Task.Run(old.Dispose).ConfigureAwait(false);
    }
    void SetControls(bool show)
    {
        if(controlsVisible==show)return;controlsVisible=show;Controls.IsHitTestVisible=show;
        Controls.BeginAnimation(OpacityProperty,new DoubleAnimation(show?1:0,TimeSpan.FromMilliseconds(160)));
        Overlay.Cursor=show?Cursors.Arrow:Cursors.None;
    }
    void Activity(object sender,MouseEventArgs e){lastActivity=DateTime.UtcNow;SetControls(true);}
    void Pause(object sender,RoutedEventArgs e){clickTimer.Stop();if(loading)return;if(ended){episode.Position=0;_=Start(episode);return;}player?.Pause();Save();lastActivity=DateTime.UtcNow;SetControls(true);}
    void CancelAutoplay(object sender,RoutedEventArgs e){autoplay.Cancel();CancelAutoplayButton.Visibility=Visibility.Collapsed;PlayerStatus.Text="Автоплей отменён. Следующую серию можно включить вручную.";}
    bool OverControl(DependencyObject? element)
    {
        while(element!=null&&element!=Overlay)
        {
            if(element==MenuPanel||element is ButtonBase or Slider or ComboBox or TextBox or ListBox)return true;
            element=element is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);
        }
        return false;
    }
    void VideoClick(object sender,MouseButtonEventArgs e)
    {
        if(OverControl(e.OriginalSource as DependencyObject))return;
        lastActivity=DateTime.UtcNow;SetControls(true);Overlay.Focus();e.Handled=true;
        if(MenuPanel.Visibility==Visibility.Visible){ClosePanel();return;}
        if(e.ClickCount==2){clickTimer.Stop();Host.ToggleFullscreen();}
        else{clickTimer.Stop();clickTimer.Start();}
    }
    void VideoWheel(object sender,MouseWheelEventArgs e)
    {
        if(OverControl(e.OriginalSource as DependencyObject))return;
        VolumeSlider.Value=Math.Clamp(VolumeSlider.Value+Math.Sign(e.Delta)*5,0,100);lastActivity=DateTime.UtcNow;SetControls(true);e.Handled=true;
    }
    void Back(object sender,RoutedEventArgs e)=>Jump(-10000);void Forward(object sender,RoutedEventArgs e)=>Jump(10000);
    void Jump(long offset){if(!loading&&player is {IsSeekable:true})player.Time=Math.Clamp(player.Time+offset,0,Math.Max(0,player.Length-500));lastActivity=DateTime.UtcNow;SetControls(true);}
    void SeekStart(object sender,MouseButtonEventArgs e)=>seeking=true;
    void SeekEnd(object sender,MouseButtonEventArgs e){if(!loading&&player is {IsSeekable:true})player.Time=(long)Position.Value;seeking=false;Save();}
    void SeekChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(!loading&&!updating&&!seeking&&player is {IsSeekable:true})player.Time=(long)e.NewValue;}
    void Volume(object sender,RoutedPropertyChangedEventArgs<double> e){if(!loading&&player!=null)player.Volume=(int)e.NewValue;}
    void Mute(object sender,RoutedEventArgs e){if(!loading&&player!=null)player.Mute=!player.Mute;}
    void AudioChanged(object sender,SelectionChangedEventArgs e){if(!loading&&!tracksUpdating&&AudioTracks.SelectedValue is int id)player?.SetAudioTrack(id);}
    void SubtitleChanged(object sender,SelectionChangedEventArgs e){if(!loading&&!tracksUpdating&&SubtitleTracks.SelectedValue is int id && player!=null){subtitles.SelectTrack(id);player.SetSpu(id);}}
    void LoadSubtitle(object sender,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Субтитры|*.srt;*.ass"};if(d.ShowDialog(Host)==true && !loading && player!=null){if(player.AddSlave(MediaSlaveType.Subtitle,new Uri(d.FileName).AbsoluteUri,true)){subtitles.SelectTrack(0);externalSubtitle=d.FileName;}trackCount=-1;}}
    void InitializeAppearance()
    {
        var value=SubtitleAppearance.Load(store);
        SubtitleSize.ItemsSource=new[]{"Маленький","Обычный","Крупный","Очень крупный"};SubtitleSize.SelectedIndex=Array.IndexOf(SubtitleAppearance.Sizes,value.Size);
        SubtitleFont.ItemsSource=SubtitleAppearance.Fonts;SubtitleFont.SelectedItem=value.Font;
        SubtitleColor.ItemsSource=new[]{"Белый","Жёлтый","Голубой"};SubtitleColor.SelectedIndex=Array.IndexOf(SubtitleAppearance.Colors,value.Color);
        SubtitleStyle.ItemsSource=SubtitleAppearance.Styles;SubtitleStyle.SelectedItem=value.Style;SubtitleBold.IsChecked=value.Bold;appearanceReady=true;UpdatePreview();
    }
    SubtitleAppearance SelectedAppearance()=>new(SubtitleAppearance.Sizes[Math.Max(0,SubtitleSize.SelectedIndex)],SubtitleFont.SelectedItem as string??"Segoe UI",SubtitleAppearance.Colors[Math.Max(0,SubtitleColor.SelectedIndex)],SubtitleStyle.SelectedItem as string??"Обводка",SubtitleBold.IsChecked==true);
    void AppearanceChanged(object sender,RoutedEventArgs e){if(appearanceReady)UpdatePreview();}
    void UpdatePreview()
    {
        var a=SelectedAppearance();SubtitlePreview.FontFamily=new FontFamily(a.Font);SubtitlePreview.FontSize=320.0/a.Size;SubtitlePreview.Foreground=(Brush)new BrushConverter().ConvertFromString("#"+a.Color)!;SubtitlePreview.FontWeight=a.Bold?FontWeights.Bold:FontWeights.Normal;
        SubtitlePreviewBackground.Background=new SolidColorBrush(Color.FromArgb((byte)(a.Style=="Тёмный фон"?180:40),0,0,0));
    }
    async void ApplyAppearance(object sender,RoutedEventArgs e)
    {
        if(loading||closing)return;
        SelectedAppearance().Save(store);if(player==null||ended){PlayerStatus.Text="Оформление сохранено";return;}
        clickTimer.Stop();Save();resumePaused=!player.IsPlaying;resumeAudio=player.AudioTrack;resumeSubtitle=externalSubtitle==null?player.Spu:null;resumeMuted=player.Mute;
        timer.Stop();Video.MediaPlayer=null;var old=engine;engine=null;player=null;loading=true;if(old!=null)await Release(old,nativeOperation);if(closing)return;Initialize();
        PlayerStatus.Text="Оформление сохранено";
    }
    void NavigateBack(object sender,RoutedEventArgs e)=>Host.Back();
    void Fullscreen(object sender,RoutedEventArgs e)=>Host.ToggleFullscreen();
    void OverlayKey(object sender,KeyEventArgs e){if(e.Key==Key.Escape){Host.Back();e.Handled=true;}else HandleKey(e);}
    public void HandleKey(KeyEventArgs e)
    {
        if(e.Handled||e.OriginalSource is TextBox||e.OriginalSource is ComboBox)return;
        switch(e.Key){case Key.Space:Pause(this,e);break;case Key.Left:Jump(-10000);break;case Key.Right:Jump(10000);break;case Key.F:Host.ToggleFullscreen();break;default:return;}e.Handled=true;
    }
    Episode? NextEpisode(){int index=series.Episodes.FindIndex(e=>e.Id==episode.Id);return series.Episodes.Skip(index+1).FirstOrDefault(e=>e.Available);}
    void Next(object sender,RoutedEventArgs e){var next=NextEpisode();if(next!=null)Switch(next);}
    void Switch(Episode next){if(loading||closing)return;clickTimer.Stop();Save();externalSubtitle=null;resumePaused=null;resumeAudio=null;resumeSubtitle=null;_=Start(next);ClosePanel();}
    void ShowTracks(object sender,RoutedEventArgs e){MenuPanel.Visibility=Visibility.Visible;TrackPanel.Visibility=Visibility.Visible;EpisodeList.Visibility=Visibility.Collapsed;SetControls(true);Ui.Enter(MenuPanel);}
    void ShowEpisodes(object sender,RoutedEventArgs e){MenuPanel.Visibility=Visibility.Visible;TrackPanel.Visibility=Visibility.Collapsed;EpisodeList.Visibility=Visibility.Visible;EpisodeList.ItemsSource=series.Episodes;EpisodeList.DisplayMemberPath="Label";EpisodeList.SelectedItem=episode;SetControls(true);Ui.Enter(MenuPanel);}
    void SelectEpisode(object sender,MouseButtonEventArgs e){if(EpisodeList.SelectedItem is Episode ep){if(ep.Available)Switch(ep);else PlayerStatus.Text="Файл недоступен. Подключите диск.";}}
    void CloseMenu(object sender,RoutedEventArgs e)=>ClosePanel();
    public bool ClosePanel(){if(MenuPanel.Visibility!=Visibility.Visible)return false;MenuPanel.Visibility=Visibility.Collapsed;lastActivity=DateTime.UtcNow;return true;}
}


