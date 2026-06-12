namespace HorizonsAI.Models;

public class StatBlock
{
    [JsonPropertyName("str")] public int Str { get; set; } = 10;
    [JsonPropertyName("dex")] public int Dex { get; set; } = 10;
    [JsonPropertyName("con")] public int Con { get; set; } = 10;
    [JsonPropertyName("int")] public int Int { get; set; } = 10;
    [JsonPropertyName("wis")] public int Wis { get; set; } = 10;
    [JsonPropertyName("cha")] public int Cha { get; set; } = 10;

    public int Mod(int score) => (int)Math.Floor((score - 10) / 2.0);

    public int GetScore(string name) => name.ToUpperInvariant() switch
    {
        "STR" => Str, "DEX" => Dex, "CON" => Con,
        "INT" => Int, "WIS" => Wis, "CHA" => Cha,
        _ => 10
    };

    public int GetMod(string name) => Mod(GetScore(name));

    public string ModStr(int score)
    {
        var m = Mod(score);
        return m >= 0 ? $"+{m}" : $"{m}";
    }
}
