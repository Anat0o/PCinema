using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;
namespace PCinema.Tests;

public sealed class CatalogHistoryTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"PCinema-history-tests",Guid.NewGuid().ToString("N"));
    readonly LibraryStore store;
    public CatalogHistoryTests(){store=new(dir);}
    Series Add(string name,int tmdb,int season=1,int number=1)
    {
        var path=Path.Combine(dir,name+".mkv");File.WriteAllBytes(path,[1,2,3]);store.Import(path,new(name,null,season,number));
        var series=store.Load().Single(s=>s.SearchTitle==name);
        store.Metadata(series.Id,new(tmdb,"Recognized show","Original",2020,"",null,null),null,null);
        return store.Load().Single(s=>s.Id==series.Id);
    }
    [Fact] public void DifferentFilesShareTmdbEpisodeHistoryAndExplicitRestart()
    {
        var a=Add("First release",100);var original=a.Episodes.Single();new ProgressService(store).Save(original,45000,100000);
        var b=Add("Replacement release",100);var replacement=b.Episodes.Single();
        Assert.Equal(45000,replacement.Position);Assert.Single(store.LoadCatalog());Assert.Single(store.LoadCatalog().Single().Episodes);
        replacement.Position=0;replacement.Watched=false;store.SaveProgress(replacement);
        Assert.All(new LibraryStore(dir).Load().SelectMany(s=>s.Episodes),e=>Assert.Equal(0,e.Position));
    }
    [Fact] public void DifferentShowSeasonAndEpisodeRemainIndependent()
    {
        var a=Add("A",100);new ProgressService(store).Save(a.Episodes.Single(),96000,100000);
        Assert.Equal(0,Add("B",101).Episodes.Single().Position);
        Assert.Equal(0,Add("C",100,2,1).Episodes.Single().Position);
        Assert.Equal(0,Add("D",100,1,2).Episodes.Single().Position);
    }
    [Fact] public void RecognizingLocalSeriesCarriesHistory()
    {
        var path=Path.Combine(dir,"Local.mkv");store.Import(path,new("Local",null,1,1));var s=store.Load().Single();
        new ProgressService(store).Save(s.Episodes.Single(),42000,100000);
        store.Metadata(s.Id,new(55,"Show","Show",2020,"",null,null),null,null);
        Assert.Equal(42000,Add("Other release",55).Episodes.Single().Position);
    }
    [Fact] public async Task DeletedDirectoryDisappearsButHistorySurvivesReplacement()
    {
        var folder=Path.Combine(dir,"media");Directory.CreateDirectory(folder);store.AddFolder(folder);
        var path=Path.Combine(folder,"Show.S01E01.mkv");File.WriteAllBytes(path,[1,2,3]);
        using var scanner=new LibraryScanner(store);await scanner.ScanAsync();var s=store.Load().Single();
        store.Metadata(s.Id,new(42,"Show","Show",2020,"",null,null),null,null);
        new ProgressService(store).Save(s.Episodes.Single(),30000,100000);
        File.Delete(path);Directory.Delete(folder);await scanner.ScanAsync();
        Assert.Empty(store.LoadCatalog());Assert.Equal(30000,store.Load().Single().Episodes.Single().Position);
        Assert.Equal(30000,Add("New release",42).Episodes.Single().Position);
        Assert.Single(store.LoadCatalog().Single().Episodes);
    }
    [Fact] public async Task OfflineDriveRemainsVisibleAndReconnectRestoresAvailability()
    {
        var s=Add("Offline",10);new ProgressService(store).Save(s.Episodes.Single(),25000,100000);
        var presence=FilePresence.Unavailable;using var scanner=new LibraryScanner(store,_=>presence);
        await scanner.ScanAsync();Assert.False(store.LoadCatalog().Single().Episodes.Single().Available);
        Assert.Equal(25000,store.LoadCatalog().Single().Episodes.Single().Position);
        presence=FilePresence.Present;await scanner.ScanAsync();Assert.True(store.LoadCatalog().Single().Episodes.Single().Available);
    }
    [Fact] public async Task PartialDeletionRemovesOnlyMissingEpisodeAndReturningFileReappears()
    {
        var first=Add("One",22);Add("Two",22,1,2);var path=first.Episodes.Single().Path;
        using var scanner=new LibraryScanner(store);File.Delete(path);await scanner.ScanAsync();
        Assert.Equal(2,store.LoadCatalog().Single().Episodes.Single().Number);
        File.WriteAllBytes(path,[4,5,6]);await scanner.ScanAsync();Assert.Equal(2,store.LoadCatalog().Single().Episodes.Count);
    }
    [Fact] public void VersionTwoMigrationKeepsLatestHistoryAndWatchedState()
    {
        var s=Add("Legacy",77);new ProgressService(store).Save(s.Episodes.Single(),97000,100000);
        store.Execute("DROP TABLE watch_history; DROP TABLE removed_files; PRAGMA user_version=2;");
        var migrated=new LibraryStore(dir);var ep=migrated.Load().Single().Episodes.Single();
        Assert.Equal(97000,ep.Position);Assert.True(ep.Watched);
        Assert.Equal(97000,Add("New encode",77).Episodes.Single().Position);
    }
    [Fact] public async Task WatcherAutomaticallyPublishesDeletionWithoutManualRescan()
    {
        var s=Add("Watched folder",81);store.AddFolder(dir);
        using var scanner=new LibraryScanner(store);scanner.Watch();
        var updated=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scanner.Updated+=()=>updated.TrySetResult();
        File.Delete(s.Episodes.Single().Path);
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(12));
        Assert.Empty(store.LoadCatalog());
    }
    [Fact] public void RemovingSourceHidesFilesWithoutDeletingHistoryAndAddingItRestoresThem()
    {
        var s=Add("Source show",12);store.AddFolder(dir);var ep=s.Episodes.Single();new ProgressService(store).Save(ep,12345,100000);
        store.RemoveFolder(store.Folders().Single());Assert.Empty(store.LoadCatalog());Assert.True(File.Exists(ep.Path));
        Assert.Equal(12345,store.Load().Single().Episodes.Single().Position);
        store.AddFolder(dir);Assert.Equal(12345,store.LoadCatalog().Single().Episodes.Single().Position);
    }
    [Fact] public void RemovingParentSourceKeepsFilesFromTrackedChild()
    {
        var child=Path.Combine(dir,"child");Directory.CreateDirectory(child);var file=Path.Combine(child,"Show.S01E01.mkv");File.WriteAllBytes(file,[1]);store.Import(file,new("Show",null,1,1));
        store.AddFolder(dir);store.AddFolder(child);store.RemoveFolder(store.Folders().Single(f=>f.Path==dir));Assert.Single(store.LoadCatalog());
    }
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
}
