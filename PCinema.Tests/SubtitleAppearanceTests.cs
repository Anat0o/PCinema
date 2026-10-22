using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;
namespace PCinema.Tests;
public class SubtitleAppearanceTests
{
    [Fact] public void AppearanceSurvivesRestartAndCorruptSettingsFallBack()
    {
        var dir=Path.Combine(Path.GetTempPath(),"PCinema-tests",Guid.NewGuid().ToString("N"));
        try
        {
            var store=new LibraryStore(dir);var selected=new SubtitleAppearance(12,"Verdana","FFE066","Тёмный фон",true);
            selected.Save(store);Assert.Equal(selected,SubtitleAppearance.Load(new LibraryStore(dir)));
            Assert.Contains("--freetype-background-opacity=180",selected.Options());Assert.Contains("--freetype-bold",selected.Options());
            store.Setting("subtitle_appearance","broken");Assert.Equal(new SubtitleAppearance(),SubtitleAppearance.Load(store));
            Assert.Equal(new SubtitleAppearance(),new SubtitleAppearance(-1,"bad","bad","bad").Normalize());
        }
        finally{SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    }
}
