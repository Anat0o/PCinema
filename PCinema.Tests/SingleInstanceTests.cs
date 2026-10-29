using Xunit;
namespace PCinema.Tests;
public class SingleInstanceTests
{
    [Fact] public async Task RepeatedLaunchSignalsPrimaryAndDoesNotBecomeAnotherInstance()
    {
        var scope=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString());
        using var primary=new SingleInstance(scope);Assert.True(primary.IsPrimary);
        var activated=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using(var secondary=new SingleInstance(scope)){Assert.False(secondary.IsPrimary);secondary.ActivateExisting();}
        primary.Listen(()=>activated.TrySetResult());
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var third=new SingleInstance(scope);Assert.False(third.IsPrimary);
    }
    [Fact] public void ExitReleasesInstanceAndIndependentProfilesCanRun()
    {
        var scope=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid().ToString());
        using(var first=new SingleInstance(scope)){Assert.True(first.IsPrimary);using var other=new SingleInstance(scope+"-other");Assert.True(other.IsPrimary);}
        using var restarted=new SingleInstance(scope);Assert.True(restarted.IsPrimary);
    }
}
