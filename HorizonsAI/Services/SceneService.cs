using HorizonsAI.Models;

namespace HorizonsAI.Services;

public static class SceneService
{
    private static string ScenesFolder => Path.Combine(AppConfig.DataFolder, "scenes");

    public static List<Scene> LoadAll()
    {
        var list = new List<Scene>();
        if (!Directory.Exists(ScenesFolder)) return list;
        foreach (var file in Directory.EnumerateFiles(ScenesFolder, "*.json"))
        {
            try
            {
                var s = JsonSerializer.Deserialize<Scene>(File.ReadAllText(file));
                if (s != null && s.Enabled) list.Add(s);
            }
            catch { }
        }
        return list.OrderBy(s => s.Name).ToList();
    }

    public static void Save(Scene s)
    {
        Directory.CreateDirectory(ScenesFolder);
        File.WriteAllText(
            Path.Combine(ScenesFolder, $"{s.Id}.json"),
            JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static void Delete(Scene s)
    {
        var path = Path.Combine(ScenesFolder, $"{s.Id}.json");
        if (File.Exists(path)) File.Delete(path);
    }
}
