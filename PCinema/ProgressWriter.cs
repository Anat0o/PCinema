namespace PCinema;

public sealed record ProgressSnapshot(long EpisodeId,long Position,long Duration,bool Watched,string ChangedAt)
{
    public static ProgressSnapshot From(Episode e)=>new(e.Id,e.Position,e.Duration,e.Watched,e.LastPlayed);
}
public sealed class ProgressWriter(Action<ProgressSnapshot> write)
{
    readonly SerialWorkQueue queue=new();
    Exception? failure;
    public event Action? Failed;
    public Task Enqueue(ProgressSnapshot snapshot)=>queue.Run(()=>
    {
        try{PerformanceLog.Measure("progress.write",()=>{write(snapshot);return true;});}
        catch(Exception ex){failure=ex;PerformanceLog.Write("progress.failed",error:ex);Failed?.Invoke();}
    });
    public async Task FlushAsync(){await queue.DrainAsync();if(failure!=null)throw new InvalidOperationException("Не удалось сохранить историю просмотра.",failure);}
    public Task DrainAsync()=>queue.DrainAsync();
}
