using Xunit;
using System.IO;
namespace PCinema.Tests;
public class PlaybackPerformanceTests
{
    [Fact] public async Task SupersededSeekCancelsAlreadyWaitingApply()
    {
        var queue=new SerialWorkQueue();using var gate=new ManualResetEventSlim();var blocked=queue.Run(()=>gate.Wait());var applied=new List<long>();
        var seek=new SeekCoordinator((target,token)=>queue.Run(()=>{if(!token.IsCancellationRequested)applied.Add(target);}));
        var first=seek.RequestAsync(1000,true);var last=seek.RequestAsync(2000,true);gate.Set();await Task.WhenAll(blocked,first,last);Assert.Equal(new long[]{2000},applied);
    }
    [Fact] public async Task BusyDatabaseDoesNotBlockProgressEnqueueAndFlushWaits()
    {
        var dir=Path.Combine(Path.GetTempPath(),"PCinema-queue-tests",Guid.NewGuid().ToString("N"));var store=new LibraryStore(dir);
        store.Import(Path.Combine(dir,"Show.S01E01.mkv"),new("Show",null,1,1));var ep=store.Load().Single().Episodes.Single();
        using var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(dir,"library.db")}");connection.Open();using var transaction=connection.BeginTransaction();
        var writer=new ProgressWriter(store.SaveProgress);var write=writer.Enqueue(new(ep.Id,1234,10000,false,"2026-10-23T12:00:00.0000000Z"));
        var flush=writer.FlushAsync();await Task.Delay(100);Assert.False(flush.IsCompleted);transaction.Commit();await flush.WaitAsync(TimeSpan.FromSeconds(10));await write;
        Assert.Equal(1234,store.Load().Single().Episodes.Single().Position);
    }
    [Fact] public async Task TenThousandFilesReconcileWithoutDuplicatesAndOfflineRetainsCatalog()
    {
        var dir=Path.Combine(Path.GetTempPath(),"PCinema-scale-tests",Guid.NewGuid().ToString("N"));var media=Path.Combine(dir,"media");Directory.CreateDirectory(media);var store=new LibraryStore(dir);store.AddFolder(media);
        store.Execute("INSERT INTO series(id,search_title,title) VALUES(1,'Scale','Scale')");
        for(int i=1;i<=10000;i++)File.WriteAllBytes(Path.Combine(media,$"Scale.S01E{i}.mkv"),[1]);
        store.Execute("WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i+1 FROM n WHERE i<10000) INSERT INTO episodes(series_id,season,number,path) SELECT 1,1,i,$root || 'Scale.S01E' || i || '.mkv' FROM n",("$root",media+Path.DirectorySeparatorChar));
        using(var scanner=new LibraryScanner(store)){await scanner.ScanAsync();await scanner.ScanAsync();}
        Assert.Equal(10000,store.Query("SELECT COUNT(*) FROM episodes",r=>r.GetInt32(0)).Single());
        using(var offline=new LibraryScanner(store,_=>FilePresence.Unavailable))await offline.ScanAsync();
        Assert.Equal(10000,store.LoadCatalog().Single().Episodes.Count);Assert.All(store.LoadCatalog().Single().Episodes,e=>Assert.False(e.Available));
    }
    [Fact] public async Task RapidRelativeSeeksAccumulateAndOnlyLastRuns()
    {
        var calls=new List<long>();var seek=new SeekCoordinator(target=>{calls.Add(target);return Task.CompletedTask;},40);
        var first=seek.JumpAsync(10000,2000,100000);var second=seek.JumpAsync(10000,2000,100000);var third=seek.JumpAsync(-10000,2000,100000);
        await Task.WhenAll(first,second,third);Assert.Equal(new long[]{12000},calls);Assert.Null(seek.Target);
    }
    [Fact] public async Task LeavingCancelsPendingSeek()
    {
        bool called=false;var seek=new SeekCoordinator(_=>{called=true;return Task.CompletedTask;},50);
        var pending=seek.RequestAsync(15000);seek.Cancel();await pending;Assert.False(called);
    }
    [Fact] public async Task QueueIsOrderedAndRecoversAfterFailure()
    {
        var queue=new SerialWorkQueue();var calls=new List<int>();using var gate=new ManualResetEventSlim();
        var first=queue.Run(()=>{gate.Wait();calls.Add(1);});var failed=queue.Run(()=>throw new IOException());var last=queue.Run(()=>calls.Add(2));
        Assert.False(last.IsCompleted);gate.Set();await first;await Assert.ThrowsAsync<IOException>(()=>failed);await last;Assert.Equal(new[]{1,2},calls);
    }
    [Fact] public async Task ProgressFlushWaitsForLastSnapshotAndPreservesOrder()
    {
        using var gate=new ManualResetEventSlim();var written=new List<long>();var writer=new ProgressWriter(s=>{gate.Wait();written.Add(s.Position);});
        var a=writer.Enqueue(new(1,9000,10000,false,"1"));var b=writer.Enqueue(new(1,1000,10000,false,"2"));var flush=writer.FlushAsync();
        Assert.False(flush.IsCompleted);gate.Set();await flush;await Task.WhenAll(a,b);Assert.Equal(new long[]{9000,1000},written);
    }
    [Fact] public async Task FailedWriteIsReportedOnFlush()
    {
        var writer=new ProgressWriter(_=>throw new IOException());await writer.Enqueue(new(1,1,2,false,"1"));await Assert.ThrowsAsync<InvalidOperationException>(writer.FlushAsync);
    }
    [Fact] public async Task StaleOpenCannotCreateNativePlayer()
    {
        var session=new PlaybackSession();var old=session.Begin();session.Begin();Assert.Null(await session.PrepareAsync([],false,old));await session.DisposeAsync();
    }
    [Fact] public void StaleProgressCannotUndoExplicitRestart()
    {
        var dir=Path.Combine(Path.GetTempPath(),"PCinema-queue-tests",Guid.NewGuid().ToString("N"));var store=new LibraryStore(dir);store.Import(Path.Combine(dir,"Show.S01E01.mkv"),new("Show",null,1,1));var ep=store.Load().Single().Episodes.Single();
        store.SaveProgress(new ProgressSnapshot(ep.Id,0,100000,false,"2026-10-23T12:00:02.0000000Z"));
        store.SaveProgress(new ProgressSnapshot(ep.Id,90000,100000,true,"2026-10-23T12:00:01.0000000Z"));
        var saved=store.Load().Single().Episodes.Single();Assert.Equal(0,saved.Position);Assert.False(saved.Watched);
    }
    [Fact] public void CatalogUpdateKeepsBoundObjects()
    {
        var old=new Series{Id=1,Episodes=[new Episode{Id=2,Position=10}]};var bound=old.Episodes[0];old.ApplyFrom(new Series{Id=1,Episodes=[new Episode{Id=2,Position=20}]});Assert.Same(bound,old.Episodes[0]);Assert.Equal(20,bound.Position);
    }
}
