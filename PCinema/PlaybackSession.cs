using System.Diagnostics;
using System.IO;
using LibVLCSharp.Shared;
namespace PCinema;

public sealed record PlaybackSnapshot(long Time=0,long Length=0,bool Playing=false,bool Seekable=false,int Audio=-1,int Subtitle=-1,bool Muted=false,TrackOption[]? AudioTracks=null,TrackOption[]? SubtitleTracks=null);

/// <summary>Application-owned native resources. Only VideoView attachment runs on the dispatcher.</summary>
public sealed class PlaybackSession
{
    readonly SerialWorkQueue commands=new();
    PlaybackEngine? engine;
    string[] options=[];
    bool software,broken,disposed;
    long generation;
    int polling;
    PlaybackSnapshot snapshot=new();
    EventHandler<EventArgs>? endedHandler,errorHandler;
    Stopwatch? opening;
    bool loggedSeekable,loggedFrame;
    public PlaybackSnapshot Snapshot=>Volatile.Read(ref snapshot);
    public event Action<long>? Ended;
    public event Action<long>? Error;
    public long Begin(){Volatile.Write(ref snapshot,new());return Interlocked.Increment(ref generation);}
    public bool IsCurrent(long owner)=>owner==Interlocked.Read(ref generation)&&!disposed;
    public Task<MediaPlayer?> PrepareAsync(string[] desired,bool softwareDecoding,long owner)=>commands.Run(()=>
    {
        if(!IsCurrent(owner))return null;
        if(engine==null||broken||software!=softwareDecoding||!options.SequenceEqual(desired))
        {
            Unsubscribe();engine?.Dispose();engine=null;
            engine=PerformanceLog.Measure("player.prepare",()=>new PlaybackEngine(desired));
            options=desired.ToArray();software=softwareDecoding;broken=false;
            engine.Player.EnableHardwareDecoding=!software;
        }
        return engine.Player;
    });
    void Unsubscribe()
    {
        if(engine==null)return;
        if(endedHandler!=null)engine.Player.EndReached-=endedHandler;
        if(errorHandler!=null)engine.Player.EncounteredError-=errorHandler;
        endedHandler=null;errorHandler=null;
    }
    public Task OpenAsync(long owner,string path,bool disableSubtitles,int volume,bool muted)=>commands.Run(()=>
    {
        if(!IsCurrent(owner)||engine==null)return;
        Unsubscribe();engine.Player.Stop();
        if(!IsCurrent(owner))return;
        if(!File.Exists(path))throw new FileNotFoundException("Файл недоступен. Подключите диск.");
        opening=Stopwatch.StartNew();loggedFrame=loggedSeekable=false;
        endedHandler=(_,_)=>{if(IsCurrent(owner))Ended?.Invoke(owner);};
        errorHandler=(_,_)=>{if(IsCurrent(owner)){broken=true;Error?.Invoke(owner);}};
        engine.Player.EndReached+=endedHandler;engine.Player.EncounteredError+=errorHandler;
        engine.Player.Volume=volume;engine.Player.Mute=muted;
        if(!engine.Play(path,disableSubtitles))throw new IOException("Не удалось открыть видео.");
        PerformanceLog.Write("player.open",opening.Elapsed.TotalMilliseconds);
    });
    public Task CommandAsync(long owner,Action<MediaPlayer> action)=>commands.Run(()=>{if(IsCurrent(owner)&&engine!=null)action(engine.Player);});
    public Task SeekAsync(long owner,long target,bool paused,CancellationToken cancellation=default)=>CommandAsync(owner,p=>
    {
        if(cancellation.IsCancellationRequested||!p.IsSeekable)return;
        var timer=Stopwatch.StartNew();p.Time=Math.Clamp(target,0,Math.Max(0,p.Length-500));p.SetPause(paused);
        PerformanceLog.Write("player.seek.command",timer.Elapsed.TotalMilliseconds);
    });
    public async Task PollAsync(long owner)
    {
        if(Interlocked.Exchange(ref polling,1)!=0)return;
        try{await commands.Run(()=>
        {
            if(!IsCurrent(owner)||engine==null)return;
            var p=engine.Player;
            var old=Snapshot;
            var audio=old.AudioTracks;var subs=old.SubtitleTracks;
            if(audio==null||audio.Length!=p.AudioTrackCount)audio=p.AudioTrackDescription.Select(t=>new TrackOption(t.Id,t.Id<0?"Выключено":t.Name)).ToArray();
            if(subs==null||subs.Length!=p.SpuCount)subs=p.SpuDescription.Select(t=>new TrackOption(t.Id,t.Id<0?"Выключено":t.Name)).ToArray();
            var next=new PlaybackSnapshot(p.Time,p.Length,p.IsPlaying,p.IsSeekable,p.AudioTrack,p.Spu,p.Mute,audio,subs);
            if(IsCurrent(owner))Volatile.Write(ref snapshot,next);
            if(opening!=null)
            {
                if(!loggedSeekable&&next.Seekable){loggedSeekable=true;PerformanceLog.Write("player.seekable",opening.Elapsed.TotalMilliseconds);}
                if(!loggedFrame){using var media=p.Media;if(media!=null&&media.Statistics.DisplayedPictures>0){loggedFrame=true;PerformanceLog.Write("player.first-frame",opening.Elapsed.TotalMilliseconds);}}
            }
        });}catch(Exception ex){PerformanceLog.Write("player.poll",error:ex);}finally{Volatile.Write(ref polling,0);}
    }
    public Task CloseAsync(long owner)
    {
        if(Interlocked.CompareExchange(ref generation,owner+1,owner)!=owner)return Task.CompletedTask;
        return commands.Run(()=>{Unsubscribe();engine?.Player.Stop();if(engine!=null)engine.Player.Media=null;Volatile.Write(ref snapshot,new());});
    }
    public Task DisposeAsync()
    {
        disposed=true;Interlocked.Increment(ref generation);
        return commands.Run(()=>{Unsubscribe();engine?.Dispose();engine=null;});
    }
}
