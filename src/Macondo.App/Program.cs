using System.Runtime.InteropServices;
using Avalonia;
using Macondo.App.Services;

namespace Macondo.App;

/// <summary>
/// Main entry point of the Macondo application (VB6 & C# Architecture & Code Analyzer).
/// Handles booting into Desktop GUI mode with Avalonia or routing to CLI mode.
/// </summary>
internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
    private const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);

    /// <summary>
    /// Main boot method. If the arguments specify CLI commands or parameters,
    /// hooks the console of the calling process and executes the <see cref="CliRunner"/>.
    /// Otherwise launch the Avalonia Desktop GUI.
    /// </summary>
    /// <param name="args">Command line arguments passed to the application.</param>
    /// <returns>Process exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        if (CliRunner.IsCliInvocation(args))
        {
            if (OperatingSystem.IsWindows() && AttachConsole(ATTACH_PARENT_PROCESS))
            {
                // In a WinExe application, .NET initializes Console.Out as NullStream;
                // after running AttachConsole you need to reinitialize the standard streams by connecting them to the parent console.
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
                // Ignore if the host environment or console does not allow code page overriding
            }

            return CliRunner.Run(args);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    /// <summary>
    /// Configure the Avalonia framework to run the desktop GUI.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
}
