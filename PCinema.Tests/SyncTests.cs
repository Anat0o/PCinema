using System.IO;
using Microsoft.Data.Sqlite;
using PCinema.Sync;
using Xunit;
namespace PCinema.Tests;

public sealed class SyncTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"PCinema-sync-"+Guid.NewGuid());
    LibraryStore Store(string name)=>new(Path.Combine(root,name));
    sealed class Drive:ISyncTransport
    {
        public List<SyncDocument> Documents=[];
        public bool Offline;
        public Action? DuringRead,DuringWrite;
        public Task<IReadOnlyList<SyncDocument>> ReadAsync(CancellationToken token)
        {token.ThrowIfCancellationRequested();if(Offline)throw new IOException("offline");DuringRead?.Invoke();return Task.FromResult<IReadOnlyList<SyncDocument>>(Documents.ToArray());}
        public Task WriteAsync(SyncDocument d,CancellationToken token)
        {token.ThrowIfCancellationRequested();if(Offline)throw new IOException("offline");DuringWrite?.Invoke();Documents.RemoveAll(x=>x.Device==d.Device&&x.Generation==d.Generation);Documents.Add(d);return Task.CompletedTask;}
    }
    static Episode Watch(LibraryStore s,long position=50000)
    {
        s.Import("C:\\private\\Example.S01E02.mkv",new("Example",2020,1,2));
        var series=s.Load().Single();s.Metadata(series.Id,new(123,"Example","Example",2020,"Description",null,null),null,null);
        var ep=s.Load().Single().Episodes.Single();new ProgressService(s).Save(ep,position,100000);return ep;
    }
    [Fact] public async Task TwoProfilesRestoreHistoryWithoutPathsAndResumeLocalCopy()
    {
        var a=Store("a");var b=Store("b");Watch(a);a.Setting("autoplay_next","false");a.Setting("tmdb_token","secret");a.AddFolder("C:\\private");
        var drive=new Drive();var ca=new SyncCoordinator(a,drive,"sub");var cb=new SyncCoordinator(b,drive,"sub");
        await ca.ConnectAsync(ConnectChoice.Merge);await cb.ConnectAsync(ConnectChoice.RestoreCloud);
        var ep=b.LoadCatalog(true).Single().Episodes.Single();Assert.Equal(50000,ep.Position);Assert.False(ep.Available);Assert.Empty(ep.Path);Assert.Empty(b.LoadCatalog());Assert.Equal("false",b.Setting("autoplay_next"));
        var json=SyncMerge.Serialize(drive.Documents);Assert.DoesNotContain("private",json);Assert.DoesNotContain("secret",json);Assert.DoesNotContain("tmdb_token",json);
        b.Import("D:\\Example.S01E02.mp4",new("Example",2020,1,2));Assert.Equal(50000,b.LoadCatalog().Single().Episodes.Single().Position);
    }
    [Fact] public async Task BackwardSeekWinsAndRepeatedDeliveryDoesNotCreateNewVersions()
    {
        var a=Store("a");var b=Store("b");var ep=Watch(a);var drive=new Drive();var ca=new SyncCoordinator(a,drive,"sub");var cb=new SyncCoordinator(b,drive,"sub");
        await ca.ConnectAsync(ConnectChoice.Merge);await cb.ConnectAsync(ConnectChoice.Merge);
        new ProgressService(a).Save(ep,1000,100000);await ca.SynchronizeAsync();await cb.SynchronizeAsync();Assert.Equal(1000,b.Load().Single().Episodes.Single().Position);
        var before=b.SyncEntries().OrderBy(e=>e.Key).ToArray();await cb.SynchronizeAsync();Assert.Equal(before,b.SyncEntries().OrderBy(e=>e.Key).ToArray());
    }
    [Fact] public async Task OfflineQueueSurvivesRestartAndReplacementRequiresExplicitChoice()
    {
        var a=Store("a");var b=Store("b");Watch(a);var drive=new Drive();var ca=new SyncCoordinator(a,drive,"sub");var cb=new SyncCoordinator(b,drive,"sub");
        await ca.ConnectAsync(ConnectChoice.Merge);await cb.ConnectAsync(ConnectChoice.Merge);
        drive.Offline=true;b.Setting("autoplay_next","false");await Assert.ThrowsAsync<IOException>(()=>cb.SynchronizeAsync());Assert.True(cb.Session!.Pending);
        drive.Offline=false;cb=new(Store("b"),drive,"sub");await cb.SynchronizeAsync();Assert.False(cb.Session!.Pending);
        await ca.ConnectAsync(ConnectChoice.ReplaceCloud);await Assert.ThrowsAsync<GenerationChangedException>(()=>cb.SynchronizeAsync());await cb.ConnectAsync(ConnectChoice.RestoreCloud);Assert.Equal(ca.Session!.Generation,cb.Session!.Generation);
    }
    [Fact] public async Task ChangesDuringNetworkRequestsAreNotLost()
    {
        var a=Store("a");var ep=Watch(a);var drive=new Drive();var sync=new SyncCoordinator(a,drive,"sub");await sync.ConnectAsync(ConnectChoice.Merge);
        drive.DuringRead=()=>new ProgressService(a).Save(ep,2000,100000);await sync.SynchronizeAsync();Assert.Equal(2000,a.Load().Single().Episodes.Single().Position);drive.DuringRead=null;
        drive.DuringWrite=()=>new ProgressService(a).Save(ep,3000,100000);await sync.SynchronizeAsync();Assert.True(sync.Session!.Pending);drive.DuringWrite=null;await sync.SynchronizeAsync();Assert.False(sync.Session!.Pending);
    }
    [Fact] public async Task UnknownFormatCannotOverwriteCloudOrLocalHistory()
    {
        var a=Store("a");Watch(a);var drive=new Drive();drive.Documents.Add(new(99,"future",ChangeStamp.Zero,"other",[]));
        await Assert.ThrowsAsync<InvalidDataException>(()=>new SyncCoordinator(a,drive,"sub").ConnectAsync(ConnectChoice.RestoreCloud));Assert.Equal(50000,a.Load().Single().Episodes.Single().Position);Assert.Equal(99,drive.Documents.Single().Schema);
    }
    [Fact] public async Task UnrecognizedTitlesDoNotMergeByName()
    {
        var a=Store("a");var b=Store("b");a.Import("C:\\Unknown.S01E01.mkv",new("Unknown",null,1,1));new ProgressService(a).Save(a.Load().Single().Episodes.Single(),100,1000);
        var drive=new Drive();await new SyncCoordinator(a,drive,"sub").ConnectAsync(ConnectChoice.Merge);await new SyncCoordinator(b,drive,"sub").ConnectAsync(ConnectChoice.Merge);
        b.Import("D:\\Unknown.S01E01.mkv",new("Unknown",null,1,1));Assert.Equal(2,b.Load().Count);Assert.Equal(0,b.LoadCatalog().Single().Episodes.Single().Position);
    }
    [Fact] public void MergeUsesLogicalClockAndDeviceTieBreakAndKeepsTombstones()
    {
        var entries=new[]{new SyncEntry("setting:autoplay_next","true",new(10,0,"z")),new SyncEntry("setting:autoplay_next",null,new(10,1,"a"),true)};
        Assert.True(SyncMerge.Merge(entries).Single().Deleted);Assert.Equal(SyncMerge.Merge(entries),SyncMerge.Merge(entries.Reverse()));
        Assert.Throws<ArgumentException>(()=>Store("a").SetSyncValue("setting:tmdb_token","secret"));
    }
    [Fact] public void MigrationBacksUpAndPreservesExistingIds()
    {
        var a=Store("a");var ep=Watch(a);a.Execute("DROP TABLE sync_records; DROP TABLE sync_meta; DROP TABLE sync_identity; DROP TABLE sync_setting_versions;");
        var migrated=Store("a");Assert.Equal(ep.Id,migrated.Load().Single().Episodes.Single().Id);Assert.Equal(50000,migrated.Load().Single().Episodes.Single().Position);Assert.NotEmpty(Directory.GetFiles(Path.Combine(root,"a","backups"),"before-sync-*.db"));
    }
    [Fact] public async Task ClockSkewDoesNotPreventLocalRestart()
    {
        var a=Store("a");Watch(a);var drive=new Drive();var ca=new SyncCoordinator(a,drive,"sub");await ca.ConnectAsync(ConnectChoice.Merge);
        var h=a.SyncEntries().Single(e=>e.Key.StartsWith("history:"));var data=System.Text.Json.JsonSerializer.Deserialize<HistoryData>(h.Value!)!;
        var future=DateTimeOffset.UtcNow.AddDays(1);var changed=h with{Stamp=new(future.ToUnixTimeMilliseconds(),0,"future"),Value=SyncMerge.Serialize(data with{LastPlayed=future.UtcDateTime.ToString("O"),Position=90000})};
        drive.Documents.Add(new(1,ca.Session!.Generation,ca.Session.GenerationStamp,"future",[changed]));await ca.SynchronizeAsync();
        new ProgressService(a).Save(a.Load().Single().Episodes.Single(),0,100000);await ca.SynchronizeAsync();Assert.Equal(0,a.Load().Single().Episodes.Single().Position);
    }
    [Fact] public async Task ManualTmdbMatchCarriesUuidHistoryAcrossProfiles()
    {
        var a=Store("a");var b=Store("b");a.Import("C:\\Unknown.S01E01.mkv",new("Unknown",null,1,1));new ProgressService(a).Save(a.Load().Single().Episodes.Single(),100,1000);
        var drive=new Drive();var ca=new SyncCoordinator(a,drive,"sub");var cb=new SyncCoordinator(b,drive,"sub");await ca.ConnectAsync(ConnectChoice.Merge);await cb.ConnectAsync(ConnectChoice.Merge);
        a.Metadata(a.Load().Single().Id,new(123,"Recognized","Recognized",2020,"Description",null,null),null,null);await ca.SynchronizeAsync();await cb.SynchronizeAsync();
        Assert.Single(b.LoadCatalog(true));Assert.Equal(123,b.LoadCatalog(true).Single().TmdbId);Assert.Equal(100,b.LoadCatalog(true).Single().Episodes.Single().Position);
    }
    [Fact] public async Task ProgressTombstoneRemovesHistoryWithoutDeletingMedia()
    {
        var a=Store("a");Watch(a);a.CaptureSync();var entry=a.SyncEntries().Single(e=>e.Key.StartsWith("history:"));a.ApplySync([entry with{Value=null,Deleted=true,Stamp=a.NextStamp()}]);
        Assert.Equal(0,a.Load().Single().Episodes.Single().Position);Assert.Empty(a.Load().Single().Episodes.Single().LastPlayed);Assert.NotEmpty(a.Load().Single().Episodes.Single().Path);await Task.CompletedTask;
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
