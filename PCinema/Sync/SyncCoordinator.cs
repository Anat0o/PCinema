using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
namespace PCinema.Sync;

public sealed record SyncSession(string Generation,ChangeStamp GenerationStamp,string? LastSuccess=null,bool Pending=true);
public sealed class GenerationChangedException():Exception("Облачная копия заменена на другом ПК. Выберите восстановление или объединение.");
public sealed class SyncCoordinator(LibraryStore store,ISyncTransport transport,string account)
{
    readonly SemaphoreSlim gate=new(1);
    readonly string stateKey="account:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account)));
    public SyncSession? Session=>JsonSerializer.Deserialize<SyncSession>(store.SyncMeta(stateKey)??"null");
    public async Task ConnectAsync(ConnectChoice choice,CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var remote=await Read(token);var generation=Latest(remote);
            if(choice!=ConnectChoice.Merge)await Task.Run(()=>store.BackupSync("before-cloud-replace"),token);
            if(choice==ConnectChoice.ReplaceCloud)generation=new(1,Guid.NewGuid().ToString("N"),store.NextStamp(generation.GenerationStamp.Milliseconds),store.DeviceId,[]);
            var cloud=Current(remote,generation.Generation);
            await Task.Run(()=>store.WithSyncLock(()=> {
                store.CaptureSync();var local=store.SyncEntries();
                var entries=choice==ConnectChoice.RestoreCloud?cloud:choice==ConnectChoice.ReplaceCloud?local:Combine(local,cloud);
                store.ApplySync(entries,true);return true;
            }),token);
            Save(new(generation.Generation,generation.GenerationStamp));await Upload(token);
        }
        finally{gate.Release();}
    }
    public async Task SynchronizeAsync(CancellationToken token=default)
    {
        await gate.WaitAsync(token);try
        {
            var session=Session??throw new InvalidOperationException("Выберите способ подключения.");
            await Task.Run(store.CaptureSync,token);Save(session with{Pending=true});
            var remote=await Read(token);var generation=Latest(remote);
            if(remote.Count>0&&(generation.Generation!=session.Generation||generation.GenerationStamp.CompareTo(session.GenerationStamp)!=0))throw new GenerationChangedException();
            await Task.Run(()=>store.WithSyncLock(()=> { store.CaptureSync(); var merged=Combine(store.SyncEntries(),Current(remote,session.Generation)); store.ApplySync(merged); return true; }),token);await Upload(token);
        }
        finally{gate.Release();}
    }
    async Task<IReadOnlyList<SyncDocument>> Read(CancellationToken token)
    {
        var docs=await transport.ReadAsync(token);foreach(var d in docs)SyncMerge.Validate(d);foreach(var d in docs){store.ObserveStamp(d.GenerationStamp);if(d.Entries.Count>0)store.ObserveStamp(d.Entries.MaxBy(e=>e.Stamp)!.Stamp);}return docs;
    }
    static List<SyncEntry> Combine(List<SyncEntry> local,List<SyncEntry> cloud) { var keys=cloud.Select(e=>e.Key).ToHashSet();return SyncMerge.Merge(local.Where(e=>e.Stamp.Milliseconds!=0||!keys.Contains(e.Key)).Concat(cloud)); }
    static SyncDocument Latest(IReadOnlyList<SyncDocument> docs)=>docs.OrderByDescending(d=>d.GenerationStamp).ThenByDescending(d=>d.Generation,StringComparer.Ordinal).FirstOrDefault()??new(1,"initial",ChangeStamp.Zero,"",[]);
    static List<SyncEntry> Current(IReadOnlyList<SyncDocument> docs,string generation)=>SyncMerge.Merge(docs.Where(d=>d.Generation==generation).SelectMany(d=>d.Entries));
    async Task Upload(CancellationToken token)
    {
        var session=Session!;var snapshot=new SyncDocument(1,session.Generation,session.GenerationStamp,store.DeviceId,store.SyncEntries());SyncMerge.Validate(snapshot);
        await transport.WriteAsync(snapshot,token);store.WithSyncLock(()=> { store.CaptureSync(); bool pending=!store.SyncEntries().OrderBy(e=>e.Key,StringComparer.Ordinal).SequenceEqual(snapshot.Entries.OrderBy(e=>e.Key,StringComparer.Ordinal)); Save(session with{LastSuccess=DateTimeOffset.UtcNow.ToString("O"),Pending=pending});store.SyncMeta("dirty",pending?"true":"false");return true; });
    }
    void Save(SyncSession session)=>store.SyncMeta(stateKey,SyncMerge.Serialize(session));
}
