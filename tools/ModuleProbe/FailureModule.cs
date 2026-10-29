using PCinema.ModuleApi;
internal sealed class FailureModule : IPCinemaModule
{
    public void Register(IModuleRegistration registration)=>registration.AddPage(new("never","Must not appear",_=>throw new InvalidOperationException()));
    public Task StartAsync(IModuleContext context,CancellationToken cancellationToken)=>throw new InvalidOperationException("Intentional startup failure");
    public Task StopAsync(CancellationToken cancellationToken)=>Task.CompletedTask;
}
