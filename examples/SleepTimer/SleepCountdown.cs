namespace PCinema.Examples;

public sealed class SleepCountdown(Func<TimeSpan,CancellationToken,Task>? delay=null)
{
    readonly Func<TimeSpan,CancellationToken,Task> wait=delay??Task.Delay;
    readonly object gate=new();
    readonly List<Task> tasks=[];
    CancellationTokenSource? current;
    public Task Arm(TimeSpan duration,Func<Task> elapsed)
    {
        lock(gate)
        {
            current?.Cancel();var cancellation=new CancellationTokenSource();current=cancellation;
            tasks.RemoveAll(t=>t.IsCompleted);var task=Run(duration,elapsed,cancellation);tasks.Add(task);return task;
        }
    }
    async Task Run(TimeSpan duration,Func<Task> elapsed,CancellationTokenSource cancellation)
    {
        try{await wait(duration,cancellation.Token);cancellation.Token.ThrowIfCancellationRequested();await elapsed();}
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested){}
        finally{lock(gate){if(ReferenceEquals(current,cancellation))current=null;}cancellation.Dispose();}
    }
    public async Task StopAsync()
    {
        Task[] pending;lock(gate){current?.Cancel();pending=tasks.ToArray();}await Task.WhenAll(pending);
    }
}
