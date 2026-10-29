using System.IO;
using Microsoft.Data.Sqlite;
using Xunit;
namespace PCinema.Tests;
public class SubtitleAppearanceTests
{
    [Fact] public void ArbitraryHexAndPixelSizeProduceVlcOptions()
    {
        var value=new SubtitleAppearance(Color:"#a1b2c3",PixelSize:37).Normalize();Assert.Equal("A1B2C3",value.Color);Assert.Equal(37,value.EffectivePixelSize);Assert.Contains("--freetype-fontsize=37",value.Options());Assert.Contains("--freetype-color=10597059",value.Options());
        Assert.Equal(12,new SubtitleAppearance(PixelSize:1).Normalize().PixelSize);Assert.Equal(160,new SubtitleAppearance(PixelSize:999).Normalize().PixelSize);Assert.False(SubtitleAppearance.ValidColor("#XYZ123"));
    }
    [Fact] public void LegacyRelativeSizeRemainsUntilExplicitPixelSelection()
    {
        var legacy=System.Text.Json.JsonSerializer.Deserialize<SubtitleAppearance>("{\"Size\":12,\"Color\":\"FFE066\"}")!;Assert.Null(legacy.PixelSize);Assert.Contains("--freetype-fontsize=0",legacy.Options());Assert.Contains("--freetype-rel-fontsize=12",legacy.Options());
    }
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
