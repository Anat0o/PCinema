using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
namespace PCinema.Sync;

public sealed class GoogleDriveTransport(GoogleAccount account):ISyncTransport
{
    static readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(30)};
    readonly Dictionary<string,string> files=new();
    async Task<HttpResponseMessage> Send(HttpRequestMessage request,CancellationToken token)
    {
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await account.AccessTokenAsync(token));
        var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode){int status=(int)response.StatusCode;response.Dispose();throw new HttpRequestException(status==401||status==403?"Доступ Google Drive недоступен. Проверьте разрешения или войдите заново.":"Google Drive временно недоступен (HTTP "+status+").",null,(System.Net.HttpStatusCode)status);}return response;
    }
    public async Task<IReadOnlyList<SyncDocument>> ReadAsync(CancellationToken token)
    {
        var result=new List<SyncDocument>();string? page=null;long total=0;files.Clear();
        do
        {
            string url="https://www.googleapis.com/drive/v3/files?spaces=appDataFolder&pageSize=100&fields=nextPageToken,files(id,size,appProperties)&q="+Uri.EscapeDataString("trashed = false and appProperties has { key='pcinema' and value='sync' }")+(page==null?"":"&pageToken="+Uri.EscapeDataString(page));
            using var listRequest=new HttpRequestMessage(HttpMethod.Get,url);using var listing=await Send(listRequest,token);using var json=JsonDocument.Parse(await listing.Content.ReadAsStringAsync(token));
            foreach(var file in json.RootElement.GetProperty("files").EnumerateArray())
            {
                var id=file.GetProperty("id").GetString()!;if(file.TryGetProperty("size",out var size)&&long.TryParse(size.GetString(),out var bytes)){total+=bytes;if(bytes>32*1024*1024||total>128*1024*1024)throw new InvalidDataException("Облачная копия превышает лимит этой версии приложения.");}
                using var request=new HttpRequestMessage(HttpMethod.Get,"https://www.googleapis.com/drive/v3/files/"+Uri.EscapeDataString(id)+"?alt=media");using var response=await Send(request,token);
                await response.Content.LoadIntoBufferAsync(32*1024*1024,token);var document=JsonSerializer.Deserialize<SyncDocument>(await response.Content.ReadAsStringAsync(token))??throw new InvalidDataException("Повреждённая облачная копия.");SyncMerge.Validate(document);result.Add(document);files[document.Generation+":"+document.Device]=id;
                if(result.Count>1000)throw new InvalidDataException("Слишком много снимков синхронизации.");
            }
            page=json.RootElement.TryGetProperty("nextPageToken",out var next)?next.GetString():null;
        }while(page!=null);return result;
    }
    public async Task WriteAsync(SyncDocument document,CancellationToken token)
    {
        SyncMerge.Validate(document);var key=document.Generation+":"+document.Device;
        if(files.TryGetValue(key,out var id))
        {
            using var request=new HttpRequestMessage(HttpMethod.Patch,"https://www.googleapis.com/upload/drive/v3/files/"+Uri.EscapeDataString(id)+"?uploadType=media"){Content=new StringContent(SyncMerge.Serialize(document),Encoding.UTF8,"application/json")};using var response=await Send(request,token);return;
        }
        using var multipart=new MultipartContent("related");multipart.Add(new StringContent(JsonSerializer.Serialize(new{name="pcinema-"+document.Generation+"-"+document.Device+".json",parents=new[]{"appDataFolder"},appProperties=new{pcinema="sync"}}),Encoding.UTF8,"application/json"));multipart.Add(new StringContent(SyncMerge.Serialize(document),Encoding.UTF8,"application/json"));
        using var create=new HttpRequestMessage(HttpMethod.Post,"https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id"){Content=multipart};using var created=await Send(create,token);using var json=JsonDocument.Parse(await created.Content.ReadAsStringAsync(token));files[key]=json.RootElement.GetProperty("id").GetString()!;
    }
}
