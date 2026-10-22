using PCinema;
using LibVLCSharp.Shared;
if(args.Length!=2)throw new ArgumentException("Usage: PCinema.Smoke <mp4> <mkv>");
foreach(var path in args)
{
 using var engine=new PlaybackEngine("--vout=dummy","--aout=dummy","--no-video-title-show");
 engine.Player.EnableHardwareDecoding=false;
 if(!engine.Play(Path.GetFullPath(path)))throw new Exception("Play failed: "+path);
 var p=engine.Player;var deadline=DateTime.UtcNow.AddSeconds(15);
 while((!p.IsSeekable||p.Length<=0||p.Time<500)&&DateTime.UtcNow<deadline)await Task.Delay(100);
 if(p.Length<=0||!p.IsSeekable)throw new Exception("Media did not become seekable");
 p.Time=12000;await Task.Delay(1000);p.SetPause(true);
 if(Math.Abs(p.Time-12000)>2500)throw new Exception("Seek failed: "+p.Time);
 var audio=p.AudioTrackDescription.Where(t=>t.Id>=0).ToList();
 foreach(var track in audio){if(!p.SetAudioTrack(track.Id))throw new Exception("Audio track switch failed");}
 if(Path.GetExtension(path)==".mkv")
 {
  if(audio.Count<2)throw new Exception("Expected two audio tracks");
  var subtitles=p.SpuDescription.Where(t=>t.Id>=0).ToList();if(subtitles.Count==0)throw new Exception("Expected subtitles");
  if(!p.SetSpu(subtitles[0].Id))throw new Exception("Subtitle switch failed");
 }
 var directory=Path.Combine(Path.GetTempPath(),"PCinema-smoke",Guid.NewGuid().ToString("N"));var store=new LibraryStore(directory);store.Import(path,NameParser.Parse(path));var e=store.Load().Single().Episodes.Single();new ProgressService(store).Save(e,p.Time,p.Length);var saved=new LibraryStore(directory).Load().Single().Episodes.Single();if(saved.Position!=e.Position||saved.Position<=0)throw new Exception("Progress persistence failed");
 Console.WriteLine($"PASS {Path.GetFileName(path)} duration={p.Length}ms saved={saved.Position}ms audio={audio.Count} subtitles={p.SpuCount}");
}
// A saved global preference must also suppress VLC's default subtitle selection,
// while still allowing an explicit track selection without recreating the player.
using(var engine=new PlaybackEngine("--vout=dummy","--aout=dummy","--no-video-title-show"))
{
 engine.Player.EnableHardwareDecoding=false;
 for(int episode=0;episode<2;episode++)
 {
  if(!engine.Play(Path.GetFullPath(args[1]),subtitlesDisabled:true))throw new Exception("Play failed");
  var p=engine.Player;var deadline=DateTime.UtcNow.AddSeconds(15);
  while((p.Time<500||p.SpuCount<2)&&DateTime.UtcNow<deadline)await Task.Delay(100);
  if(p.Time<500||p.SpuCount<2)throw new Exception("Subtitle test media not ready");
  await Task.Delay(500);
  if(p.Spu!=-1)throw new Exception("Disabled subtitles became active on episode "+episode);
  var track=p.SpuDescription.First(t=>t.Id>=0);
  if(!p.SetSpu(track.Id))throw new Exception("Cannot re-enable subtitles");
  await Task.Delay(500);
  if(p.Spu!=track.Id)throw new Exception("Subtitle selection was not applied");
  p.Stop();
 }
 Console.WriteLine("PASS subtitles off across media switches; explicit re-enable works");
}
