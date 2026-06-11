namespace GukChat;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppConfig.Load();
        base.OnStartup(e);
    }
}
