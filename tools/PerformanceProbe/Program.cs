using System.IO;
using PCinema;
using System.Diagnostics;
using System.Text.Json;
// Dummy output measures engine/control work; it does not validate D3D presentation or GPU decoding.
if(args.Length==2&&args[0]=="--cache")
{
    LibVLCSharp.Shared.Core.Initialize(Path.GetFullPath(args[1]));
    using var vlc=new LibVLCSharp.Shared.LibVLC("--reset-plugins-cache","--quiet");
    if(!File.Exists(Path.Combine(args[1],"plugins","plugins.dat")))throw new IOException("Plugin cache was not generated.");
    return;
}
if(args.Length==0)throw new ArgumentException("Pass local media paths. Output contains file indices only.");
var results=new List<object>();var session=new PlaybackSession();
foreach(bool software in new[]{false,true})
for(int file=0;file<args.Length;file++)
for(int iteration=0;iteration<3;iteration++)
{
    var owner=session.Begin();var sw=Stopwatch.StartNew();
    await session.PrepareAsync(["--vout=dummy","--aout=dummy","--no-video-title-show"],software,owner);
    var preparation=sw.Elapsed.TotalMilliseconds;
    await session.OpenAsync(owner,Path.GetFullPath(args[file]),true,0,false);
    long pictures=0;var deadline=Stopwatch.StartNew();
    while(deadline.Elapsed.TotalSeconds<15){await session.PollAsync(owner);await session.CommandAsync(owner,p=>{using var media=p.Media;pictures=media?.Statistics.DisplayedPictures??0;});if(pictures>0&&session.Snapshot.Seekable)break;await Task.Delay(20);}
    var firstFrame=sw.Elapsed.TotalMilliseconds;int passed=0,failed=0,pausedPassed=0;
    if(iteration==0&&pictures>0)
    for(int seek=0;seek<30;seek++)
    {
        var paused=seek%3==0;var target=(long)(session.Snapshot.Length*(seek%2==0?.2:.65));
        await session.SeekAsync(owner,target,paused);var timer=Stopwatch.StartNew();bool accurate=false;
        do{await session.PollAsync(owner);accurate=Math.Abs(session.Snapshot.Time-target)<1500&&session.Snapshot.Playing!=paused;if(accurate)break;await Task.Delay(20);}while(timer.Elapsed.TotalSeconds<3);
        if(accurate){passed++;if(paused)pausedPassed++;}else failed++;
    }
    results.Add(new{software,file,iteration,preparation,firstFrame,frameObserved=pictures>0,passed,failed,pausedPassed});
    await session.CloseAsync(owner);
}
await session.DisposeAsync();Console.WriteLine(JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));

