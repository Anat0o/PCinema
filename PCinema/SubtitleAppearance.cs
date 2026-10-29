using System.Text.Json;
namespace PCinema;

public sealed record SubtitleAppearance(int Size=16, string Font="Segoe UI", string Color="FFFFFF", string Style="Обводка", bool Bold=false, int? PixelSize=null)
{
    public static readonly int[] Sizes=[20,16,12,8];
    public static readonly string[] Fonts=["Segoe UI","Arial","Verdana","Consolas"];
    public static readonly string[] Colors=["FFFFFF","FFE066","80E5FF"];
    public static readonly string[] Styles=["Обводка","Контрастная обводка","Тёмный фон"];
    public static bool ValidColor(string? color)=>System.Text.RegularExpressions.Regex.IsMatch(color?.Trim().TrimStart('#')??"", "\\A[0-9a-fA-F]{6}\\z");
    public int EffectivePixelSize=>PixelSize??Math.Clamp(1080/(Size>0?Size:16),12,160);
    public SubtitleAppearance Normalize()=>new(Sizes.Contains(Size)?Size:16,Fonts.Contains(Font)?Font:"Segoe UI",ValidColor(Color)?Color.Trim().TrimStart('#').ToUpperInvariant():"FFFFFF",Styles.Contains(Style)?Style:Styles[0],Bold,PixelSize.HasValue?Math.Clamp(PixelSize.Value,12,160):null);
    public static SubtitleAppearance Load(LibraryStore store)
    {
        try{return (JsonSerializer.Deserialize<SubtitleAppearance>(store.Setting("subtitle_appearance")??"null")??new()).Normalize();}
        catch(JsonException){return new();}
    }
    public void Save(LibraryStore store)=>store.Setting("subtitle_appearance",JsonSerializer.Serialize(Normalize()));
    public string[] Options()
    {
        var v=Normalize();
        return ["--no-video-title-show",$"--freetype-font={v.Font}",$"--freetype-fontsize={v.PixelSize??0}",$"--freetype-rel-fontsize={v.Size}",
            $"--freetype-color={Convert.ToInt32(v.Color,16)}",v.Bold?"--freetype-bold":"--no-freetype-bold",
            "--freetype-outline-color=0",$"--freetype-outline-thickness={(v.Style==Styles[1]?6:2)}",
            "--freetype-background-color=0",$"--freetype-background-opacity={(v.Style==Styles[2]?180:0)}"];
    }
}
