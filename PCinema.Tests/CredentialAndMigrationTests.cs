using System.Net;
using System.Net.Http;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;
namespace PCinema.Tests;
public class CredentialAndMigrationTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"PCinema-tests",Guid.NewGuid().ToString("N"));readonly LibraryStore store;
    public CredentialAndMigrationTests(){store=new(dir);}
    sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>Task.FromResult(respond(request));}
    [Fact] public async Task DefaultOverrideAndDisabledHaveExplicitPrecedence()
    {
        var seen=new List<string?>();
        using var service=new TmdbService(store,new HttpClient(new Handler(r=>{seen.Add(r.Headers.Authorization?.Parameter);return new(HttpStatusCode.OK){Content=new StringContent("{\"results\":[]}")};})),"bundled-test");
        Assert.Equal("builtin",service.Mode);await service.Search("test");
        service.SaveToken("personal-test");await service.Search("test");
        service.SetMode("off");Assert.False(service.Connected);await Assert.ThrowsAsync<InvalidOperationException>(()=>service.Search("test"));
        service.SetMode("builtin");await service.Search("test");
        Assert.Equal(new[]{"bundled-test","personal-test","bundled-test"},seen);
    }
    [Fact] public async Task InvalidPersonalTokenDoesNotFallBack()
    {
        int requests=0;
        using var service=new TmdbService(store,new HttpClient(new Handler(r=>{requests++;Assert.Equal("bad-personal",r.Headers.Authorization?.Parameter);return new(HttpStatusCode.Unauthorized);} )),"bundled-test");
        service.SaveToken("bad-personal");await Assert.ThrowsAsync<InvalidOperationException>(()=>service.Search("test"));Assert.Equal(1,requests);Assert.Equal("custom",service.Mode);
    }
    [Fact] public void LegacyCustomTokenRemainsSelected()
    {
        using(var service=new TmdbService(store,builtInToken:"bundled-test"))service.SaveToken("legacy");
        store.Execute("DELETE FROM settings WHERE key='tmdb_mode'");
        using var reloaded=new TmdbService(store,builtInToken:"bundled-test");Assert.Equal("custom",reloaded.Mode);
    }
    [Fact] public void ReopeningMigrationPreservesHistoryAndMetadata()
    {
        var path=Path.Combine(dir,"Test.S01E01.mkv");store.Import(path,new("Test",2020,1,1));var series=store.Load().Single();var ep=series.Episodes.Single();new ProgressService(store).Save(ep,42000,100000);
        store.Execute("INSERT INTO series_details(series_id,genres,rating) VALUES($i,'Драма',8.5)",("$i",series.Id));
        var reloaded=new LibraryStore(dir).Load().Single();Assert.Equal(8.5,reloaded.Rating);Assert.Equal(ep.Id,reloaded.Episodes.Single().Id);Assert.Equal(42000,reloaded.Episodes.Single().Position);
    }
    [Fact] public async Task UnchangedScanDoesNotPublishLibraryRefresh()
    {
        var path=Path.Combine(dir,"Test.S01E01.mkv");await File.WriteAllBytesAsync(path,[1,2,3]);store.AddFolder(dir);
        using var scanner=new LibraryScanner(store);int updated=0;scanner.Updated+=()=>updated++;
        await scanner.ScanAsync();await scanner.ScanAsync();Assert.Equal(1,updated);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
}
