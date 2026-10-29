using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace PCinema;

public sealed class SingleInstance : IDisposable
{
    readonly Mutex lifetime;
    readonly EventWaitHandle activation;
    RegisteredWaitHandle? listener;
    public bool IsPrimary { get; }
    public SingleInstance(string dataDirectory)
    {
        var scope=Environment.UserName+"|"+Path.GetFullPath(dataDirectory).TrimEnd('\\','/').ToUpperInvariant();
        var name="Local\\PCinema."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)));
        // Create the event first: a launch during startup must not lose its activation.
        activation=new EventWaitHandle(false,EventResetMode.AutoReset,name+".activate");
        lifetime=new Mutex(false,name+".instance",out bool created);
        IsPrimary=created;
    }
    public void ActivateExisting()=>activation.Set();
    public void Listen(Action activate)=>listener=ThreadPool.RegisterWaitForSingleObject(activation,(_,_)=>activate(),null,Timeout.Infinite,false);
    public void Dispose(){listener?.Unregister(null);activation.Dispose();lifetime.Dispose();}
}
