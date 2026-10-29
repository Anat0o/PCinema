using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace PCinema.Sync;

public sealed record GoogleIdentity(string Sub,string Email,string Name);
sealed record GoogleCredentials(string RefreshToken,GoogleIdentity Identity);
public sealed class GoogleAccount
{
    static readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(30)};
    readonly string tokenFile;readonly string clientId,clientSecret;
    readonly SemaphoreSlim refreshGate=new(1);
    string? accessToken;DateTimeOffset expires;GoogleCredentials? credentials;
    public bool Configured=>!string.IsNullOrWhiteSpace(clientId);
    public GoogleIdentity? Identity=>credentials?.Identity;
    public GoogleAccount(string directory)
    {
        tokenFile=Path.Combine(directory,"google-account.dpapi");
        var config=Path.Combine(AppContext.BaseDirectory,"google-oauth.json");
        clientId=Environment.GetEnvironmentVariable("PCINEMA_GOOGLE_CLIENT_ID")??"";clientSecret="";
        if(File.Exists(config)) try
        {
            using var json=JsonDocument.Parse(File.ReadAllText(config));var root=json.RootElement;if(root.TryGetProperty("installed",out var installed))root=installed;
            clientId=root.TryGetProperty("client_id",out var id)?id.GetString()??"":clientId;clientSecret=root.TryGetProperty("client_secret",out var secret)?secret.GetString()??"":"";
        }
        catch(Exception ex) when(ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException) { clientId="";clientSecret=""; }
        if(File.Exists(tokenFile))try{credentials=JsonSerializer.Deserialize<GoogleCredentials>(ProtectedData.Unprotect(File.ReadAllBytes(tokenFile),null,DataProtectionScope.CurrentUser));}catch(CryptographicException){}catch(JsonException){}catch(IOException){}catch(UnauthorizedAccessException){}
    }
    public async Task<GoogleIdentity> SignInAsync(CancellationToken token)
    {
        if(!Configured)throw new InvalidOperationException("В этой сборке ещё не настроен Google OAuth-клиент PCinema. Инструкция: docs/GOOGLE-SETUP.md. Локальные функции доступны без входа.");
        string verifier=Base64(RandomNumberGenerator.GetBytes(48)),state=Base64(RandomNumberGenerator.GetBytes(32));
        string challenge=Base64(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();string redirect="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/";
        var query=new Dictionary<string,string>{{"client_id",clientId},{"redirect_uri",redirect},{"response_type","code"},{"scope","openid email profile https://www.googleapis.com/auth/drive.appdata"},{"code_challenge",challenge},{"code_challenge_method","S256"},{"state",state},{"access_type","offline"},{"prompt","consent select_account"}};
        Process.Start(new ProcessStartInfo("https://accounts.google.com/o/oauth2/v2/auth?"+string.Join("&",query.Select(p=>Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value)))){UseShellExecute=true});
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(3));string? code=null;
        while(code==null)
        {
            using var socket=await listener.AcceptTcpClientAsync(timeout.Token);using var stream=socket.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
            using var requestTimeout=CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            var line=await reader.ReadLineAsync(requestTimeout.Token);if(line==null||line.Length>8192)continue;
            int headerBytes=0; string? headerLine; while(!string.IsNullOrEmpty(headerLine=await reader.ReadLineAsync(requestTimeout.Token))) { headerBytes+=headerLine.Length; if(headerBytes>16384)throw new InvalidDataException("Invalid callback headers."); }
            var parts=line.Split(' ');if(parts.Length<2||parts[0]!="GET")continue;
            var uri=new Uri(new Uri(redirect),parts[1]);var parameters=uri.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(p=>p.Split('=',2)).GroupBy(p=>Uri.UnescapeDataString(p[0])).ToDictionary(g=>g.Key,g=>g.Last().Length==2?Uri.UnescapeDataString(g.Last()[1].Replace('+',' ')):"");
            bool valid=parameters.GetValueOrDefault("state")==state;var body=Encoding.UTF8.GetBytes(valid?"PCinema: you may close this tab and return to the app.":"Invalid authorization response.");
            var header=Encoding.ASCII.GetBytes("HTTP/1.1 "+(valid?"200 OK":"400 Bad Request")+"\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");await stream.WriteAsync(header,requestTimeout.Token);await stream.WriteAsync(body,requestTimeout.Token);
            if(!valid)continue;if(parameters.ContainsKey("error"))throw new OperationCanceledException("Вход отменён пользователем.");code=parameters.GetValueOrDefault("code");
        }
        var result=await Exchange(new(){{"code",code},{"code_verifier",verifier},{"redirect_uri",redirect},{"grant_type","authorization_code"}},token);
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://openidconnect.googleapis.com/v1/userinfo");request.Headers.Authorization=new("Bearer",accessToken);using var response=await http.SendAsync(request,token);response.EnsureSuccessStatusCode();using var info=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var identity=new GoogleIdentity(info.RootElement.GetProperty("sub").GetString()!,info.RootElement.GetProperty("email").GetString()!,info.RootElement.TryGetProperty("name",out var n)?n.GetString()??"":"");
        if(!result.TryGetProperty("refresh_token",out var refresh))throw new InvalidOperationException("Google не выдал доступ для фоновой синхронизации. Повторите вход.");
        credentials=new(refresh.GetString()!,identity);Directory.CreateDirectory(Path.GetDirectoryName(tokenFile)!);
        File.WriteAllBytes(tokenFile+".pending",ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(credentials),null,DataProtectionScope.CurrentUser));File.Move(tokenFile+".pending",tokenFile,true);return identity;
    }
    async Task<JsonElement> Exchange(Dictionary<string,string> fields,CancellationToken token)
    {
        fields["client_id"]=clientId;if(clientSecret.Length>0)fields["client_secret"]=clientSecret;
        using var response=await http.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(fields),token);
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Google отклонил авторизацию. Повторите вход или проверьте настройку OAuth-клиента.");
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));var root=json.RootElement.Clone();accessToken=root.GetProperty("access_token").GetString();expires=DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()-60);return root;
    }
    public async Task<string> AccessTokenAsync(CancellationToken token)
    {
        await refreshGate.WaitAsync(token);try{if(credentials==null)throw new InvalidOperationException("Войдите в Google.");if(accessToken==null||expires<=DateTimeOffset.UtcNow)await Exchange(new(){{"refresh_token",credentials.RefreshToken},{"grant_type","refresh_token"}},token);return accessToken!;}finally{refreshGate.Release();}
    }
    public async Task SignOutAsync(){await refreshGate.WaitAsync();try{credentials=null;accessToken=null;if(File.Exists(tokenFile))File.Delete(tokenFile);}finally{refreshGate.Release();}}
    static string Base64(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
}
