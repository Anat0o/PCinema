namespace PCinema;

public sealed class AutoplayController
{
    DateTime? deadline;
    public bool Pending=>deadline.HasValue;
    public void Start(bool enabled,bool hasNext,DateTime now)=>deadline=enabled&&hasNext?now.AddSeconds(5):null;
    public int Remaining(DateTime now)=>deadline.HasValue?Math.Max(0,(int)Math.Ceiling((deadline.Value-now).TotalSeconds)):0;
    public bool TakeDue(DateTime now)
    {
        if(!deadline.HasValue||now<deadline.Value)return false;
        deadline=null;return true;
    }
    public void Cancel()=>deadline=null;
}
