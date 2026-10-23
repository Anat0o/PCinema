namespace PCinema;

/// <summary>FIFO work; a failed operation never poisons subsequent operations.</summary>
public sealed class SerialWorkQueue
{
    readonly object sync=new();
    Task tail=Task.CompletedTask;
    public Task Run(Action work)=>Run(()=>{work();return true;});
    public Task<T> Run<T>(Func<T> work)
    {
        lock(sync)
        {
            var before=tail;
            var next=Task.Run(async()=>{try{await before.ConfigureAwait(false);}catch{}return work();});
            tail=next;return next;
        }
    }
    public Task DrainAsync(){lock(sync)return tail;}
}
