using System.Net.NetworkInformation;
namespace PCinema.Sync;

public sealed class SyncService
{
    readonly LibraryStore store;readonly CancellationTokenSource lifetime=new();readonly SemaphoreSlim operations=new(1);
    readonly Func<Task> captureModules,applyModules;CancellationTokenSource accountOperations=new();SyncCoordinator? coordinator;Task? loop;DateTimeOffset due=DateTimeOffset.MaxValue,lastProgress;int failures;bool stopping;
    public GoogleAccount Account {get;}
    public string Status{get;private set;}="Синхронизация выключена";
    public bool RequiresChoice{get;private set;}
    public bool Busy{get;private set;}
    public bool Automatic{get;private set;}
    public string? LastSuccess=>coordinator?.Session?.LastSuccess;
    public bool Pending=>store.SyncMeta("dirty")=="true"||coordinator?.Session?.Pending==true;
    public event Action? Changed;
    public SyncService(LibraryStore store,Func<Task> captureModules,Func<Task> applyModules)
    {
        this.store=store;this.captureModules=captureModules;this.applyModules=applyModules;Account=new(store.SyncDirectory);Automatic=store.SyncMeta("automatic")!="false";
        if(Account.Identity is {} identity){coordinator=new(store,new GoogleDriveTransport(Account),identity.Sub);RequiresChoice=coordinator.Session==null;Status=RequiresChoice?"Выберите способ подключения":"Готово к синхронизации";}
        store.SyncChanged+=LocalChanged;NetworkChange.NetworkAvailabilityChanged+=NetworkChanged;
    }
    public void Start(){if(loop!=null)return;due=DateTimeOffset.UtcNow;loop=Task.Run(Loop);}
    void NetworkChanged(object? sender,NetworkAvailabilityEventArgs e){if(e.IsAvailable)due=DateTimeOffset.UtcNow;}
    void LocalChanged(bool progress)
    {
        if(stopping)return;var now=DateTimeOffset.UtcNow;var target=progress?(lastProgress.AddSeconds(30)>now?lastProgress.AddSeconds(30):now):now.AddSeconds(2);if(target<due)due=target;
    }
    public void SettingsChanged()=>LocalChanged(false);
    public void RequestNow(){due=DateTimeOffset.UtcNow;}
    public void SetAutomatic(bool value){Automatic=value;store.SyncMeta("automatic",value?"true":"false");due=DateTimeOffset.UtcNow;Changed?.Invoke();}
    async Task Loop()
    {
        try{while(!lifetime.IsCancellationRequested){await Task.Delay(1000,lifetime.Token);if(Automatic&&!RequiresChoice&&coordinator!=null&&DateTimeOffset.UtcNow>=due)await SynchronizeAsync(lifetime.Token);}}catch(OperationCanceledException){}
    }
    public async Task SignInAsync(CancellationToken token)
    {
        await operations.WaitAsync(token);try{Busy=true;Changed?.Invoke();coordinator=null;await Account.SignInAsync(token);RequiresChoice=true;Status="Выберите способ подключения";}finally{Busy=false;operations.Release();Changed?.Invoke();}
    }
    public async Task ConnectAsync(ConnectChoice choice,CancellationToken token)
    {
        await operations.WaitAsync(token);try
        {
            Busy=true;Changed?.Invoke();await captureModules();coordinator??=new(store,new GoogleDriveTransport(Account),Account.Identity?.Sub??throw new InvalidOperationException("Войдите в Google."));
            await Task.Run(()=>coordinator.ConnectAsync(choice,token),token);RequiresChoice=false;failures=0;Status="Синхронизировано";await applyModules();due=DateTimeOffset.UtcNow.AddSeconds(60);
        }
        catch{RequiresChoice=true;throw;}finally{Busy=false;operations.Release();Changed?.Invoke();}
    }
    public async Task SynchronizeAsync(CancellationToken token=default)
    {
        if(RequiresChoice||coordinator==null)return;
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,accountOperations.Token);token=linked.Token;
        try { await operations.WaitAsync(token); } catch(OperationCanceledException) { return; } try
        {
            if(coordinator==null)return;Busy=true;Changed?.Invoke();await captureModules();await Task.Run(()=>coordinator.SynchronizeAsync(token),token);await applyModules();Status="Синхронизировано";failures=0;lastProgress=DateTimeOffset.UtcNow;due=lastProgress.AddSeconds(60);
        }
        catch(GenerationChangedException ex){RequiresChoice=true;Status=ex.Message;}
        catch(OperationCanceledException){Status="Изменения сохранены на этом компьютере";}
        catch(Exception ex){Status=ex is InvalidOperationException or System.IO.InvalidDataException or System.Net.Http.HttpRequestException?ex.Message:"Не удалось синхронизировать. Локальные данные сохранены.";failures++;due=DateTimeOffset.UtcNow.AddSeconds(Math.Min(300,Math.Pow(2,Math.Min(failures,7))*5));PerformanceLog.Write("sync.failed",error:ex);}
        finally{Busy=false;operations.Release();Changed?.Invoke();}
    }
    public async Task SignOutAsync()
    {
        accountOperations.Cancel();await operations.WaitAsync();try{accountOperations.Dispose();accountOperations=new();coordinator=null;await Account.SignOutAsync();RequiresChoice=false;Status="Вы вышли. Локальная библиотека сохранена.";}finally{operations.Release();Changed?.Invoke();}
    }
    public async Task StopAsync()
    {
        stopping=true;store.SyncChanged-=LocalChanged;NetworkChange.NetworkAvailabilityChanged-=NetworkChanged;lifetime.Cancel();if(loop!=null)await loop;
        await Task.Run(store.CaptureSync);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(3));try { if(Automatic&&!RequiresChoice)await SynchronizeAsync(timeout.Token); } catch(OperationCanceledException) {}
    }
}
