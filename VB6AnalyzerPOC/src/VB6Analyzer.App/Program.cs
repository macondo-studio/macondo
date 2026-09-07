using Avalonia;

namespace Macondo.App;

internal static class Program
{
    // Punto di ingresso. Evitare inizializzazione pesante qui: obiettivo = startup rapido.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
