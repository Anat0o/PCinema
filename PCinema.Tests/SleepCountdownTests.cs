using PCinema.Examples;
using Xunit;
namespace PCinema.Tests;
public sealed class SleepCountdownTests
{
    [Fact] public async Task ElapsedInvokesPauseExactlyOnce()
    {
        int count=0;var timer=new SleepCountdown((_,_)=>Task.CompletedTask);
        await timer.Arm(TimeSpan.FromMinutes(1),()=>{count++;return Task.CompletedTask;});await timer.StopAsync();Assert.Equal(1,count);
    }
    [Fact] public async Task StopCancelsPendingPause()
    {
        int count=0;var timer=new SleepCountdown((_,ct)=>Task.Delay(Timeout.Infinite,ct));
        _=timer.Arm(TimeSpan.FromMinutes(1),()=>{count++;return Task.CompletedTask;});await timer.StopAsync();Assert.Equal(0,count);
    }
    [Fact] public async Task StopWaitsForAlreadyExecutingAction()
    {
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var timer=new SleepCountdown((_,_)=>Task.CompletedTask);
        _=timer.Arm(TimeSpan.Zero,()=>release.Task);var stop=timer.StopAsync();Assert.False(stop.IsCompleted);release.SetResult();await stop;
    }
    [Fact] public async Task RearmingCancelsPreviousCountdown()
    {
        int count=0;int requests=0;var timer=new SleepCountdown((_,ct)=>++requests==1?Task.Delay(Timeout.Infinite,ct):Task.CompletedTask);
        _=timer.Arm(TimeSpan.FromMinutes(1),()=>{count+=100;return Task.CompletedTask;});await timer.Arm(TimeSpan.Zero,()=>{count++;return Task.CompletedTask;});await timer.StopAsync();Assert.Equal(1,count);
    }
}
