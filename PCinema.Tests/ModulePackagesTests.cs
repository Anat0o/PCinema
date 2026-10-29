using System.IO;
using System.IO.Compression;
using System.Text.Json;
using PCinema.Modules;
using Xunit;
namespace PCinema.Tests;
public sealed class ModulePackagesTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"PCinema-modules-"+Guid.NewGuid().ToString("N"));
    ModulePackages Store=>new(Path.Combine(root,"store"));
    string Package(string version="1.0.0",ModuleKind kind=ModuleKind.Theme,int api=1,params (string Name,string Text)[] extras)
    {
        Directory.CreateDirectory(root);var file=Path.Combine(root,Guid.NewGuid()+".pcmodule");using var zip=ZipFile.Open(file,ZipArchiveMode.Create);
        var m=new ModuleManifest("test.example","Test","Tester",version,kind,api,kind==ModuleKind.Feature?"plugin.dll":null,kind==ModuleKind.Feature?"Plugin":null,ThemeFile:kind==ModuleKind.Theme?"theme.json":null);
        void Add(string name,string value){using var w=new StreamWriter(zip.CreateEntry(name).Open());w.Write(value);}
        Add("manifest.json",JsonSerializer.Serialize(m,ModulePackages.Json));if(kind==ModuleKind.Theme)Add("theme.json","{\"App.Background\":\"#123456\"}");if(kind==ModuleKind.Feature)Add("plugin.dll","fake");foreach(var e in extras)Add(e.Name,e.Text);return file;
    }
    [Fact] public void InstallEnableUpgradeRollbackAndRemovePreserveData()
    {
        var store=Store;store.Install(Package());store.Enable("test.example",true);var data=store.DataDirectory("test.example");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"state"),"keep");
        store.Install(Package("2.0.0"));var r=Assert.Single(store.Read().Modules);Assert.True(r.Enabled);Assert.Equal("1.0.0",r.PreviousVersion);
        store.Rollback("test.example");Assert.Equal("1.0.0",Assert.Single(store.Read().Modules).Version);store.Remove("test.example");Assert.True(Assert.Single(store.Read().Modules).Removed);Assert.Equal("keep",File.ReadAllText(Path.Combine(data,"state")));
    }
    [Theory][InlineData("../escape")][InlineData("C:/escape")][InlineData("dir\\escape")][InlineData("/absolute")][InlineData("dir/../escape")]
    public void RejectsUnsafeArchivePaths(string path)=>Assert.Throws<InvalidDataException>(()=>Store.Install(Package(extras:[(path,"bad")])));
    [Fact] public void RejectsDuplicatePathsAndExecutableTheme()=>Assert.Throws<InvalidDataException>(()=>Store.Install(Package(extras:[("run.dll","bad")])));
    [Fact] public void DuplicatePathsAreRejected()=>Assert.Throws<InvalidDataException>(()=>Store.Install(Package(extras:[("THEME.JSON","{}")] )));
    [Fact] public void IncompatibleApiDoesNotReplaceCurrent()
    {
        Store.Install(Package());Assert.Throws<InvalidDataException>(()=>Store.Install(Package("2.0.0",api:2)));Assert.Equal("1.0.0",Assert.Single(Store.Read().Modules).Version);
    }
    [Fact] public void CodeRequiresTrust(){var package=Package(kind:ModuleKind.Feature);Assert.Throws<InvalidOperationException>(()=>Store.Install(package));Store.Install(package,true);Assert.False(Assert.Single(Store.Read().Modules).Enabled);}
    [Fact] public void CorruptPackageDoesNotDamageRegistry(){Store.Install(Package());var bad=Path.Combine(root,"broken");File.WriteAllText(bad,"bad");Assert.Throws<InvalidDataException>(()=>Store.Install(bad));Assert.Single(Store.Read().Modules);}
    [Fact] public void ThemeValuesRejectUnknownOrUnsafeValues(){Assert.Throws<InvalidDataException>(()=>ThemeValues.Validate(new Dictionary<string,string>{{"x","#111111"}}));Assert.Throws<InvalidDataException>(()=>ThemeValues.Validate(new Dictionary<string,string>{{"App.FontSize","NaN"}}));}
    [Fact] public void SameVersionCannotOverwriteInstalledFiles(){var package=Package();Store.Install(package);Assert.Throws<InvalidOperationException>(()=>Store.Install(package));Assert.Single(Store.Read().Modules);}
    [Fact] public void CannotChangePackageTypeOnUpdate(){Store.Install(Package());Assert.Throws<InvalidDataException>(()=>Store.Install(Package("2.0.0",ModuleKind.Feature),true));Assert.Equal(ModuleKind.Theme,Assert.Single(Store.Read().Modules).Manifest.Kind);}
    [Fact] public void RegistryRecoveryPreservesDataAndAllowsReinstallation()
    {
        var store=Store;var package=Package();store.Install(package);var data=store.DataDirectory("test.example");Directory.CreateDirectory(data);File.WriteAllText(Path.Combine(data,"keep"),"keep");
        File.WriteAllText(Path.Combine(store.Root,"registry.json"),"broken");store.ResetRegistry();Assert.Empty(store.Read().Modules);store.Install(package);Assert.Single(store.Read().Modules);Assert.True(File.Exists(Path.Combine(data,"keep")));Assert.Single(Directory.GetFiles(store.Root,"registry.backup-*.json"));
    }
    [Fact] public void TamperedRollbackManifestCannotChangeIdentity()
    {
        var store=Store;store.Install(Package());store.Install(Package("2.0.0"));var old=Path.Combine(store.Root,"packages/test.example/1.0.0/manifest.json");File.WriteAllText(old,File.ReadAllText(old).Replace("test.example","test.another"));Assert.Throws<InvalidDataException>(()=>store.Rollback("test.example"));Assert.Equal("2.0.0",Assert.Single(store.Read().Modules).Version);
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
