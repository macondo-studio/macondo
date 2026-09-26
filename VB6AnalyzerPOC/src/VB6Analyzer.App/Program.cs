using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Export;

namespace Macondo.App;

internal static class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);
    private const uint ATTACH_PARENT_PROCESS = unchecked((uint)-1);

    [STAThread]
    public static int Main(string[] args)
    {
        if (IsCliInvocation(args))
        {
            if (OperatingSystem.IsWindows())
            {
                AttachConsole(ATTACH_PARENT_PROCESS);
            }
            return RunCli(args);
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();

    private static bool IsCliInvocation(string[] args)
    {
        if (args == null || args.Length == 0) return false;
        return args.Any(a => a is "analyze" or "--export-md" or "-m" or "--export-json" or "-j" or "--help" or "-h" or "help" or "/?");
    }

    private static int RunCli(string[] args)
    {
        if (args.Any(a => a is "-h" or "--help" or "help" or "/?"))
        {
            PrintCliHelp();
            return 0;
        }

        string? exportMdPath = null;
        string? exportJsonPath = null;
        var inputPaths = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.Equals("analyze", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            else if (arg.Equals("--export-md", StringComparison.OrdinalIgnoreCase) || arg.Equals("-m", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    exportMdPath = args[++i];
                }
                else
                {
                    Console.Error.WriteLine("[Error] Missing output file path for --export-md");
                    return 1;
                }
            }
            else if (arg.Equals("--export-json", StringComparison.OrdinalIgnoreCase) || arg.Equals("-j", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    exportJsonPath = args[++i];
                }
                else
                {
                    Console.Error.WriteLine("[Error] Missing output file path for --export-json");
                    return 1;
                }
            }
            else if (!arg.StartsWith("-"))
            {
                inputPaths.Add(arg);
            }
        }

        if (inputPaths.Count == 0)
        {
            Console.Error.WriteLine("[Error] No target project, group or folder path specified for analysis.");
            PrintCliHelp();
            return 1;
        }

        if (exportMdPath == null && exportJsonPath == null)
        {
            Console.Error.WriteLine("[Error] Specify at least one export format: --export-md <file.md> or --export-json <file.json>");
            return 1;
        }

        try
        {
            Console.WriteLine($"[Macondo] Starting analysis of {inputPaths.Count} path(s)...");
            var projects = ProjectDescriptor.DiscoverFromPaths(inputPaths);
            if (projects.Count == 0)
            {
                Console.Error.WriteLine($"[Error] No supported project files (.vbp, .vbg, .csproj, .sln) or source files found in: {string.Join(", ", inputPaths)}");
                return 1;
            }

            var totalFiles = projects.Sum(p => p.SourceFiles.Count);
            Console.WriteLine($"[Macondo] Discovered {projects.Count} project(s), total {totalFiles} source file(s).");

            var progress = new Progress<ProjectAnalysisProgress>(p =>
            {
                if (!string.IsNullOrEmpty(p.CurrentFileName))
                {
                    Console.WriteLine($"[{p.CurrentFileIndex}/{p.TotalFiles}] {p.CurrentFileName} ({p.ExtractedNodesCount} symbols)");
                }
                else if (!string.IsNullOrEmpty(p.PhaseMessage))
                {
                    Console.WriteLine($" -> {p.PhaseMessage}");
                }
            });

            var visualGraph = ProjectAnalysisService.AnalyzeProjects(projects, progress);
            var semanticGraph = visualGraph.SemanticGraph;
            if (semanticGraph == null)
            {
                Console.Error.WriteLine("[Error] Semantic analysis produced an empty CodeGraph.");
                return 1;
            }

            Console.WriteLine($"[Macondo] Analysis complete: {semanticGraph.Symbols.Count} symbols, {semanticGraph.Relationships.Count} relationships.");

            if (!string.IsNullOrEmpty(exportMdPath))
            {
                var fullMdPath = Path.GetFullPath(exportMdPath);
                var mdDir = Path.GetDirectoryName(fullMdPath);
                if (!string.IsNullOrEmpty(mdDir) && !Directory.Exists(mdDir))
                    Directory.CreateDirectory(mdDir);

                var mdContent = CodeGraphMarkdownExporter.Export(semanticGraph);
                File.WriteAllText(fullMdPath, mdContent, Encoding.UTF8);
                Console.WriteLine($"[OK] Markdown Repo Map exported to: {fullMdPath}");
            }

            if (!string.IsNullOrEmpty(exportJsonPath))
            {
                var fullJsonPath = Path.GetFullPath(exportJsonPath);
                var jsonDir = Path.GetDirectoryName(fullJsonPath);
                if (!string.IsNullOrEmpty(jsonDir) && !Directory.Exists(jsonDir))
                    Directory.CreateDirectory(jsonDir);

                var jsonContent = CodeGraphJsonExporter.ExportToJson(semanticGraph, indented: true);
                File.WriteAllText(fullJsonPath, jsonContent, Encoding.UTF8);
                Console.WriteLine($"[OK] CodeGraph JSON exported to: {fullJsonPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Fatal Error] Analysis failed: {ex.Message}");
            return 1;
        }
    }

    private static void PrintCliHelp()
    {
        Console.WriteLine(@"
Macondo — Architecture & Code Analyzer (CLI Mode)

Usage:
  Macondo.exe analyze <path_to_vbp_or_folder> [options]

Options:
  --export-md <file.md>, -m <file.md>        Export hierarchical Repo Map in Markdown format
  --export-json <file.json>, -j <file.json>  Export complete CodeGraph in JSON format
  --help, -h                                 Show this help message

Examples:
  # Esporta la Repo Map Markdown del progetto
  Macondo.exe analyze ""C:\Progetti\MioProgettoVB6"" --export-md ""repo-map.md""

  # Esporta il grafo semantico completo in JSON
  Macondo.exe analyze ""C:\Progetti\MioProgettoVB6\App.vbp"" --export-json ""graph.json""

  # Esporta entrambi i formati contemporaneamente
  Macondo.exe analyze ""C:\Progetti\MioProgettoVB6"" --export-md ""repo-map.md"" --export-json ""graph.json""
");
    }
}
