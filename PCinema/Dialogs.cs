using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using System.IO;
namespace PCinema;

public sealed class LibraryScreen : Screen
{
    readonly LibraryStore store; List<Series> data=[]; readonly ListBox rows=new(); readonly TextBox search=new(){Width=320,ToolTip="Название сериала"};
    readonly ComboBox filter=new(){ItemsSource=new[]{"Все","Не начато","В процессе","Просмотрено","Нераспознанное"},SelectedIndex=0};
    readonly DispatcherTimer debounce=new(){Interval=TimeSpan.FromMilliseconds(160)};
    readonly TextBlock empty=Ui.Text("Нет сериалов. Добавьте папку в настройках или измените поиск.");
    int columns=4;
    public LibraryScreen(MainWindow host,LibraryStore store,bool unmatched=false):base(host)
    {
        this.store=store; if(unmatched) filter.SelectedItem="Нераспознанное"; var root=new DockPanel{Margin=new(56,110,40,20)};
        var header=new StackPanel();header.Children.Add(Ui.Text("Библиотека",32,true));var bar=new WrapPanel();bar.Children.Add(search);bar.Children.Add(filter);header.Children.Add(bar);header.Children.Add(empty);
        DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);root.Children.Add(rows);Content=root;
        var template=new DataTemplate();var items=new FrameworkElementFactory(typeof(ItemsControl));items.SetBinding(ItemsControl.ItemsSourceProperty,new Binding());items.SetValue(ItemsControl.ItemTemplateProperty,host.FindResource("Card"));
        var panel=new FrameworkElementFactory(typeof(StackPanel));panel.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);items.SetValue(ItemsControl.ItemsPanelProperty,new ItemsPanelTemplate(panel));template.VisualTree=items;rows.ItemTemplate=template;
        rows.ItemContainerStyle=new Style(typeof(ListBoxItem), (Style)Application.Current.FindResource(typeof(ListBoxItem)));rows.ItemContainerStyle.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(0)));rows.ItemContainerStyle.Setters.Add(new Setter(UIElement.FocusableProperty,false));
        rows.ItemContainerStyle.Setters.Add(new Setter(Control.TemplateProperty,new ControlTemplate(typeof(ListBoxItem)){VisualTree=new FrameworkElementFactory(typeof(ContentPresenter))}));
        debounce.Tick+=(_,_)=>{debounce.Stop();Filter();};search.TextChanged+=(_,_)=>Queue();filter.SelectionChanged+=(_,_)=>Queue();
        SizeChanged+=(_,_)=>{int next=Math.Max(1,(int)((ActualWidth-110)/216));if(next!=columns){columns=next;Queue();}};
        Loaded+=(_,_)=>search.Focus();RefreshData();
    }
    void Queue(){debounce.Stop();debounce.Start();}
    public override async void RefreshData(){data=await Task.Run(store.LoadCatalog);Filter();}
    void Filter()
    {
        var f=filter.SelectedItem as string;var q=search.Text.Trim();
        var result=data.Where(s=>(f=="Все" || (f=="Нераспознанное" ? s.TmdbId==null||s.Episodes.Any(e=>!e.Number.HasValue) : s.State==f))&&(s.Title.Contains(q,StringComparison.OrdinalIgnoreCase)||s.SearchTitle.Contains(q,StringComparison.OrdinalIgnoreCase))).OrderBy(s=>s.Title).ToList();
        rows.ItemsSource=result.Chunk(columns).ToList();empty.Visibility=result.Count==0?Visibility.Visible:Visibility.Collapsed;
    }
    public override void Leave()=>debounce.Stop();
}

public sealed class SettingsWindow : Screen
{
    readonly LibraryStore store;readonly TmdbService tmdb; readonly StackPanel folders=new();
    readonly PasswordBox token=new(){Margin=new(0,12,0,12)};
    readonly TextBlock status=Ui.Text("");readonly ComboBox mode=new(){ItemsSource=new[]{"Встроенный токен","Свой токен","Без подключения"}};
    public SettingsWindow(MainWindow host,LibraryStore store,TmdbService tmdb):base(host)
    {
        this.store=store;this.tmdb=tmdb;
        var p=new StackPanel{Margin=new(64,110,64,40),MaxWidth=900,HorizontalAlignment=HorizontalAlignment.Left};
        p.Children.Add(Ui.Text("Настройки",32,true));p.Children.Add(Ui.Text("Всё для вашей коллекции. Без аккаунта и облачного хранения."));
        var library=new StackPanel();library.Children.Add(Ui.Text("Источники библиотеки",20,true));library.Children.Add(Ui.Text("Выберите папки с видео. Все вложенные папки проверяются автоматически. Исходные файлы остаются на месте.",13));library.Children.Add(folders);Reload();
        var row=new WrapPanel();row.Children.Add(Ui.Button("+ Добавить папку",()=>{var d=new OpenFolderDialog{Title="Добавьте папки с сериалами",Multiselect=true};if(d.ShowDialog(Host)==true){foreach(var path in d.FolderNames)store.AddFolder(path);Reload();Host.SettingsChanged();}},true));
        row.Children.Add(Ui.Button("Пересканировать все папки",()=>Host.SettingsChanged()));library.Children.Add(row);
        var scanStatus=Ui.Text("",12);scanStatus.SetBinding(TextBlock.TextProperty,new Binding("Status"));library.Children.Add(scanStatus);
        library.Children.Add(Ui.Button("Файлы без распознавания",()=>Host.Navigate(new LibraryScreen(Host,store,true))));
        p.Children.Add(Ui.Panel(library));
        var playback=new StackPanel();playback.Children.Add(Ui.Text("Воспроизведение",20,true));
        var auto=new CheckBox{Content="Автоматически включать следующую серию",IsChecked=store.Setting("autoplay_next")!="false",Foreground=Brushes.White,Margin=new(0,4,0,12)};
        auto.Checked+=(_,_)=>store.Setting("autoplay_next","true");auto.Unchecked+=(_,_)=>store.Setting("autoplay_next","false");playback.Children.Add(auto);
        playback.Children.Add(Ui.Text("После завершения — отсчёт 5 секунд с кнопкой отмены. Недоступные файлы пропускаются. Настройка сохраняется сразу.",13));
        playback.Children.Add(Ui.Text("В плеере: щелчок по видео — пауза, двойной щелчок — полный экран, колесо — громкость. Оформление субтитров — в меню ⚙ плеера.",13));p.Children.Add(Ui.Panel(playback));
        var api=new StackPanel();api.Children.Add(Ui.Text("Постеры и описания · TMDB",20,true));api.Children.Add(Ui.Text("Встроенный токен работает сразу. При желании используйте свой API Read Access Token."));
        mode.SelectedIndex=tmdb.Mode=="custom"?1:tmdb.Mode=="off"?2:0;api.Children.Add(mode);api.Children.Add(token);token.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        mode.SelectionChanged+=(_,_)=>token.Visibility=mode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        var buttons=new WrapPanel();buttons.Children.Add(Ui.Button("Сохранить",Save,true));
        var test=Ui.Button("Проверить подключение",async()=>{try{await tmdb.TestConnection();status.Text="TMDB доступен. Подключение работает.";}catch(Exception ex){status.Text=ex.Message;}});buttons.Children.Add(test);api.Children.Add(buttons);api.Children.Add(status);p.Children.Add(Ui.Panel(api));
        p.Children.Add(Ui.Text("PCinema 1.1.5 · Видео остаётся на вашем компьютере",15,true));
        p.Children.Add(new Image { Source=new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/PCinema;component/Assets/tmdb-logo.png")), Width=100, Height=36, Stretch=Stretch.Uniform, HorizontalAlignment=HorizontalAlignment.Left, Margin=new(0,10,0,14) });
        p.Children.Add(Ui.Text("Данные и изображения: The Movie Database (TMDB).\nThis product uses the TMDB API but is not endorsed or certified by TMDB.",12));
        Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }
    public override void RefreshData()=>Reload();
    async void Reload()
    {
        var sources=await Task.Run(()=>store.Folders().Select(f=>new{Folder=f,Available=Directory.Exists(f.Path)}).ToList());
        folders.Children.Clear();
        if(sources.Count==0)folders.Children.Add(Ui.Text("Папок пока нет. Нажмите «Добавить папку», чтобы собрать библиотеку.",14));
        foreach(var item in sources)
        {
            var f=item.Folder;var card=new StackPanel();card.Children.Add(Ui.Text(Path.GetFileName(Path.TrimEndingDirectorySeparator(f.Path)) is {Length:>0} name?name:f.Path,16,true));
            card.Children.Add(Ui.Text(f.Path,12));card.Children.Add(Ui.Text(item.Available?"● Доступна · автопроверка каждые 30 секунд":"○ Папка недоступна · подключите диск или проверьте путь",12));
            card.Children.Add(Ui.Button("Убрать из библиотеки",async()=>
            {
                var choice=await Host.Choose("Убрать папку из библиотеки? Видео на диске и история просмотра сохранятся.\n"+f.Path,new List<string>{"Убрать папку","Отмена"},x=>x);
                if(choice!="Убрать папку")return;store.RemoveFolder(f);Reload();Host.SettingsChanged();
            }));folders.Children.Add(Ui.Panel(card));
        }
    }
    void Save()
    {
        if(mode.SelectedIndex==1)
        {
            if(!string.IsNullOrWhiteSpace(token.Password))tmdb.SaveToken(token.Password);
            else if(string.IsNullOrWhiteSpace(store.Setting("tmdb_token"))){status.Text="Введите собственный токен.";return;}
            else tmdb.SetMode("custom");
        }
        else tmdb.SetMode(mode.SelectedIndex==2?"off":"builtin");
        token.Clear();status.Text=tmdb.Connected?"Настройки сохранены. Метаданные обновляются.":"Подключение отключено. Локальная библиотека доступна.";Host.SettingsChanged();
    }
}

public sealed class SeriesWindow : Screen
{
    readonly LibraryStore store; readonly TmdbService tmdb; Series series;
    readonly ListBox episodes=new(){Height=340}; readonly ComboBox seasons=new();
    readonly StackPanel root=new(); bool refreshing;
    public SeriesWindow(MainWindow host,LibraryStore store,TmdbService tmdb,Series series):base(host)
    {
        this.store=store;this.tmdb=tmdb;this.series=series;Content=new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        episodes.MouseDoubleClick+=(_,e)=>{if(e.OriginalSource is DependencyObject source){while(source!=null){if(source is System.Windows.Controls.Primitives.ButtonBase)return;source=source is Visual?VisualTreeHelper.GetParent(source):LogicalTreeHelper.GetParent(source);}}if(episodes.SelectedItem is Episode ep)Play(ep);};
        seasons.SelectionChanged+=(_,_)=>Fill();Build();Loaded+=async(_,_)=>await EnrichEpisodes();
    }
    async Task EnrichEpisodes()
    {
        try{await tmdb.LoadEpisodes(series);RefreshData();}catch(Exception){/* Cached and local episode data remain usable offline. */}
    }
    void Build()
    {
        var previous=seasons.SelectedItem as string;
        if(seasons.Parent is Panel seasonParent) seasonParent.Children.Remove(seasons);
        if(episodes.Parent is Panel episodeParent) episodeParent.Children.Remove(episodes);
        root.Children.Clear();
        var hero=new Grid{Height=530,Background=new SolidColorBrush(Color.FromRgb(24,27,30))};
        var backdrop=new Image{Source=LocalImageConverter.Load(series.Backdrop,1600),Stretch=Stretch.UniformToFill};hero.Children.Add(backdrop);
        hero.Children.Add(new System.Windows.Shapes.Rectangle{Fill=new LinearGradientBrush(new GradientStopCollection{new(Color.FromArgb(190,16,17,19),0),new(Color.FromArgb(70,16,17,19),0.65),new(Color.FromArgb(255,16,17,19),1)},new Point(0,0),new Point(0,1))});
        var layout=new Grid{Margin=new(64,130,64,36)};layout.ColumnDefinitions.Add(new(){Width=new(3,GridUnitType.Star)});layout.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var info=new StackPanel{MaxWidth=650,HorizontalAlignment=HorizontalAlignment.Left};
        if(series.Logo!=null)info.Children.Add(new Image{Source=LocalImageConverter.Load(series.Logo,600),Height=110,MaxWidth=340,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,Margin=new(0,0,0,18)});
        else info.Children.Add(Ui.Text(series.Title,38,true));
        info.Children.Add(Ui.Text(series.Details,15,true));
        var actions=new WrapPanel();actions.Children.Add(Ui.Button(series.State=="В процессе"?"Продолжить":"Смотреть",()=>Play(PlaybackSelection.Continue(series)),true));var more=Ui.Button("⋯",()=>{});more.ToolTip="Действия с сериалом";more.Click+=(_,_)=>ShowSeriesMenu(more);actions.Children.Add(more);info.Children.Add(actions);
        var overview=Ui.Text(series.Overview,16);overview.MaxHeight=150;overview.TextTrimming=TextTrimming.CharacterEllipsis;overview.Margin=new(0,20,26,0);info.Children.Add(overview);layout.Children.Add(info);
        var facts=new StackPanel();facts.Children.Add(Ui.Text("В ВАШЕЙ БИБЛИОТЕКЕ",11));facts.Children.Add(Ui.Text(series.ProgressLabel,18,true));facts.Children.Add(Ui.Text($"Сезонов: {series.Episodes.Where(e=>e.Season.HasValue).Select(e=>e.Season).Distinct().Count()}"));facts.Children.Add(Ui.Text($"Доступно серий: {series.Episodes.Count(e=>e.Available)}"));facts.Children.Add(Ui.Text(series.State));
        var factsPanel=Ui.Panel(facts);factsPanel.VerticalAlignment=VerticalAlignment.Bottom;Grid.SetColumn(factsPanel,1);layout.Children.Add(factsPanel);hero.Children.Add(layout);root.Children.Add(hero);
        var section=new StackPanel{Margin=new(64,0,64,32)};var heading=new DockPanel();var title=Ui.Text("Серии",24,true);DockPanel.SetDock(title,Dock.Left);heading.Children.Add(title);
        seasons.ItemsSource=new[]{"Все сезоны"}.Concat(series.Episodes.Where(e=>e.Season.HasValue).Select(e=>e.Season!.Value).Distinct().Order().Select(n=>"Сезон "+n)).ToList();seasons.SelectedItem=previous??"Все сезоны";if(seasons.SelectedIndex<0)seasons.SelectedIndex=0;
        seasons.HorizontalAlignment=HorizontalAlignment.Right;heading.Children.Add(seasons);section.Children.Add(heading);
        episodes.ItemTemplate=EpisodeTemplate();section.Children.Add(episodes);Fill();
        root.Children.Add(section);
    }
    DataTemplate EpisodeTemplate()
    {
        var template=new DataTemplate(typeof(Episode));var row=new FrameworkElementFactory(typeof(DockPanel));
        var actions=new FrameworkElementFactory(typeof(StackPanel));actions.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);actions.SetValue(DockPanel.DockProperty,Dock.Right);actions.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        void Action(string text,string tooltip,RoutedEventHandler click)
        {
            var button=new FrameworkElementFactory(typeof(Button));button.SetValue(ContentControl.ContentProperty,text);button.SetValue(FrameworkElement.ToolTipProperty,tooltip);button.SetValue(Control.PaddingProperty,new Thickness(12,8,12,8));button.AddHandler(Button.ClickEvent,click);actions.AppendChild(button);
        }
        Action("▶","Смотреть серию",(sender,e)=>{e.Handled=true;if(((FrameworkElement)sender).DataContext is Episode ep)Play(ep);});
        Action("✓","Изменить отметку просмотра",(sender,e)=>{e.Handled=true;if(((FrameworkElement)sender).DataContext is Episode ep){ep.Watched=!ep.Watched;store.SaveProgress(ep);RefreshData();}});
        Action("⋯","Другие действия с серией",(sender,e)=>{e.Handled=true;var button=(Button)sender;if(button.DataContext is Episode ep)ShowEpisodeMenu(button,ep);});row.AppendChild(actions);
        var image=new FrameworkElementFactory(typeof(Image));image.SetValue(DockPanel.DockProperty,Dock.Left);image.SetValue(FrameworkElement.WidthProperty,132.0);image.SetValue(FrameworkElement.HeightProperty,74.0);image.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,20,0));image.SetValue(Image.StretchProperty,Stretch.UniformToFill);image.SetBinding(Image.SourceProperty,new Binding("Still"){Converter=new LocalImageConverter(),ConverterParameter=300});row.AppendChild(image);
        var column=new FrameworkElementFactory(typeof(StackPanel));column.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new Binding("Label"));label.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);column.AppendChild(label);
        var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new Binding("DisplayTitle"));title.SetValue(FrameworkElement.MarginProperty,new Thickness(0,6,12,6));title.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);column.AppendChild(title);
        var status=new FrameworkElementFactory(typeof(TextBlock));status.SetBinding(TextBlock.TextProperty,new Binding("Status"));status.SetValue(TextBlock.ForegroundProperty,Ui.Muted);status.SetValue(TextBlock.FontSizeProperty,12.0);column.AppendChild(status);row.AppendChild(column);template.VisualTree=row;return template;
    }
    static ContextMenu Menu(Button anchor)
    {
        var menu=new ContextMenu{PlacementTarget=anchor,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};anchor.ContextMenu=menu;return menu;
    }
    static void Option(ContextMenu menu,string label,Action action){var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}
    void ShowEpisodeMenu(Button anchor,Episode ep)
    {
        var menu=Menu(anchor);
        Option(menu,"Начать заново",()=>{ep.Position=0;ep.Watched=false;store.SaveProgress(ep);Play(ep);});
        Option(menu,"Указать другой файл…",()=>{var d=new OpenFileDialog{Filter="Видео|*.mkv;*.mp4;*.avi;*.mov;*.m4v;*.ts"};if(d.ShowDialog(Host)==true){store.Relink(ep,d.FileName);RefreshData();}});
        Option(menu,"Исправить номер…",async()=>{var result=await Host.Ask("Сезон и серия через пробел",$"{ep.Season??1} {ep.Number??1}");if(result==null)return;var parts=result.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(parts.Length==2&&int.TryParse(parts[0],out int season)&&season>=0&&int.TryParse(parts[1],out int number)&&number>0){store.Correct(ep,season,number);RefreshData();}else await Host.Notice("Введите два числа, например: 1 2.");});
        menu.IsOpen=true;
    }
    void ShowSeriesMenu(Button anchor)
    {
        var menu=Menu(anchor);Option(menu,"Найти или сменить сериал в TMDB…",async()=>await Match());Option(menu,"Выбрать локальный постер…",ChoosePoster);Option(menu,"Полное описание",async()=>await Host.Notice(series.Overview));menu.IsOpen=true;
    }
    void ChoosePoster()
    {
        var d=new OpenFileDialog{Filter="Изображения|*.jpg;*.jpeg;*.png"};
        if(d.ShowDialog(Host)==true){var dir=Path.Combine(LibraryStore.DataDirectory,"images");Directory.CreateDirectory(dir);var dest=Path.Combine(dir,Guid.NewGuid()+Path.GetExtension(d.FileName));File.Copy(d.FileName,dest);store.Execute("UPDATE series SET poster=$p WHERE id=$i",("$p",dest),("$i",series.Id));RefreshData();}
    }

    async Task Match()
    {
        if(!tmdb.Connected){await Host.Notice("Включите подключение TMDB в настройках.");return;}
        var query=await Host.Ask("Название сериала для поиска",series.SearchTitle);if(string.IsNullOrWhiteSpace(query))return;
        try{var candidates=await tmdb.Search(query);var selected=await Host.Choose("Выберите сериал",candidates,m=>m.ToString());if(selected!=null){await tmdb.Apply(series.Id,selected);RefreshData();}}
        catch(Exception ex){await Host.Notice(ex.Message);}
    }
    void Fill(){var selected=seasons.SelectedItem as string;episodes.ItemsSource=series.Episodes.Where(e=>selected==null||selected=="Все сезоны"||selected=="Сезон "+e.Season).ToList();}
    public override async void RefreshData()
    {
        if(refreshing)return;refreshing=true;
        try{var latest=await Task.Run(()=>store.LoadCatalog().FirstOrDefault(s=>s.Id==series.Id || (series.TmdbId.HasValue && s.TmdbId==series.TmdbId)));if(latest!=null){series=latest;Build();}else{Content=Ui.Panel(Ui.Text("Файлы сериала удалены. История просмотра сохранена. Вернитесь в библиотеку кнопкой «Назад».",20));}}finally{refreshing=false;}
    }
    void Play(Episode? ep){if(ep!=null)Host.Play(series,ep);}
}






