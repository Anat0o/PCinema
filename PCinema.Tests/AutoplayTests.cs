using Xunit;
namespace PCinema.Tests;
public class AutoplayTests
{
    [Fact] public void CountdownRunsOnceAndCanBeCancelled()
    {
        var a=new AutoplayController();var now=DateTime.UtcNow;
        a.Start(true,true,now);Assert.True(a.Pending);Assert.Equal(5,a.Remaining(now));
        Assert.False(a.TakeDue(now.AddSeconds(4)));Assert.True(a.TakeDue(now.AddSeconds(5)));Assert.False(a.TakeDue(now.AddSeconds(6)));
        a.Start(true,true,now);a.Cancel();Assert.False(a.TakeDue(now.AddSeconds(8)));
        a.Start(false,true,now);Assert.False(a.Pending);a.Start(true,false,now);Assert.False(a.Pending);
    }
}
