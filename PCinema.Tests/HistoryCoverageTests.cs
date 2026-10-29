using System.IO;
using System.Net;
using System.Net.Http;
using Xunit;
namespace PCinema.Tests;
public sealed class HistoryCoverageTests
{
    [Fact] public void HistoryShowsLatestWatchedEpisodePerTitleWithoutDiscardingOtherProgress()
    {
        var first=new Episode{Id=1,Season=1,Number=8,Position=8000,LastPlayed="2026-10-22T12:00:00Z"};
        var latest=new Episode{Id=2,Season=1,Number=2,Position=123456,LastPlayed="2026-10-23T12:00:00Z"};
        var series=new Series{Id=1,Title="Show",Episodes=[first,latest,new(){Id=3,Season=1,Number=9}]};
        var row=Assert.Single(HistoryEntry.From([series]));Assert.Same(latest,row.Episode);Assert.Equal("23.10.2026",row.DateLabel);Assert.Equal(8000,first.Position);Assert.Equal(123456,latest.Position);Assert.Equal(3,row.Series.Episodes.Count);
    }
    static LibraryStore Store()=>new(Path.Combine(Path.GetTempPath(),"PCinema-history-coverage",Guid.NewGuid().ToString("N")));
    [Fact] public void DeletedFilesRemainInHistoryWithExactPositionAndMetadata()
    {
        var store=Store();store.Import("C:/Archive/Show.S02E03.mkv",new("Show",null,2,3));var s=store.Load().Single();var ep=s.Episodes.Single();ep.Position=123456;store.SaveProgress(ep);store.Execute("INSERT INTO removed_files VALUES($id)",("$id",ep.Id));
        Assert.Empty(store.LoadCatalog());var entry=Assert.Single(HistoryEntry.From(store.LoadCatalog(true)));Assert.Equal("Show",entry.Title);Assert.Equal(123456,entry.Episode.Position);Assert.Equal(2,entry.Episode.Season);Assert.Equal(3,entry.Episode.Number);Assert.False(entry.Episode.Available);Assert.DoesNotContain("00:02",entry.Detail);
    }
    [Fact] public void RemovedSourceRemainsInHistoryButIsNotPlayable()
    {
        var store=Store();store.AddFolder("C:/Archive");store.Import("C:/Archive/Show.S01E01.mkv",new("Show",null,1,1));var ep=store.Load().Single().Episodes.Single();ep.Position=100;store.SaveProgress(ep);store.RemoveFolder(store.Folders().Single());Assert.Empty(store.LoadCatalog());Assert.False(Assert.Single(HistoryEntry.From(store.LoadCatalog(true))).Episode.Available);
    }
    [Fact] public void CoverageUsesNumbersNotFileCountAndSeparatesFutureUnknownAndSpecials()
    {
        RemoteEpisode[] remote=[new(1,1,"One",new(2020,1,1)),new(1,2,"Two",new(2020,1,2)),new(2,1,"Future",new(2099,1,1)),new(2,2,"Unknown",null),new(0,1,"Special",new(2020,1,1))];
        Episode[] local=[new(){Season=1,Number=1,Available=true},new(){Season=1,Number=1,Available=true},new(){Season=1,Number=2,Available=false}];
        var result=EpisodeCoverageResult.Compare(remote,local,new(2026,10,23));Assert.Equal(2,result.Released);Assert.Equal(1,result.Present);Assert.Equal(1,result.Missing);Assert.Equal(1,result.Upcoming);Assert.Equal(1,result.Unknown);Assert.Equal(4,result.Rows.Count);
        Assert.Equal(2,EpisodeCoverageResult.Compare(remote,local,new(2026,10,23),true).Missing);
    }
    sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> reply):HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>Task.FromResult(reply(request));}
    static HttpResponseMessage Json(string value)=>new(HttpStatusCode.OK){Content=new StringContent(value)};
    [Fact] public async Task TmdbChecksSeasonsThatHaveNoLocalFilesAndCachesCompleteList()
    {
        var store=Store();using var client=new HttpClient(new Handler(r=>r.RequestUri!.AbsolutePath.EndsWith("/tv/42")?Json("""{"seasons":[{"season_number":1},{"season_number":2}]}"""):Json("""{"episodes":[{"episode_number":1,"name":"Episode","air_date":"2020-01-01"}]}""")));
        using var tmdb=new TmdbService(store,client,"test");var catalog=await tmdb.FetchEpisodeCatalog(42);Assert.Equal(new[]{1,2},catalog.Episodes.Select(e=>e.Season));Assert.Equal(2,tmdb.CachedEpisodeCatalog(42)!.Episodes.Count);
    }
    [Fact] public async Task PartialNetworkFailureDoesNotReplaceCachedCatalog()
    {
        var store=Store();var saved=new EpisodeCatalog(42,DateTime.UtcNow,[new(1,1,"Saved",new(2020,1,1))]);store.Setting("tmdb_episode_catalog:42",System.Text.Json.JsonSerializer.Serialize(saved));
        using var client=new HttpClient(new Handler(r=>r.RequestUri!.AbsolutePath.EndsWith("/tv/42")?Json("""{"seasons":[{"season_number":1}]}"""):throw new HttpRequestException()));using var tmdb=new TmdbService(store,client,"test");await Assert.ThrowsAsync<HttpRequestException>(()=>tmdb.FetchEpisodeCatalog(42));Assert.Equal("Saved",tmdb.CachedEpisodeCatalog(42)!.Episodes.Single().Title);
    }
}
