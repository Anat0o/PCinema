using LibVLCSharp.Shared;
namespace PCinema;
/// <summary>Owns native playback resources independently of the WPF window.</summary>
public sealed class PlaybackEngine : IDisposable
{
    readonly LibVLC vlc;
    public MediaPlayer Player { get; }
    public PlaybackEngine(params string[] options)
    { Core.Initialize(); vlc = new LibVLC(options.Length == 0 ? ["--no-video-title-show"] : options); Player = new MediaPlayer(vlc) { Volume = 80, EnableHardwareDecoding = true, EnableKeyInput = false, EnableMouseInput = false }; }
    public bool Play(string path, bool subtitlesDisabled = false)
    {
        using var media = new Media(vlc, path, FromType.FromPath);
        if (subtitlesDisabled) media.AddOption(":sub-language=none");
        return Player.Play(media);
    }
    public void Dispose() { Player.Stop(); Player.Dispose(); vlc.Dispose(); }
}
