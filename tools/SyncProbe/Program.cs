using PCinema;
using PCinema.Sync;
using System.Text.Json;

if(args.Length<2)throw new ArgumentException("SyncProbe <isolated directory> seed|status|sync|verify|clean");
var directory=Path.GetFullPath(args[0]);
if(!directory.Contains("qa-2.1-google-",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Use an isolated QA profile.");
var store=new LibraryStore(directory);const string title="PCinema · проверка синхронизации";
if(args[1]=="seed")
{
    var path=Path.Combine(directory,"synthetic-not-a-video.S01E02.mkv");
    store.Import(path,new(title,null,1,2));var ep=store.Load().Single(s=>s.Title==title).Episodes.Single();new ProgressService(store).Save(ep,42000,120000);
    store.Execute("UPDATE episodes SET available=0 WHERE id=$i",("$i",ep.Id));store.Setting("autoplay_next","false");store.CaptureSync();Console.WriteLine("Synthetic QA history prepared; no media created.");return;
}
var account=new GoogleAccount(directory);
if(args[1]=="status")
{
    var session=account.Identity==null?null:new SyncCoordinator(store,new GoogleDriveTransport(account),account.Identity.Sub).Session;
    Console.WriteLine(JsonSerializer.Serialize(new {signedIn=account.Identity!=null,session=session!=null,lastSuccess=session?.LastSuccess,pending=session?.Pending,history=store.LoadCatalog(true).Sum(s=>s.Episodes.Count(e=>e.LastPlayed.Length>0)),sample=store.Load().FirstOrDefault(s=>s.Title==title)?.Episodes.FirstOrDefault()?.Position}));return;
}
if(args[1]=="verify")
{
    var ep=store.LoadCatalog(true).Single(s=>s.Title==title).Episodes.Single();
    if(ep.Position!=42000||ep.Season!=1||ep.Number!=2||ep.Available||ep.Path.Length!=0||store.Setting("autoplay_next")!="false")throw new Exception("Restored QA history differs.");Console.WriteLine("Verified: UUID history, season 1 episode 2, position 42000 ms, no local file, autoplay off.");return;
}
var coordinator=new SyncCoordinator(store,new GoogleDriveTransport(account),account.Identity?.Sub??throw new InvalidOperationException("Sign in through the app first."));
if(args[1]=="clean")
{
    var entries=store.SyncEntries().Where(e=>e.Key.StartsWith("history:")&&!e.Deleted&&JsonSerializer.Deserialize<HistoryData>(e.Value!)?.Title==title).ToArray();
    foreach(var entry in entries)store.ApplySync([entry with{Value=null,Deleted=true,Stamp=store.NextStamp()}]);
}
if(args[1] is "sync" or "clean"){await coordinator.SynchronizeAsync();Console.WriteLine("Google Drive exchange completed.");}
