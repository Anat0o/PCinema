namespace PCinema;

/// <summary>Latest target wins; relative jumps accumulate against the requested target.</summary>
public sealed class SeekCoordinator
{
    readonly Func<long,CancellationToken,Task> apply;
    readonly int delayMilliseconds;
    public SeekCoordinator(Func<long,Task> apply,int delayMilliseconds=100):this((target,_)=>apply(target),delayMilliseconds){}
    public SeekCoordinator(Func<long,CancellationToken,Task> apply,int delayMilliseconds=100){this.apply=apply;this.delayMilliseconds=delayMilliseconds;}
    CancellationTokenSource? pending;
    public long? Target {get;private set;}
    public Task JumpAsync(long offset,long actual,long length)=>RequestAsync(Math.Clamp((Target??actual)+offset,0,Math.Max(0,length-500)));
    public async Task RequestAsync(long target,bool immediate=false)
    {
        pending?.Cancel();var cancellation=pending=new();Target=target;
        try{if(!immediate)await Task.Delay(delayMilliseconds,cancellation.Token);cancellation.Token.ThrowIfCancellationRequested();await apply(target,cancellation.Token);}
        catch(OperationCanceledException){}
        finally{if(ReferenceEquals(pending,cancellation)){Target=null;pending=null;}cancellation.Dispose();}
    }
    public void Cancel(){pending?.Cancel();pending=null;Target=null;}
}
