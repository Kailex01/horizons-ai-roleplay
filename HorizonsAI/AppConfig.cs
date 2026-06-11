using HorizonsAI.Models;
using HorizonsAI.Services;

namespace HorizonsAI;

public static class AppConfig
{
    public static AppSettings Current { get; private set; } = new();

    public static void Load()  => Current = SettingsService.Load();
    public static void Save()  => SettingsService.Save(Current);
    public static void Apply(AppSettings updated)
    {
        Current = updated;
        Save();
    }

    public const string Game            = "eqemu";
    public const string DefaultLocation = "Unknown";
    public const string DefaultChannel  = "say";
}
