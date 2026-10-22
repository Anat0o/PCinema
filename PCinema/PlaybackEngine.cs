using LibVLCSharp.Shared;
namespace PCinema;
/// <summary>Owns native playback resources independently of the WPF window.</summary>
public sealed class PlaybackEngine : IDisposable
{
    static readonly object preparationLock=new();
    static Task<PlaybackEngine>? prepared;
    static string[] preparedOptions=[];
    public static void Prepare(string[] options)
    {
        lock(preparationLock)
        {
            if(prepared!=null)return;
            preparedOptions=options.ToArray();prepared=Task.Run(()=>new PlaybackEngine(options));
            // Observe failures; opening the player can retry normally.
            _=prepared.ContinueWith(t=>{_ = t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
        }
    }
    public static Task<PlaybackEngine> AcquireAsync(string[] options)
    {
        lock(preparationLock)
        {
            var ready=prepared;prepared=null;
            if(ready!=null && preparedOptions.SequenceEqual(options) && !ready.IsFaulted)return ready;
            if(ready!=null)_=Discard(ready);
            return Task.Run(()=>new PlaybackEngine(options));
        }
    }
    static async Task Discard(Task<PlaybackEngine> pending)
    {
        try{var engine=await pending.ConfigureAwait(false);await Task.Run(engine.Dispose).ConfigureAwait(false);}catch{ /* A failed warmup owns no usable player. */ }
    }
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
