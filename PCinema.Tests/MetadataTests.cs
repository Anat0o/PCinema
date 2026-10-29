using System.Net;
using System.Net.Http;
using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;
namespace PCinema.Tests;
public class MetadataTests:IDisposable
{
 readonly string dir=Path.Combine(Path.GetTempPath(),"PCinema-tests",Guid.NewGuid().ToString("N"));readonly LibraryStore store;
 public MetadataTests(){store=new(dir);}
 sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken t)=>Task.FromResult(respond(r));}
 TmdbService Service(Func<HttpRequestMessage,HttpResponseMessage> response){var s=new TmdbService(store,new HttpClient(new Handler(response)));s.SaveToken("test-token");return s;}
 static HttpResponseMessage Json(string json)=>new(HttpStatusCode.OK){Content=new StringContent(json)};
 [Fact]public async Task EpisodeOverviewFallsBackToEnglishAndPersistsWithoutChangingProgress()
 {
  store.Import(Path.Combine(dir,"Show.S01E01.mkv"),new("Show",null,1,1));var show=store.Load().Single();store.Metadata(show.Id,new(42,"Show","Show",2020,"Series description",null,null),null,null);show=store.Load().Single();var episode=show.Episodes.Single();episode.Position=12345;store.SaveProgress(episode);
  using var service=Service(r=>Json(r.RequestUri!.Query.Contains("en-US")?"""{"episodes":[{"episode_number":1,"name":"English title","overview":"English episode description","still_path":null}]}""":"""{"episodes":[{"episode_number":1,"name":"Русское название","overview":"","still_path":null}]}"""));
  await service.LoadEpisodes(show);var loaded=new LibraryStore(dir).Load().Single().Episodes.Single();Assert.Equal("Русское название",loaded.Title);Assert.Equal("English episode description",loaded.Overview);Assert.Equal(12345,loaded.Position);
 }
 [Fact]public async Task EpisodeOverviewUsesRussianWithoutFallbackAndCachesMissingDescription()
 {
  store.Import(Path.Combine(dir,"Show.S01E01.mkv"),new("Show",null,1,1));var show=store.Load().Single();store.Metadata(show.Id,new(42,"Show","Show",2020,"",null,null),null,null);show=store.Load().Single();int requests=0;
  using var service=Service(r=>{requests++;Assert.Contains("ru-RU",r.RequestUri!.Query);return Json("""{"episodes":[{"episode_number":1,"name":"Серия","overview":"Описание серии","still_path":null}]}""");});await service.LoadEpisodes(show);Assert.Equal("Описание серии",store.Load().Single().Episodes.Single().Overview);await service.LoadEpisodes(store.Load().Single());Assert.Equal(1,requests);
 }
 [Fact]public async Task InvalidTokenGivesActionableError(){var s=Service(_=>new(HttpStatusCode.Unauthorized));var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>s.Search("Dark"));Assert.Contains("токен",ex.Message);}
 [Fact]public async Task MissingImagesAndRussianDescriptionFallBackToEnglish(){store.Import(Path.Combine(dir,"Dark.S01E01.mkv"),new("Dark",null,1,1));var id=store.Load().Single().Id;var s=Service(r=>Json(r.RequestUri!.Query.Contains("en-US")?"""{"id":1,"name":"Dark","original_name":"Dark","first_air_date":"2017-01-01","overview":"English description","poster_path":null,"backdrop_path":null}""":"""{"id":1,"name":"Тьма","original_name":"Dark","first_air_date":"2017-01-01","overview":"","poster_path":null,"backdrop_path":null}"""));await s.Apply(id,new(1,"Dark","Dark",2017,"",null,null));var actual=store.Load().Single();Assert.Equal("English description",actual.Overview);Assert.Null(actual.Poster);Assert.Equal("Тьма",actual.Title);}
 [Fact]public async Task AmbiguousMatchesStayUnmatched(){store.Import(Path.Combine(dir,"Dark.S01E01.mkv"),new("Dark",null,1,1));var s=Service(_=>Json("""{"results":[{"id":1,"name":"Dark","original_name":"Dark","first_air_date":"2017-01-01"},{"id":2,"name":"Dark","original_name":"Dark","first_air_date":"2020-01-01"}]}"""));await s.AutoMatch();Assert.Null(store.Load().Single().TmdbId);}
 [Fact]public async Task OfflineDoesNotRemoveLibrary(){store.Import(Path.Combine(dir,"Dark.S01E01.mkv"),new("Dark",null,1,1));var s=Service(_=>throw new HttpRequestException("offline"));await Assert.ThrowsAsync<HttpRequestException>(()=>s.AutoMatch());Assert.Single(store.Load().Single().Episodes);}
 [Fact]public void TokenIsNotStoredAsPlaintext(){var s=Service(_=>Json("{}"));Assert.DoesNotContain("test-token",store.Setting("tmdb_token")!);Assert.True(s.Connected);s.SaveToken("");Assert.False(s.Connected);}
 public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
}
