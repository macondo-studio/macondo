using System.Runtime.InteropServices;
using Avalonia;
using Macondo.App.Services;

namespace Macondo.App;

/// <summary>
/// Punto di ingresso principale dell'applicazione Macondo (VB6 & C# Architecture & Code Analyzer).
/// Gestisce l'avvio in modalità Desktop GUI con Avalonia o l'instradamento alla modalità CLI.
/// </summary>
internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
    private const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);

    /// <summary>
    /// Metodo Main di avvio. Se gli argomenti specificano comandi o parametri CLI,
    /// aggancia la console del processo chiamante ed esegue il <see cref="CliRunner"/>.
    /// In caso contrario avvia la GUI Desktop Avalonia.
    /// </summary>
    /// <param name="args">Argomenti da riga di comando passati all'applicazione.</param>
    /// <returns>Codice di uscita del processo.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        if (CliRunner.IsCliInvocation(args))
        {
            if (OperatingSystem.IsWindows() && AttachConsole(ATTACH_PARENT_PROCESS))
            {
                // In un'applicazione WinExe, .NET inizializza Console.Out come NullStream;
                // dopo aver eseguito AttachConsole occorre reinizializzare gli stream standard collegandoli alla console padre.
                var stdout = Console.OpenStandardOutput();
                Console.SetOut(new StreamWriter(stdout, new System.Text.UTF8Encoding(false)) { AutoFlush = true });

                var stderr = Console.OpenStandardError();
                Console.SetError(new StreamWriter(stderr, new System.Text.UTF8Encoding(false)) { AutoFlush = true });
            }

            try
            {
                Console.OutputEncoding = new System.Text.UTF8Encoding(false);
                Console.InputEncoding = new System.Text.UTF8Encoding(false);
            }
            catch
            {
                // Ignora nel caso l'ambiente host o la console non consentano l'override della codepage
            }

            return CliRunner.Run(args);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>
    /// Configura il framework Avalonia per l'esecuzione della GUI desktop.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
