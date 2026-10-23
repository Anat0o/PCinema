namespace PCinema;

public sealed record RemoteEpisode(int Season,int Number,string Title,DateOnly? AirDate)
{
    public string Label=>$"S{Season:00} · E{Number:00}";
}
public sealed record EpisodeCatalog(int TmdbId,DateTime RetrievedAt,List<RemoteEpisode> Episodes);
public sealed record EpisodeCoverageRow(RemoteEpisode Episode,string Status)
{
    public string Label=>$"{Episode.Label}  ·  {Episode.Title}";
}
public sealed record EpisodeCoverageResult(List<EpisodeCoverageRow> Rows,int Released,int Present,int Missing,int Upcoming,int Unknown)
{
    public static EpisodeCoverageResult Compare(IEnumerable<RemoteEpisode> remote,IEnumerable<Episode> local,DateOnly today,bool specials=false)
    {
        var available=local.Where(e=>e.Available&&e.Season.HasValue&&e.Number.HasValue).Select(e=>(e.Season!.Value,e.Number!.Value)).ToHashSet();
        int released=0,present=0,missing=0,upcoming=0,unknown=0;var rows=new List<EpisodeCoverageRow>();
        foreach(var ep in remote.Where(e=>specials||e.Season>0).DistinctBy(e=>(e.Season,e.Number)).OrderBy(e=>e.Season).ThenBy(e=>e.Number))
        {
            bool found=available.Contains((ep.Season,ep.Number));string status;
            if(ep.AirDate==null){unknown++;status=found?"Есть локально · дата выхода неизвестна":"Дата выхода неизвестна";}
            else if(ep.AirDate>today){upcoming++;status=found?"Есть локально · будущая дата TMDB":"Ещё не вышла";}
            else{released++;if(found){present++;status="Есть локально";}else{missing++;status="Нет локально";}}
            rows.Add(new(ep,status));
        }
        return new(rows,released,present,missing,upcoming,unknown);
    }
}
