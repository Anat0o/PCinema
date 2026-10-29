using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.IO;
namespace PCinema;

public sealed class CoverageScreen : Screen
{
    public event Action<string>? SummaryChanged;
    readonly LibraryStore store;readonly TmdbService tmdb;readonly Series series;
    readonly TextBlock status=Ui.Text("Загружаем список серий…");readonly TextBlock summary=Ui.Text("",20,true);readonly ListBox list=new();
    readonly CheckBox specials=new(){Content="Учитывать спецвыпуски (сезон 0)",Foreground=System.Windows.Media.Brushes.White,Margin=new(0,0,0,16)};
    readonly bool embeddedView;readonly Button refresh;EpisodeCatalog? catalog;List<Episode> local=[];bool busy;
    public CoverageScreen(MainWindow host,LibraryStore store,TmdbService tmdb,Series series,bool embedded=false):base(host)
    {
        this.store=store;this.tmdb=tmdb;this.series=series;embeddedView=embedded;
        var root=new DockPanel{Margin=embedded?new Thickness(12):new Thickness(32,100,32,24)};var header=new StackPanel();if(!embedded){header.Children.Add(Ui.Text("Наличие серий",32,true));header.Children.Add(Ui.Text(series.Title,22,true));}header.Children.Add(Ui.Text("Сравниваем доступные файлы со списком TMDB. Будущие серии и серии без даты выхода показаны отдельно."));
        refresh=Ui.Button("Обновить проверку",()=>_=Check());refresh.HorizontalAlignment=HorizontalAlignment.Left;header.Children.Add(refresh);header.Children.Add(specials);header.Children.Add(status);header.Children.Add(summary);DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);root.Children.Add(list);Content=root;
        var template=new DataTemplate(typeof(EpisodeCoverageRow));var panel=new FrameworkElementFactory(typeof(StackPanel));panel.SetValue(FrameworkElement.MarginProperty,new Thickness(12,8,12,8));var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new Binding("Label"));title.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);panel.AppendChild(title);var detail=new FrameworkElementFactory(typeof(TextBlock));detail.SetBinding(TextBlock.TextProperty,new Binding("Status"));detail.SetValue(TextBlock.ForegroundProperty,Ui.Muted);detail.SetValue(FrameworkElement.MarginProperty,new Thickness(0,6,0,0));panel.AppendChild(detail);template.VisualTree=panel;list.ItemTemplate=template;
        specials.Checked+=(_,_)=>Render();specials.Unchecked+=(_,_)=>Render();Loaded+=async(_,_)=>await Check();
    }
    async Task Check()
    {
        if(busy)return;
        if(series.TmdbId is not int id){status.Text="Сначала сопоставьте сериал с TMDB через меню ⋯ на странице тайтла.";refresh.IsEnabled=false;return;}
        busy=true;refresh.IsEnabled=false;status.Text="Получаем сезоны и серии из TMDB…";
        try
        {
            var result=await Task.Run(async()=>
            {
                var cached=tmdb.CachedEpisodeCatalog(id);bool fresh=false;
                try{cached=await tmdb.FetchEpisodeCatalog(id);fresh=true;}catch(Exception ex) when(ex is System.Net.Http.HttpRequestException or OperationCanceledException or InvalidOperationException){PerformanceLog.Write("catalog.tmdb.unavailable",error:ex);if(cached==null)throw;}
                var latest=store.LoadCatalog(true).FirstOrDefault(s=>s.TmdbId==id);
                var files=latest?.Episodes??[];foreach(var ep in files)ep.Available=ep.Available&&File.Exists(ep.Path);
                return (Catalog:cached,Fresh:fresh,Files:files);
            });
            catalog=result.Catalog;local=result.Files;
            status.Text=result.Fresh?"Список TMDB обновлён. Проверена доступность локальных файлов.":$"TMDB недоступен. Используется сохранённый список от {catalog!.RetrievedAt.ToLocalTime():dd.MM.yyyy}. Он может быть неполным.";
            Render();
        }
        catch(Exception ex){status.Text="Не удалось получить список серий. Проверьте подключение TMDB и интернет, затем повторите проверку.";_=Task.Run(()=>PerformanceLog.Write("catalog.compare.failed",error:ex));}
        finally{busy=false;refresh.IsEnabled=true;}
    }
    void Render()
    {
        if(catalog==null)return;
        var result=EpisodeCoverageResult.Compare(catalog.Episodes,local,DateOnly.FromDateTime(DateTime.Now),specials.IsChecked==true);list.ItemsSource=embeddedView?result.Rows.Where(r=>r.Status=="Нет локально").ToList():result.Rows;
        SummaryChanged?.Invoke(result.Released>0?$"Локально {result.Present} из {result.Released} · отсутствует {result.Missing}":"Нет данных о вышедших сериях");
        summary.Text=result.Released==0?"В TMDB пока нет серий с подтверждённой прошедшей датой выхода.":result.Missing==0?$"Все вышедшие серии из этого списка есть локально: {result.Present} из {result.Released}.":$"Есть локально: {result.Present} из {result.Released}. Не хватает: {result.Missing}.";
        summary.Text+=$" Будущих: {result.Upcoming}; без даты: {result.Unknown}.";
        if(series.Episodes.Any(e=>!e.Season.HasValue||!e.Number.HasValue))summary.Text+=" Есть файлы без номера: исправьте распознавание для точного сравнения.";
    }
}
