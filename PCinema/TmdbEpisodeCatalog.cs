using System.Text.Json;
namespace PCinema;
public sealed partial class TmdbService
{
    public EpisodeCatalog? CachedEpisodeCatalog(int id)
    {
        try{return JsonSerializer.Deserialize<EpisodeCatalog>(store.Setting($"tmdb_episode_catalog:{id}")??"null");}catch(JsonException){return null;}
    }
    public async Task<EpisodeCatalog> FetchEpisodeCatalog(int id)
    {
        using var show=await Get($"tv/{id}?language=ru-RU");
        if(!show.RootElement.TryGetProperty("seasons",out var seasons))throw new InvalidOperationException("TMDB не предоставил список сезонов.");
        var episodes=new List<RemoteEpisode>();
        foreach(int season in seasons.EnumerateArray().Select(s=>s.GetProperty("season_number").GetInt32()).Distinct().Order())
        {
            using var detail=await Get($"tv/{id}/season/{season}?language=ru-RU");
            if(!detail.RootElement.TryGetProperty("episodes",out var list))throw new InvalidOperationException("TMDB не предоставил полный список серий.");
            foreach(var e in list.EnumerateArray())episodes.Add(new(season,e.GetProperty("episode_number").GetInt32(),Str(e,"name"),DateOnly.TryParse(Str(e,"air_date"),out var date)?date:null));
        }
        var catalog=new EpisodeCatalog(id,DateTime.UtcNow,episodes);
        store.Setting($"tmdb_episode_catalog:{id}",JsonSerializer.Serialize(catalog));return catalog;
    }
}
