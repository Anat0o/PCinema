using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using LibVLCSharp.Shared;
using Microsoft.Win32;
namespace PCinema;

public record TrackOption(int Id,string Name);
public partial class PlayerWindow : Screen
{
    readonly LibraryStore store;readonly Series series;Episode episode;
    readonly PlaybackSession session;readonly SeekCoordinator seek;
    readonly SubtitlePreferences subtitles;readonly AutoplayController autoplay=new();
    readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(100)};
    bool seeking,updating,tracksUpdating,closing,restored,ended,initialized,controlsVisible=true,loading,appearanceReady;
    bool paused,clickWasPaused,seekPending,completionEligible,positionConfirmed;
    long owner,confirmedSeekTime;int seekRevision;
    DateTime lastSave,lastActivity=DateTime.UtcNow;
    bool? resumePaused;int? resumeAudio,resumeSubtitle;bool resumeMuted;string? externalSubtitle;
    TrackOption[]? shownAudio,shownSubtitles;
    PlaybackSnapshot state=>session.Snapshot;
    public PlayerWindow(MainWindow host,LibraryStore store,Series series,Episode episode):base(host)
    {
        this.store=store;this.series=series;this.episode=episode;session=host.Playback;subtitles=new(store,false);
        seek=new((target,cancellation)=>SeekAsync(target,cancellation));
        InitializeComponent();_=InitializeAppearance();
        Loaded+=(_,_)=>{if(!initialized){initialized=true;_=Start(episode);}};
        timer.Tick+=(_,_)=>Tick();Position.LostMouseCapture+=(_,_)=>CommitDrag();
        session.Ended+=MediaEnded;session.Error+=MediaError;
    }
    async Task Start(Episode ep)
    {
        seek.Cancel();seekRevision++;seekPending=false;timer.Stop();autoplay.Cancel();CancelAutoplayButton.Visibility=Visibility.Collapsed;
        episode=ep;restored=false;ended=false;completionEligible=false;loading=true;shownAudio=shownSubtitles=null;lastSave=DateTime.UtcNow;
        owner=session.Begin();var openingOwner=owner;paused=resumePaused??false;resumePaused=null;
        Video.MediaPlayer=null;Overlay.Background=Brushes.Black;
        EpisodeTitle.Text=series.Title;EpisodeNumber.Text=ep.Label;PlayerStatus.Text="Открываем файл…";
        try
        {
            var preferences=await Task.Run(()=>(Options:SubtitleAppearance.Load(store).Options(),Software:store.Setting("software_decoding")=="true",Disabled:store.Setting("subtitles_disabled")=="true"));
            subtitles.LoadDisabled(preferences.Disabled);
            var native=await session.PrepareAsync(preferences.Options,preferences.Software,openingOwner);
            if(closing||!session.IsCurrent(openingOwner))return;
            Video.MediaPlayer=native;
            await session.OpenAsync(openingOwner,ep.Path,subtitles.Disabled,(int)VolumeSlider.Value,resumeMuted);
            if(closing||!session.IsCurrent(openingOwner))return;
            if(externalSubtitle!=null){var uri=new Uri(externalSubtitle).AbsoluteUri;await session.CommandAsync(owner,p=>p.AddSlave(MediaSlaveType.Subtitle,uri,!subtitles.Disabled));}
            timer.Start();NextButton.IsEnabled=NextEpisode()!=null;Overlay.Focus();
        }
        catch(Exception ex){if(!closing)PlayerStatus.Text="Не удалось открыть видео. Проверьте файл или режим декодирования.";_=Task.Run(()=>PerformanceLog.Write("player.start.failed",error:ex));}
        finally{if(owner==openingOwner){loading=false;Overlay.Background=new SolidColorBrush(Color.FromArgb(1,0,0,0));}}
    }
    void MediaEnded(long id)=>Dispatcher.BeginInvoke(()=>
    {
        if(closing||id!=owner||loading||seekPending||seeking||seek.Target!=null)return;
        ended=true;episode.Watched=true;episode.Position=Math.Max(episode.Position,state.Length);Save();
        autoplay.Start(Host.AutoplayEnabled,NextEpisode()!=null,DateTime.UtcNow);
        CancelAutoplayButton.Visibility=autoplay.Pending?Visibility.Visible:Visibility.Collapsed;
        PlayerStatus.Text="Серия закончилась.";SetControls(true);
    });
    void MediaError(long id)=>Dispatcher.BeginInvoke(()=>{if(!closing&&id==owner){PlayerStatus.Text="Ошибка воспроизведения. Попробуйте программное декодирование в настройках.";SetControls(true);}});
    void Tick()
    {
        if(closing||loading)return;
        _=session.PollAsync(owner);var current=state;
        if(autoplay.Pending){PlayerStatus.Text=$"Следующая серия через {autoplay.Remaining(DateTime.UtcNow)} с…";if(autoplay.TakeDue(DateTime.UtcNow)){CancelAutoplayButton.Visibility=Visibility.Collapsed;var next=NextEpisode();if(next!=null){Switch(next);return;}}}
        if(!restored&&current.Seekable&&current.Length>0){restored=true;_=seek.RequestAsync(Math.Clamp(episode.Position,0,Math.Max(0,current.Length-1000)),true);}
        if(subtitles.Disabled&&current.Subtitle>=0)Command(p=>p.SetSpu(-1));
        if(resumeAudio is int audio&&current.AudioTracks?.Any(t=>t.Id==audio)==true){Command(p=>p.SetAudioTrack(audio));resumeAudio=null;}
        if(resumeSubtitle is int sub&&current.SubtitleTracks?.Any(t=>t.Id==sub)==true){Command(p=>p.SetSpu(subtitles.Disabled?-1:sub));resumeSubtitle=null;}
        if(!seekPending&&!seeking&&seek.Target==null&&current.Playing&&current.Time>=confirmedSeekTime+1000)completionEligible=true;
        if(controlsVisible)
        {
            if(!seeking&&seek.Target==null&&!seekPending){updating=true;Position.Maximum=Math.Max(1,current.Length);Position.Value=Math.Max(0,current.Time);updating=false;}
            ShowClock(seeking||seekPending||seek.Target!=null?(long)Position.Value:current.Time);
            PauseButton.Content=paused||ended?"\uE768":"\uE769";
        }
        tracksUpdating=true;
        if(!ReferenceEquals(shownAudio,current.AudioTracks)){shownAudio=current.AudioTracks;AudioTracks.ItemsSource=shownAudio;AudioTracks.DisplayMemberPath="Name";AudioTracks.SelectedValuePath="Id";}
        if(!ReferenceEquals(shownSubtitles,current.SubtitleTracks)){shownSubtitles=current.SubtitleTracks;SubtitleTracks.ItemsSource=shownSubtitles;SubtitleTracks.DisplayMemberPath="Name";SubtitleTracks.SelectedValuePath="Id";}
        AudioTracks.SelectedValue=current.Audio;SubtitleTracks.SelectedValue=current.Subtitle;tracksUpdating=false;
        if(DateTime.UtcNow-lastSave>=TimeSpan.FromSeconds(5))Save();
        if(current.Playing&&!paused&&!seeking&&MenuPanel.Visibility!=Visibility.Visible&&DateTime.UtcNow-lastActivity>TimeSpan.FromSeconds(3))SetControls(false);
        if(paused||ended)SetControls(true);
    }
    void ShowClock(long position)=>Clock.Text=$"{TimeSpan.FromMilliseconds(Math.Max(0,position)):hh\\:mm\\:ss} / {TimeSpan.FromMilliseconds(Math.Max(0,state.Length)):hh\\:mm\\:ss}";
    async Task SeekAsync(long target,CancellationToken cancellation)
    {
        if(closing||loading||!state.Seekable)return;
        int revision=++seekRevision;long id=owner;seekPending=true;positionConfirmed=false;completionEligible=false;PlayerStatus.Text="Перематываем…";
        try
        {
            await session.SeekAsync(id,target,paused,cancellation);cancellation.ThrowIfCancellationRequested();
            var deadline=DateTime.UtcNow.AddSeconds(4);bool confirmed=false;
            do{await session.PollAsync(id);if(closing||id!=owner||revision!=seekRevision)return;confirmed=Math.Abs(state.Time-target)<1500;if(confirmed)break;await Task.Delay(50,cancellation);}while(DateTime.UtcNow<deadline);
            if(confirmed){positionConfirmed=true;confirmedSeekTime=state.Time;episode.Position=Math.Max(0,state.Time);PlayerStatus.Text="";}
            else PlayerStatus.Text="Перемотка задерживается. Можно повторить или сменить режим декодирования.";
        }
        catch(OperationCanceledException){}
        catch(Exception ex){PlayerStatus.Text="Не удалось перемотать видео.";_=Task.Run(()=>PerformanceLog.Write("player.seek.failed",error:ex));}
        finally{if(id==owner&&revision==seekRevision)seekPending=false;}
    }
    void Save()
    {
        if(loading||!restored||!positionConfirmed||seekPending||seeking||seek.Target!=null)return;
        var current=state;episode.Position=ended?Math.Max(episode.Position,current.Length):Math.Max(0,current.Time);
        episode.Duration=Math.Max(episode.Duration,current.Length);
        if(ended||completionEligible&&episode.Duration>0&&episode.Position>=episode.Duration*.95)episode.Watched=true;
        episode.LastPlayed=DateTime.UtcNow.ToString("O");episode.NotifyUpdated();series.NotifyUpdated();_=Host.Progress.Enqueue(ProgressSnapshot.From(episode));lastSave=DateTime.UtcNow;
    }
    public override void Leave()
    {
        if(closing)return;Save();closing=true;seek.Cancel();seekRevision++;autoplay.Cancel();timer.Stop();
        session.Ended-=MediaEnded;session.Error-=MediaError;Video.MediaPlayer=null;_=session.CloseAsync(owner);
        if(Host.IsFullscreen)Host.ToggleFullscreen();
    }
    async void Command(Action<LibVLCSharp.Shared.MediaPlayer> action)
    {if(loading||closing)return;try{await session.CommandAsync(owner,action);}catch(Exception ex){_=Task.Run(()=>PerformanceLog.Write("player.command.failed",error:ex));}}
    void SetControls(bool show){if(controlsVisible==show)return;controlsVisible=show;Controls.IsHitTestVisible=show;Controls.BeginAnimation(OpacityProperty,new DoubleAnimation(show?1:0,TimeSpan.FromMilliseconds(160)));Overlay.Cursor=show?Cursors.Arrow:Cursors.None;}
    void Activity(object sender,MouseEventArgs e){lastActivity=DateTime.UtcNow;SetControls(true);}
    void SetPaused(bool value){paused=value;Command(p=>p.SetPause(value));PauseButton.Content=value?"\uE768":"\uE769";Save();lastActivity=DateTime.UtcNow;SetControls(true);}
    void Pause(object sender,RoutedEventArgs e){if(loading)return;if(ended){episode.Position=0;resumePaused=false;_=Start(episode);return;}SetPaused(!paused);}
    void CancelAutoplay(object sender,RoutedEventArgs e){autoplay.Cancel();CancelAutoplayButton.Visibility=Visibility.Collapsed;PlayerStatus.Text="Автоплей отменён.";}
    bool OverControl(DependencyObject? element){while(element!=null&&element!=Overlay){if(element==MenuPanel||element is ButtonBase or Slider or ComboBox or TextBox or ListBox)return true;element=element is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);}return false;}
    void VideoClick(object sender,MouseButtonEventArgs e)
    {
        if(OverControl(e.OriginalSource as DependencyObject))return;lastActivity=DateTime.UtcNow;SetControls(true);Overlay.Focus();e.Handled=true;
        if(MenuPanel.Visibility==Visibility.Visible){ClosePanel();return;}
        if(e.ClickCount==2){SetPaused(clickWasPaused);Host.ToggleFullscreen();}
        else if(e.ClickCount==1){clickWasPaused=paused;Pause(sender,e);}
    }
    void VideoWheel(object sender,MouseWheelEventArgs e){if(OverControl(e.OriginalSource as DependencyObject))return;VolumeSlider.Value=Math.Clamp(VolumeSlider.Value+Math.Sign(e.Delta)*5,0,100);lastActivity=DateTime.UtcNow;SetControls(true);e.Handled=true;}
    void Back(object sender,RoutedEventArgs e)=>Jump(-10000);void Forward(object sender,RoutedEventArgs e)=>Jump(10000);
    void Jump(long offset){if(loading||!state.Seekable)return;_=seek.JumpAsync(offset,state.Time,state.Length);updating=true;Position.Value=seek.Target??state.Time;updating=false;lastActivity=DateTime.UtcNow;SetControls(true);}
    void SeekStart(object sender,MouseButtonEventArgs e){if(!loading&&state.Seekable){seeking=true;seek.Cancel();}}
    void SeekEnd(object sender,MouseButtonEventArgs e)=>CommitDrag();
    void CommitDrag(){if(!seeking)return;seeking=false;if(!loading&&state.Seekable)_=seek.RequestAsync((long)Position.Value,true);}
    void SeekChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(updating)return;ShowClock((long)e.NewValue);if(!loading&&!seeking&&state.Seekable)_=seek.RequestAsync((long)e.NewValue);}
    void Volume(object sender,RoutedPropertyChangedEventArgs<double> e)=>Command(p=>p.Volume=(int)e.NewValue);
    void Mute(object sender,RoutedEventArgs e){resumeMuted=!state.Muted;Command(p=>p.Mute=resumeMuted);}
    void AudioChanged(object sender,SelectionChangedEventArgs e){if(!tracksUpdating&&AudioTracks.SelectedValue is int id)Command(p=>p.SetAudioTrack(id));}
    void SubtitleChanged(object sender,SelectionChangedEventArgs e){if(!tracksUpdating&&SubtitleTracks.SelectedValue is int id){_=subtitles.SelectTrackAsync(id);Command(p=>p.SetSpu(id));}}
    void LoadSubtitle(object sender,RoutedEventArgs e){var d=new OpenFileDialog{Filter="Субтитры|*.srt;*.ass"};if(d.ShowDialog(Host)==true&&!loading){externalSubtitle=d.FileName;var uri=new Uri(d.FileName).AbsoluteUri;_=subtitles.SelectTrackAsync(0);Command(p=>p.AddSlave(MediaSlaveType.Subtitle,uri,true));}}
    async Task InitializeAppearance()
    {
        (SubtitleAppearance Appearance,bool Software) config;
        try{config=await Task.Run(()=>(SubtitleAppearance.Load(store),store.Setting("software_decoding")=="true"));}catch(Exception ex){_=Task.Run(()=>PerformanceLog.Write("player.preferences.failed",error:ex));config=(new(),false);}
        if(closing)return;
        Decoding.ItemsSource=new[]{"Автоматически","Программное"};Decoding.SelectedIndex=config.Software?1:0;
        var value=config.Appearance;
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
    async void DecodingChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!appearanceReady||loading||closing)return;
        Save();resumePaused=paused;resumeAudio=state.Audio;resumeSubtitle=externalSubtitle==null?state.Subtitle:null;resumeMuted=state.Muted;
        string value=Decoding.SelectedIndex==1?"true":"false";
        await Task.Run(()=>store.Setting("software_decoding",value));if(!closing)await Start(episode);
    }
    async void ApplyAppearance(object sender,RoutedEventArgs e)
    {
        if(loading||closing)return;var appearance=SelectedAppearance();Save();resumePaused=paused;resumeAudio=state.Audio;resumeSubtitle=externalSubtitle==null?state.Subtitle:null;resumeMuted=state.Muted;
        await Task.Run(()=>appearance.Save(store));if(!closing)await Start(episode);
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
    void Switch(Episode next){if(loading||closing)return;Save();seek.Cancel();externalSubtitle=null;resumePaused=null;resumeAudio=null;resumeSubtitle=null;_=Start(next);ClosePanel();}
    void ShowTracks(object sender,RoutedEventArgs e){MenuPanel.Visibility=Visibility.Visible;TrackPanel.Visibility=Visibility.Visible;EpisodeList.Visibility=Visibility.Collapsed;SetControls(true);Ui.Enter(MenuPanel);}
    void ShowEpisodes(object sender,RoutedEventArgs e){MenuPanel.Visibility=Visibility.Visible;TrackPanel.Visibility=Visibility.Collapsed;EpisodeList.Visibility=Visibility.Visible;EpisodeList.ItemsSource=series.Episodes;EpisodeList.DisplayMemberPath="Label";EpisodeList.SelectedItem=episode;SetControls(true);Ui.Enter(MenuPanel);}
    void SelectEpisode(object sender,MouseButtonEventArgs e){if(EpisodeList.SelectedItem is Episode ep){if(ep.Available)Switch(ep);else PlayerStatus.Text="Файл недоступен. Подключите диск.";}}
    void CloseMenu(object sender,RoutedEventArgs e)=>ClosePanel();
    public bool ClosePanel(){if(MenuPanel.Visibility!=Visibility.Visible)return false;MenuPanel.Visibility=Visibility.Collapsed;lastActivity=DateTime.UtcNow;return true;}
}


