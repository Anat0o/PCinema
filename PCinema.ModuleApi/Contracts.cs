using System.Windows;
namespace PCinema.ModuleApi;

public enum ViewKind { Home, Library, History, Series, Player }
public enum Route { Home, Library, History, Settings, Modules, Series }
public sealed record EpisodeInfo(long Id,long SeriesId,int? Season,int? Number,string Title,bool Available,long Position,long Duration,bool Watched,string LastPlayed);
public sealed record SeriesInfo(long Id,int? TmdbId,string Title,string Overview,string? Poster,int? Year,IReadOnlyList<EpisodeInfo> Episodes);
public sealed record PlayerState(long? EpisodeId,bool IsPlaying,long Position,long Duration);
public sealed record AppEvent(string Name,long? SeriesId=null,long? EpisodeId=null);
public sealed record ModuleCommand(string Id,string Label,Func<long?,long?,CancellationToken,Task> Execute);
public sealed record ModulePage(string Id,string Label,Func<IModuleContext,FrameworkElement> Create,bool IsSettings=false);
public interface IPCinemaModule
{
    void Register(IModuleRegistration registration);
    Task StartAsync(IModuleContext context,CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
// Optional, additive API: only these non-secret settings may leave the device.
public interface IPortableModuleSettings
{
    int PortableSettingsVersion { get; }
    IReadOnlyCollection<string> PortableSettingKeys { get; }
}
public interface IModuleRegistration
{
    void AddPage(ModulePage page);
    void AddSeriesCommand(ModuleCommand command);
    void AddEpisodeCommand(ModuleCommand command);
    void SetSkin(IModuleSkin skin);
}
public interface IModuleSkin
{
    FrameworkElement CreateShell(ShellContext context);
    FrameworkElement CreateView(ViewContext context);
    IReadOnlyDictionary<string,string> ThemeTokens { get; }
}
public sealed record ShellContext(FrameworkElement Content,IModuleContext Host);
// BuiltInView preserves all core operations. Player views replace only the overlay;
// the app owns the video surface, VLC and progress throughout its lifetime.
public sealed record ViewContext(ViewKind Kind,FrameworkElement BuiltInView,IModuleContext Host,long? SeriesId=null);
public interface IModuleContext
{
    string ModuleId { get; }
    string DataDirectory { get; }
    Task<IReadOnlyList<SeriesInfo>> GetLibraryAsync(bool includeUnavailable,CancellationToken cancellationToken=default);
    Task<IReadOnlyList<SeriesInfo>> GetHistoryAsync(CancellationToken cancellationToken=default);
    Task NavigateAsync(Route route,long? seriesId=null);
    Task GoBackAsync();
    Task OpenPageAsync(string id);
    Task PlayAsync(long seriesId,long episodeId);
    Task SetPausedAsync(bool paused);
    Task SeekAsync(long milliseconds);
    Task ToggleFullscreenAsync();
    Task TogglePictureInPictureAsync();
    Task NextEpisodeAsync();
    Task SetVolumeAsync(int percent);
    Task ShowPlayerOptionsAsync();
    PlayerState Player { get; }
    Task<string?> ReadSettingAsync(string key);
    Task WriteSettingAsync(string key,string? value);
    IDisposable Subscribe(Action<AppEvent> handler);
    Task OnUIAsync(Action action);
    void Log(string message,Exception? error=null);
}
public static class ThemeKeys
{
    public const string Glass="App.Glass",Border="App.Border",Hover="App.Hover";
    public const string Background="App.Background",Panel="App.Panel",Text="App.Text",Muted="App.Muted",Accent="App.Accent",Font="App.Font",FontSize="App.FontSize",Radius="App.Radius",Animation="App.Animation";
}
