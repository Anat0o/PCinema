using Xunit;
using System.IO;
using Microsoft.Data.Sqlite;
namespace PCinema.Tests;
public class LibraryTests:IDisposable
{
 readonly string dir=Path.Combine(Path.GetTempPath(),"PCinema-tests",Guid.NewGuid().ToString("N"));readonly LibraryStore store;
 public LibraryTests(){store=new(dir);}
 [Theory]
 [InlineData(@"C:\TV\Show\Show.Name.2022.S01E02.1080p.mkv","Show Name",2022,1,2)]
 [InlineData(@"C:\TV\Dark\Dark.2x03.mp4","Dark",null,2,3)]
 [InlineData(@"C:\TV\Тьма\Сезон 2\Серия 03.mkv","Тьма",null,2,3)]
 [InlineData(@"C:\TV\Dark\Dark.S01E01E02.mkv","Dark",null,null,null)]
 [InlineData(@"C:\TV\Dark\random.mkv","Dark",null,null,null)]
 public void ParseNames(string path,string title,int? year,int? season,int? number){var p=NameParser.Parse(path);Assert.Equal(new ParsedName(title,year,season,number),p);}
 [Fact] public void ReimportDoesNotDuplicateOrResetProgress(){var path=Path.Combine(dir,"Show.S01E01.mkv");store.Import(path,NameParser.Parse(path));var e=store.Load().Single().Episodes.Single();new ProgressService(store).Save(e,42000,100000);store.Import(path.ToUpperInvariant(),NameParser.Parse(path));var fresh=new LibraryStore(dir).Load().Single().Episodes.Single();Assert.Equal(e.Id,fresh.Id);Assert.Equal(42000,fresh.Position);}
 [Fact] public void RelinkAndMetadataRetainIdentity(){var path=Path.Combine(dir,"Show.S01E01.mkv");store.Import(path,NameParser.Parse(path));var s=store.Load().Single();var e=s.Episodes.Single();new ProgressService(store).Save(e,50000,100000);store.Relink(e,Path.Combine(dir,"moved.mkv"));store.Correct(e,2,3);store.Metadata(s.Id,new(1,"Новое название","Show",2020,"Описание",null,null),null,null);var actual=store.Load().Single().Episodes.Single();Assert.Equal(e.Id,actual.Id);Assert.Equal(50000,actual.Position);Assert.Equal(2,actual.Season);}
 [Theory][InlineData(94000,false)][InlineData(95000,true)][InlineData(100000,true)]public void CompletionThreshold(long time,bool watched){var path=Path.Combine(dir,"Show.S01E01.mkv");store.Import(path,NameParser.Parse(path));var e=store.Load().Single().Episodes.Single();new ProgressService(store).Save(e,time,100000);Assert.Equal(watched,store.Load().Single().Episodes.Single().Watched);}
 [Fact]public void EndOfUnknownDurationMarksWatched(){var path=Path.Combine(dir,"Show.S01E01.mkv");store.Import(path,NameParser.Parse(path));var e=store.Load().Single().Episodes.Single();new ProgressService(store).Save(e,20,0,true);Assert.True(store.Load().Single().Episodes.Single().Watched);}
 [Fact]public async Task ScannerFindsNestedFilesAndPreservesMissing(){var media=Path.Combine(dir,"media","Show");Directory.CreateDirectory(media);var path=Path.Combine(media,"Show.S01E01.mkv");await File.WriteAllBytesAsync(path,[1,2,3]);store.AddFolder(Path.Combine(dir,"media"));using var scanner=new LibraryScanner(store);await scanner.ScanAsync();var e=store.Load().Single().Episodes.Single();new ProgressService(store).Save(e,20,100);File.Move(path,path+".away");await scanner.ScanAsync();var missing=store.Load().Single().Episodes.Single();Assert.False(missing.Available);Assert.Equal(20,missing.Position);File.Move(path+".away",path);await scanner.ScanAsync();Assert.True(store.Load().Single().Episodes.Single().Available);}
 [Fact]public async Task ScannerSkipsLockedFile(){var path=Path.Combine(dir,"Show.S01E01.mkv");store.AddFolder(dir);using(var f=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None)){f.WriteByte(1);f.Flush();using var scanner=new LibraryScanner(store);await scanner.ScanAsync();Assert.Empty(store.Load());}}
 [Fact]public void RelinkingAlreadyScannedPathMergesDuplicateWithoutLosingHistory(){var old=Path.Combine(dir,"Show.S01E01.mkv");var moved=Path.Combine(dir,"moved","Show.S01E01.mkv");store.Import(old,NameParser.Parse(old));var original=store.Load().Single().Episodes.Single();new ProgressService(store).Save(original,40000,100000);store.Import(moved,NameParser.Parse(moved));store.Relink(original,moved);var result=store.Load().Single().Episodes.Single();Assert.Equal(original.Id,result.Id);Assert.Equal(40000,result.Position);Assert.Equal(moved,result.Path);}
 public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
}
