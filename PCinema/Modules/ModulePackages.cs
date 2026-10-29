using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace PCinema.Modules;
public enum ModuleKind { Feature, Skin, Theme, Font }
public sealed record ModuleManifest(string Id,string Name,string Author,string Version,ModuleKind Kind,int ApiVersion=1,string? EntryAssembly=null,string? EntryType=null,string[]? Capabilities=null,string? FontFile=null,string? FontFamily=null,string? ThemeFile=null);
public sealed record ModuleRecord(ModuleManifest Manifest,string Version,string? PreviousVersion=null,bool Enabled=false,bool Removed=false);
public sealed record ModuleRegistry(List<ModuleRecord> Modules);
public sealed class ModulePackages
{
    public static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNameCaseInsensitive=true,Converters={new JsonStringEnumConverter()}};
    public string Root {get;}
    readonly object gate=new();
    public ModulePackages(string root){Root=Path.GetFullPath(root);Directory.CreateDirectory(Root);}
    public string PackageDirectory(ModuleRecord record)=>Path.Combine(Root,"packages",record.Manifest.Id,record.Version);
    public string DataDirectory(string id){ValidateId(id);return Path.Combine(Root,"data",id);}
    public ModuleRegistry Read()
    {
        lock(gate)
        {
            var file=Path.Combine(Root,"registry.json");
            if(!File.Exists(file))return new([]);
            var result=JsonSerializer.Deserialize<ModuleRegistry>(File.ReadAllText(file),Json)??throw new InvalidDataException("Повреждён реестр модулей.");
            if(result.Modules==null||result.Modules.Select(r=>r.Manifest.Id).Distinct().Count()!=result.Modules.Count)throw new InvalidDataException("Повреждён реестр модулей.");
            foreach(var r in result.Modules){Validate(r.Manifest);ValidateVersion(r.Version);if(r.Manifest.Version!=r.Version)throw new InvalidDataException("Версия реестра не совпадает с манифестом.");if(r.PreviousVersion!=null)ValidateVersion(r.PreviousVersion);}
            return result;
        }
    }
    public void Update(Func<ModuleRegistry,ModuleRegistry> update)
    {
        lock(gate){var next=update(Read());WriteAtomic(Path.Combine(Root,"registry.json"),JsonSerializer.Serialize(next,Json));}
    }
    public void ResetRegistry()
    {
        lock(gate)
        {
            var file=Path.Combine(Root,"registry.json");
            if(File.Exists(file))File.Copy(file,Path.Combine(Root,"registry.backup-"+Guid.NewGuid().ToString("N")+".json"));
            // Quarantine packages too so the same version can be installed again.
            var packages=Path.Combine(Root,"packages");
            if(Directory.Exists(packages))Directory.Move(packages,Path.Combine(Root,"packages.backup-"+Guid.NewGuid().ToString("N")));
            WriteAtomic(file,JsonSerializer.Serialize(new ModuleRegistry([]),Json));
        }
    }
    public static void WriteAtomic(string path,string text){Directory.CreateDirectory(Path.GetDirectoryName(path)!);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temp,text);try{File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
    static void ValidateId(string id){if(string.IsNullOrEmpty(id)||!Regex.IsMatch(id,@"\A[a-z][a-z0-9]*(?:[.-][a-z0-9]+)+\z")||id.Length>100)throw new InvalidDataException("Некорректный ID модуля.");}
    static void ValidateVersion(string value){if(!Regex.IsMatch(value??"",@"\A\d{1,5}\.\d{1,5}\.\d{1,5}\z")||!Version.TryParse(value,out _))throw new InvalidDataException("Некорректная версия.");}
    static void Validate(ModuleManifest m)
    {
        ValidateId(m.Id);ValidateVersion(m.Version);
        if(m.ApiVersion!=1||!Enum.IsDefined(m.Kind))throw new InvalidDataException("Несовместимая версия API или тип модуля.");
        if(string.IsNullOrWhiteSpace(m.Name)||string.IsNullOrWhiteSpace(m.Author))throw new InvalidDataException("Не указаны название или автор.");
        if(m.Kind is ModuleKind.Feature or ModuleKind.Skin&&(string.IsNullOrWhiteSpace(m.EntryAssembly)||string.IsNullOrWhiteSpace(m.EntryType)))throw new InvalidDataException("Не указана точка входа.");
        if(m.Kind==ModuleKind.Font&&(string.IsNullOrWhiteSpace(m.FontFile)||string.IsNullOrWhiteSpace(m.FontFamily)))throw new InvalidDataException("Не указан шрифт.");
        if(m.Kind==ModuleKind.Theme&&string.IsNullOrWhiteSpace(m.ThemeFile))throw new InvalidDataException("Не указана тема.");
    }
    public static string SafePath(string root,string relative)
    {
        if(string.IsNullOrWhiteSpace(relative)||relative.Contains(':')||relative.Contains('\\')||Path.IsPathRooted(relative)||relative.Split('/').Any(p=>p==".."||p=="."||p.EndsWith('.')||p.EndsWith(' ')))throw new InvalidDataException("Недопустимый путь в пакете.");
        var result=Path.GetFullPath(Path.Combine(root,relative));
        if(!result.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Путь выходит за каталог пакета.");
        return result;
    }
    public ModuleManifest Inspect(string archive)
    {
        using var zip=ZipFile.OpenRead(archive);return Inspect(zip);
    }
    ModuleManifest Inspect(ZipArchive zip)
    {
        if(zip.Entries.Count>4096)throw new InvalidDataException("Слишком много файлов.");
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
        foreach(var e in zip.Entries)
        {
            SafePath(Root,e.FullName.TrimEnd('/'));
            if(!names.Add(e.FullName)||((e.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Повторяющийся путь или ссылка в пакете.");
            total+=e.Length;if(e.Length>150L*1024*1024||total>512L*1024*1024)throw new InvalidDataException("Пакет слишком большой.");
        }
        var entry=zip.GetEntry("manifest.json")??throw new InvalidDataException("Нет manifest.json.");
        if(entry.Length>64*1024)throw new InvalidDataException("Слишком большой манифест.");
        using var stream=entry.Open();var m=JsonSerializer.Deserialize<ModuleManifest>(stream,Json)??throw new InvalidDataException("Некорректный манифест.");Validate(m);
        foreach(var path in new[]{m.EntryAssembly,m.FontFile,m.ThemeFile}.Where(x=>x!=null)){SafePath(Root,path!);if(!names.Contains(path!))throw new InvalidDataException("Отсутствует файл из манифеста.");}
        if(m.Kind is ModuleKind.Font or ModuleKind.Theme)
        {
            var allowed=new[]{".json",".ttf",".otf",".png",".jpg",".jpeg",".txt",".md"};
            if(zip.Entries.Any(e=>e.Name.Length>0&&!allowed.Contains(Path.GetExtension(e.FullName).ToLowerInvariant())))throw new InvalidDataException("Пакет оформления содержит исполняемые или неподдерживаемые файлы.");
        }
        if(m.Kind==ModuleKind.Font&&!new[]{".ttf",".otf"}.Contains(Path.GetExtension(m.FontFile!).ToLowerInvariant()))throw new InvalidDataException("Ожидается TTF или OTF.");
        if(m.Kind==ModuleKind.Theme){using var theme=zip.GetEntry(m.ThemeFile!)!.Open();ThemeValues.Validate(JsonSerializer.Deserialize<Dictionary<string,string>>(theme,Json)??[]);}
        return m;
    }
    public ModuleManifest Install(string archive,bool trustCode=false)
    {
        lock(gate)
        {
            using var zip=ZipFile.OpenRead(archive);var m=Inspect(zip);
            if(m.Kind is ModuleKind.Feature or ModuleKind.Skin&&!trustCode)throw new InvalidOperationException("Для программного модуля нужно подтвердить доверие к коду.");
            var existing=Read().Modules.FirstOrDefault(r=>r.Manifest.Id==m.Id);
            if(existing!=null&&existing.Manifest.Kind!=m.Kind)throw new InvalidDataException("Нельзя менять тип установленного модуля при обновлении.");
            var target=Path.Combine(Root,"packages",m.Id,m.Version);if(Directory.Exists(target))throw new InvalidOperationException("Эта версия уже установлена. Для изменений увеличьте версию пакета.");
            var staging=Path.Combine(Root,"staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
            try
            {
                foreach(var e in zip.Entries){var path=SafePath(staging,e.FullName.TrimEnd('/'));if(e.Name.Length==0){Directory.CreateDirectory(path);continue;}Directory.CreateDirectory(Path.GetDirectoryName(path)!);e.ExtractToFile(path);}
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);Directory.Move(staging,target);
                try{Update(reg=>{var previous=reg.Modules.FirstOrDefault(x=>x.Manifest.Id==m.Id);reg.Modules.RemoveAll(x=>x.Manifest.Id==m.Id);reg.Modules.Add(new(m,m.Version,previous is {Removed:false}?previous.Version:null,previous?.Enabled??false));return reg;});}
                catch{Directory.Delete(target,true);throw;}
            }
            finally{if(Directory.Exists(staging))Directory.Delete(staging,true);}
            return m;
        }
    }
    public void Enable(string id,bool enabled)=>Update(reg=>new(reg.Modules.Select(r=>r.Manifest.Id==id?r with{Enabled=enabled,Removed=false}:enabled&&reg.Modules.Any(x=>x.Manifest.Id==id&&x.Manifest.Kind==r.Manifest.Kind)&&r.Manifest.Kind!=ModuleKind.Feature?r with{Enabled=false}:r).ToList()));
    public void Remove(string id)=>Update(reg=>new(reg.Modules.Select(r=>r.Manifest.Id==id?r with{Enabled=false,Removed=true}:r).ToList()));
    public void Rollback(string id)=>Update(reg=>new(reg.Modules.Select(r=>
    {
        if(r.Manifest.Id!=id||r.PreviousVersion==null)return r;
        var file=Path.Combine(Root,"packages",id,r.PreviousVersion,"manifest.json");var manifest=JsonSerializer.Deserialize<ModuleManifest>(File.ReadAllText(file),Json)!;Validate(manifest);
        if(manifest.Id!=id||manifest.Version!=r.PreviousVersion||manifest.Kind!=r.Manifest.Kind)throw new InvalidDataException("Повреждён манифест предыдущей версии.");
        return new ModuleRecord(manifest,r.PreviousVersion,r.Version,r.Enabled);
    }).ToList()));
}
