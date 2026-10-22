using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace PCinema;

public sealed class TmdbService : IDisposable
{
    readonly LibraryStore store; readonly HttpClient client; readonly bool ownsClient;
    readonly string builtin; readonly CancellationTokenSource shutdown = new();
    readonly SemaphoreSlim autoGate=new(1);
    readonly Dictionary<long,DateTime> attempted=[];
    public TmdbService(LibraryStore store,HttpClient? client=null,string? builtInToken=null)
    {
        this.store=store; ownsClient=client==null; this.client=client??new HttpClient {Timeout=TimeSpan.FromSeconds(20)};
        using var stream=typeof(TmdbService).Assembly.GetManifestResourceStream("PCinema.TmdbToken");
        builtin=builtInToken ?? (stream==null ? "" : new StreamReader(stream).ReadToEnd().Trim());
    }
    public string Mode => store.Setting("tmdb_mode") ?? (!string.IsNullOrWhiteSpace(store.Setting("tmdb_token")) ? "custom" : "builtin");
    public bool Connected => Mode=="builtin" ? builtin.Length>0 : Mode=="custom" && !string.IsNullOrWhiteSpace(store.Setting("tmdb_token"));
    public void SetMode(string mode)
    {
        if(mode is not ("builtin" or "custom" or "off")) throw new ArgumentException("Неизвестный режим подключения.");
        store.Setting("tmdb_mode",mode); attempted.Clear();
    }
    public void SaveToken(string token)
    {
        store.Setting("tmdb_token",string.IsNullOrWhiteSpace(token) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token.Trim()),null,DataProtectionScope.CurrentUser)));
        SetMode(string.IsNullOrWhiteSpace(token) ? "off" : "custom");
    }
    string Token()
    {
        if(!Connected) throw new InvalidOperationException("Подключение TMDB отключено.");
        if(Mode=="builtin") return builtin;
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(store.Setting("tmdb_token")??""),null,DataProtectionScope.CurrentUser)); }
        catch(Exception ex) when(ex is CryptographicException or FormatException) { throw new InvalidOperationException("Не удалось прочитать сохранённый токен. Укажите его заново."); }
    }
    async Task<JsonDocument> Get(string path)
    {
        for(int attempt=0;;attempt++)
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.themoviedb.org/3/"+path);
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",Token());
            using var response=await client.SendAsync(request,shutdown.Token);
            if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new InvalidOperationException("TMDB отклонил токен. Проверьте API Read Access Token в настройках.");
            if(response.StatusCode==HttpStatusCode.TooManyRequests && attempt<2) { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(response.Headers.RetryAfter?.Delta?.TotalSeconds??(attempt+1)*2,1,10)),shutdown.Token);continue; }
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(shutdown.Token));
        }
    }
    public async Task TestConnection() { using var doc=await Get("configuration"); }
    static string Str(JsonElement e,string k)=>e.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String ? v.GetString()??"" : "";
    static Metadata Read(JsonElement e)=>new(e.GetProperty("id").GetInt32(),Str(e,"name"),Str(e,"original_name"),int.TryParse(Str(e,"first_air_date").Split('-')[0],out var y)?y:null,Str(e,"overview"),Str(e,"poster_path"),Str(e,"backdrop_path"));
    public async Task<List<Metadata>> Search(string title) { using var doc=await Get("search/tv?language=ru-RU&query="+Uri.EscapeDataString(title));return doc.RootElement.GetProperty("results").EnumerateArray().Select(Read).ToList(); }
    public async Task Apply(long seriesId,Metadata candidate)
    {
        using var doc=await Get($"tv/{candidate.Id}?language=ru-RU&append_to_response=images&include_image_language=ru,en,null");
        var root=doc.RootElement;var m=Read(root);
        if(string.IsNullOrWhiteSpace(m.Overview)) { using var en=await Get($"tv/{candidate.Id}?language=en-US");m=m with {Overview=Str(en.RootElement,"overview")}; }
        var poster=await OptionalCache(m.Poster,"w500");var backdrop=await OptionalCache(m.Backdrop,"w1280");
        string? logo=null;
        if(root.TryGetProperty("images",out var images)&&images.TryGetProperty("logos",out var logos))
        {
            var chosen=logos.EnumerateArray().OrderBy(l=>Str(l,"iso_639_1")=="ru"?0:Str(l,"iso_639_1")=="en"?1:2).FirstOrDefault();
            if(chosen.ValueKind==JsonValueKind.Object) logo=await OptionalCache(Str(chosen,"file_path"),"w500");
        }
        var genres=root.TryGetProperty("genres",out var g) ? string.Join(" · ",g.EnumerateArray().Select(x=>Str(x,"name"))) : "";
        var rating=root.TryGetProperty("vote_average",out var r)&&r.TryGetDouble(out var ratingValue) ? ratingValue : 0;
        store.Metadata(seriesId,m,poster,backdrop);
        store.Execute("INSERT INTO series_details(series_id,genres,rating,logo) VALUES($i,$g,$r,$l) ON CONFLICT(series_id) DO UPDATE SET genres=$g,rating=$r,logo=$l",("$i",seriesId),("$g",genres),("$r",rating),("$l",logo));
        // Changing recognition invalidates only cached episode metadata, never progress.
        store.Execute("DELETE FROM episode_details WHERE episode_id IN (SELECT id FROM episodes WHERE series_id=$i)",("$i",seriesId));
    }
    public async Task LoadEpisodes(Series series)
    {
        if(!Connected || series.TmdbId==null) return;
        foreach(var group in series.Episodes.Where(e=>e.Season.HasValue&&e.Number.HasValue&&e.Title==null).GroupBy(e=>e.Season!.Value))
        {
            using var doc=await Get($"tv/{series.TmdbId}/season/{group.Key}?language=ru-RU");
            if(!doc.RootElement.TryGetProperty("episodes",out var list)) continue;
            foreach(var item in list.EnumerateArray())
            {
                var n=item.GetProperty("episode_number").GetInt32();
                var local=group.Where(e=>e.Number==n).ToList();if(local.Count==0)continue;
                var still=await OptionalCache(Str(item,"still_path"),"w300");
                foreach(var ep in local) store.Execute("INSERT INTO episode_details(episode_id,title,still) VALUES($i,$t,$s) ON CONFLICT(episode_id) DO UPDATE SET title=$t,still=$s",("$i",ep.Id),("$t",Str(item,"name")),("$s",still));
            }
        }
    }
    async Task<string?> OptionalCache(string? path,string size)
    {
        if(string.IsNullOrWhiteSpace(path))return null;
        try { return await Cache(path,size); }
        catch(HttpRequestException) {return null;}
        catch(IOException) {return null;}
    }
    async Task<string?> Cache(string path,string size)
    {
        var dir=Path.Combine(LibraryStore.DataDirectory,"images");Directory.CreateDirectory(dir);var dest=Path.Combine(dir,size+"_"+Path.GetFileName(path));
        if(File.Exists(dest))return dest;
        var bytes=await client.GetByteArrayAsync("https://image.tmdb.org/t/p/"+size+path,shutdown.Token);
        var temp=dest+"."+Guid.NewGuid()+".tmp";
        try { await File.WriteAllBytesAsync(temp,bytes,shutdown.Token);File.Move(temp,dest,true); } finally {if(File.Exists(temp))File.Delete(temp);}
        return dest;
    }
    public async Task AutoMatch()
    {
        if(!Connected || !await autoGate.WaitAsync(0))return;
        try
        {
            var complete=store.Query("SELECT series_id FROM series_details",r=>r.GetInt64(0)).ToHashSet();
            foreach(var s in await Task.Run(store.Load))
            {
                if(s.TmdbId.HasValue && complete.Contains(s.Id))continue;
                if(attempted.TryGetValue(s.Id,out var time)&&DateTime.UtcNow-time<TimeSpan.FromMinutes(30))continue;
                attempted[s.Id]=DateTime.UtcNow;
                if(s.TmdbId is int id) {await Apply(s.Id,new(id,s.Title,s.SearchTitle,s.Year,s.Overview,null,null));continue;}
                var candidates=await Search(s.SearchTitle);
                var exact=candidates.Where(m=>(NameParser.Normalize(m.Name)==NameParser.Normalize(s.SearchTitle)||NameParser.Normalize(m.OriginalName)==NameParser.Normalize(s.SearchTitle))&&(!s.Year.HasValue||s.Year==m.Year)).ToList();
                if(exact.Count==1)await Apply(s.Id,exact[0]);
            }
        }
        finally {autoGate.Release();}
    }
    public void Dispose() {shutdown.Cancel();if(ownsClient)client.Dispose();}
}
