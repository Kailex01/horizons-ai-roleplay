using HorizonsAI.Models;
using HorizonsAI.Services;

namespace HorizonsAI;

public static class AppConfig
{
    public static readonly string DataFolder       = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
    public static readonly string CharactersFolder = Path.Combine(DataFolder, "characters");
    public static readonly string PortraitsFolder  = Path.Combine(DataFolder, "portraits");
    public static readonly string PartiesFolder    = Path.Combine(DataFolder, "parties");
    public static readonly string ScenesFolder     = Path.Combine(DataFolder, "scenes");
    public static readonly string ChatLogsFolder   = Path.Combine(DataFolder, "chatlogs");
    public static readonly string TtsFolder        = Path.Combine(DataFolder, "tts");
    public static readonly string SettingsFile     = Path.Combine(DataFolder, "settings.json");
    public static readonly string LoreboookFile    = Path.Combine(DataFolder, "lorebook.json");

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        EnsureFolders();
        Current = SettingsService.Load();
    }

    public static void Save()  => SettingsService.Save(Current);
    public static void Apply(AppSettings updated) { Current = updated; Save(); }

    public static void Reset()
    {
        EnsureFolders();
        Current = new AppSettings();
        try { File.Delete(SettingsFile); } catch { }
    }

    private static void EnsureFolders()
    {
        Directory.CreateDirectory(CharactersFolder);
        Directory.CreateDirectory(PortraitsFolder);
        Directory.CreateDirectory(PartiesFolder);
        Directory.CreateDirectory(ScenesFolder);
        Directory.CreateDirectory(ChatLogsFolder);
        Directory.CreateDirectory(TtsFolder);
    }
}
