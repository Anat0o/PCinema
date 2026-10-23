using System.Collections.ObjectModel;
namespace PCinema;

public sealed class LibraryViewModel(LibraryStore store) : Observable
{
    public ObservableCollection<Series> All { get; } = []; public ObservableCollection<Series> Visible { get; } = []; public ObservableCollection<Series> Continuing { get; } = []; public ObservableCollection<Series> Recent { get; } = [];
    string search = "", filter = "Все", status = "Ваша коллекция. Ваш ритм.";
    public string Search { get => search; set { search = value; Changed(); ApplyFilter(); } }
    public string Filter { get => filter; set { filter = value; Changed(); ApplyFilter(); } }
    public string Status { get => status; set { status = value; Changed(); } }
    public Series? Hero => Continuing.FirstOrDefault() ?? Recent.FirstOrDefault();
    public bool Empty => All.Count == 0; public string HeroTitle => Hero?.Title ?? "Ваш личный кинотеатр";
    public string HeroOverview => Hero?.Overview ?? "Добавьте папку с сериалами. PCinema соберёт вашу коллекцию и запомнит, где вы остановились.";
    public string HeroAction => Hero == null ? "Добавить сериалы" : Hero.State == "В процессе" ? "Продолжить" : "Смотреть";
    public void Reload(List<Series>? data=null)
    {
        var old=All.ToDictionary(s=>s.Id);var merged=new List<Series>();
        foreach(var next in data??store.LoadCatalog()){if(old.TryGetValue(next.Id,out var existing)){existing.ApplyFrom(next);merged.Add(existing);}else merged.Add(next);}
        Sync(All,merged);Sync(Continuing,All.Where(x=>x.State=="В процессе").OrderByDescending(x=>x.Episodes.Max(e=>e.LastPlayed)).Take(24));
        Sync(Recent,All.OrderByDescending(x=>x.Episodes.Max(e=>e.Id)).Take(12));ApplyFilter();
        Changed(nameof(Hero));Changed(nameof(HeroTitle));Changed(nameof(HeroOverview));Changed(nameof(Empty));Changed(nameof(HeroAction));
    }
    static void Sync(ObservableCollection<Series> target,IEnumerable<Series> items)
    {
        var desired=items.ToList();var ids=desired.Select(s=>s.Id).ToHashSet();for(int i=target.Count-1;i>=0;i--)if(!ids.Contains(target[i].Id))target.RemoveAt(i);
        for(int i=0;i<desired.Count;i++){if(i<target.Count&&target[i].Id==desired[i].Id)continue;int at=target.IndexOf(desired[i]);if(at<0)target.Insert(i,desired[i]);else target.Move(at,i);}
    }
    void ApplyFilter()=>Sync(Visible,All.Where(x=>(Filter=="Все"||x.State==Filter)&&(x.Title.Contains(Search,StringComparison.OrdinalIgnoreCase)||x.SearchTitle.Contains(Search,StringComparison.OrdinalIgnoreCase))).OrderBy(x=>x.Title));
}
