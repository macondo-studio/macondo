using System.Text;
using FluentAssertions;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class CliRunnerTests
{
    #region IsCliInvocation Tests

    [Fact]
    public void IsCliInvocation_WithNullOrEmptyArgs_ShouldReturnFalse()
    {
        CliRunner.IsCliInvocation(null).Should().BeFalse();
        CliRunner.IsCliInvocation(Array.Empty<string>()).Should().BeFalse();
    }

    [Fact]
    public void IsCliInvocation_WithGuiOnlyArgs_ShouldReturnFalse()
    {
        CliRunner.IsCliInvocation(new[] { "--some-unknown-gui-flag", "value" }).Should().BeFalse();
    }

    [Theory]
    [InlineData("analyze")]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    [InlineData("/?")]
    [InlineData("--report")]
    [InlineData("-r")]
    [InlineData("--out-dir")]
    [InlineData("-o")]
    [InlineData("--export-md")]
    [InlineData("-m")]
    [InlineData("--export-json")]
    [InlineData("-j")]
    [InlineData("--dead-code")]
    [InlineData("--maintainability")]
    [InlineData("--rules")]
    [InlineData("--clones")]
    [InlineData("--cohesion")]
    [InlineData("--martin")]
    [InlineData("--shared-state")]
    [InlineData("--crud")]
    [InlineData("--data-flow")]
    [InlineData("--screen-flow")]
    [InlineData("--dsm")]
    [InlineData("--impact")]
    [InlineData("--fail-on-violations")]
    [InlineData("--fail-on-rules")]
    [InlineData("--fail-on-mi")]
    [InlineData("--fail-on-dead-code")]
    public void IsCliInvocation_WithCliFlag_ShouldReturnTrue(string flag)
    {
        CliRunner.IsCliInvocation(new[] { flag }).Should().BeTrue();
        CliRunner.IsCliInvocation(new[] { "targetFolder", flag }).Should().BeTrue();
    }

    #endregion

    #region Help and Parameter Validation Tests

    [Fact]
    public void Run_WithEmptyArgs_ShouldReturnErrorAndPrintHelp()
    {
        using var outWriter = new StringWriter();
        using var errorWriter = new StringWriter();

        var exitCode = CliRunner.Run(Array.Empty<string>(), outWriter, errorWriter);

        exitCode.Should().Be(CliRunner.ExitCodeError);
        errorWriter.ToString().Should().Contain("[Error] No arguments provided.");
        outWriter.ToString().Should().Contain("Macondo — Architecture & Code Analyzer");
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    [InlineData("help")]
    [InlineData("/?")]
    public void Run_WithHelpFlag_ShouldReturnSuccessAndPrintHelp(string flag)
    {
        using var outWriter = new StringWriter();
        using var errorWriter = new StringWriter();

        var exitCode = CliRunner.Run(new[] { flag }, outWriter, errorWriter);

        exitCode.Should().Be(CliRunner.ExitCodeSuccess);
        outWriter.ToString().Should().Contain("Usage:");
        outWriter.ToString().Should().Contain("--report");
    }

    [Theory]
    [InlineData(new[] { "analyze", "--export-md" }, "Missing file path for --export-md")]
    [InlineData(new[] { "analyze", "-m" }, "Missing file path for --export-md")]
    [InlineData(new[] { "analyze", "--export-json" }, "Missing file path for --export-json")]
    [InlineData(new[] { "analyze", "-j" }, "Missing file path for --export-json")]
    [InlineData(new[] { "analyze", "--report" }, "Missing file path for --report")]
    [InlineData(new[] { "analyze", "-r" }, "Missing file path for --report")]
    [InlineData(new[] { "analyze", "--out-dir" }, "Missing directory path for --out-dir")]
    [InlineData(new[] { "analyze", "-o" }, "Missing directory path for --out-dir")]
    [InlineData(new[] { "analyze", "--analysis" }, "Missing value for --analysis")]
    [InlineData(new[] { "analyze", "-a" }, "Missing value for --analysis")]
    [InlineData(new[] { "analyze", "--format" }, "Missing value for --format")]
    [InlineData(new[] { "analyze", "-f" }, "Missing value for --format")]
    [InlineData(new[] { "analyze", "--rules-config" }, "Missing file path for --rules-config")]
    [InlineData(new[] { "analyze", "--fail-on-mi" }, "Missing or invalid numeric threshold for --fail-on-mi")]
    [InlineData(new[] { "analyze", "--fail-on-mi", "not-a-number" }, "Missing or invalid numeric threshold for --fail-on-mi")]
    [InlineData(new[] { "analyze", "--fail-on-dead-code" }, "Missing or invalid numeric threshold for --fail-on-dead-code")]
    [InlineData(new[] { "analyze", "--fail-on-dead-code", "abc" }, "Missing or invalid numeric threshold for --fail-on-dead-code")]
    [InlineData(new[] { "analyze", "--impact" }, "Missing symbol name for --impact")]
    public void Run_WithMissingOrInvalidOptionValues_ShouldReturnError(string[] args, string expectedErrorMessage)
    {
        using var outWriter = new StringWriter();
        using var errorWriter = new StringWriter();

        var exitCode = CliRunner.Run(args, outWriter, errorWriter);

        exitCode.Should().Be(CliRunner.ExitCodeError);
        errorWriter.ToString().Should().Contain(expectedErrorMessage);
    }

    [Fact]
    public void Run_WithNoInputPaths_ShouldReturnErrorAndPrintHelp()
    {
        using var outWriter = new StringWriter();
        using var errorWriter = new StringWriter();

        var exitCode = CliRunner.Run(new[] { "analyze", "--dead-code" }, outWriter, errorWriter);

        exitCode.Should().Be(CliRunner.ExitCodeError);
        errorWriter.ToString().Should().Contain("[Error] No target project, group or folder path specified for analysis.");
    }

    [Fact]
    public void Run_WithNonExistentPath_ShouldReturnError()
    {
        using var outWriter = new StringWriter();
        using var errorWriter = new StringWriter();

        var exitCode = CliRunner.Run(new[] { "analyze", @"C:\NonExistent_Fake_Path_12345" }, outWriter, errorWriter);

        exitCode.Should().Be(CliRunner.ExitCodeError);
        errorWriter.ToString().Should().Contain("[Error] No supported project files");
    }

    #endregion

    #region Real Project Analysis and Export Scenarios

    [Fact]
    public void Run_OnValidSourceFolder_WithAuditReport_ShouldSucceed()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_CliTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var basFile = Path.Combine(tempDir, "Module1.bas");
            File.WriteAllText(basFile, @"
Attribute VB_Name = ""Module1""
Public Sub Main()
    Call Helper
End Sub

Public Sub Helper()
    MsgBox ""Hello""
End Sub
");

            var reportPath = Path.Combine(tempDir, "audit-out.md");
            var mdExportPath = Path.Combine(tempDir, "repomap.md");
            var jsonExportPath = Path.Combine(tempDir, "graph.json");

            using var outWriter = new StringWriter();
            using var errorWriter = new StringWriter();

            var exitCode = CliRunner.Run(new[]
            {
                "analyze",
                tempDir,
                "--report", reportPath,
                "--export-md", mdExportPath,
                "--export-json", jsonExportPath
            }, outWriter, errorWriter);

            exitCode.Should().Be(CliRunner.ExitCodeSuccess);
            File.Exists(reportPath).Should().BeTrue();
            File.Exists(mdExportPath).Should().BeTrue();
            File.Exists(jsonExportPath).Should().BeTrue();

            var reportText = File.ReadAllText(reportPath);
            reportText.Should().Contain("Architecture & Code Quality Audit Report");

            var outStr = outWriter.ToString();
            outStr.Should().Contain("[Macondo] Execution completed successfully.");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Run_OnValidSourceFolder_WithOutDirAndGranularFlags_ShouldExportAllRequestedFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_CliTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var clsFile = Path.Combine(tempDir, "Customer.cls");
            File.WriteAllText(clsFile, @"
VERSION 1.0 CLASS
BEGIN
  MultiUse = -1  'True
END
Attribute VB_Name = ""Customer""
Private m_Name As String

Public Property Get Name() As String
    Name = m_Name
End Property

Public Sub Save()
    ' Save logic
End Sub
");

            var outDir = Path.Combine(tempDir, "all_reports");
            var deadCodeFile = Path.Combine(tempDir, "dead-code.json");
            var impactFile = Path.Combine(tempDir, "impact.md");

            using var outWriter = new StringWriter();
            using var errorWriter = new StringWriter();

            var exitCode = CliRunner.Run(new[]
            {
                "analyze",
                tempDir,
                "--out-dir", outDir,
                "--format", "all",
                "--dead-code", deadCodeFile,
                "--impact", "Customer.Save", impactFile
            }, outWriter, errorWriter);

            exitCode.Should().Be(CliRunner.ExitCodeSuccess);
            Directory.Exists(outDir).Should().BeTrue();
            File.Exists(deadCodeFile).Should().BeTrue();
            File.Exists(impactFile).Should().BeTrue();

            // Verify that both .md and .json files have been generated
            Directory.GetFiles(outDir, "*.md").Should().NotBeEmpty();
            Directory.GetFiles(outDir, "*.json").Should().NotBeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Run_WithQualityGate_FailOnMi_ShouldReturnExitCode2WhenThresholdViolated()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_CliTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var basFile = Path.Combine(tempDir, "Complex.bas");
            File.WriteAllText(basFile, @"
Attribute VB_Name = ""Complex""
Public Sub DoStuff()
    Dim x As Integer
    x = 10
End Sub
");

            using var outWriter = new StringWriter();
            using var errorWriter = new StringWriter();

            // We set an unattainable threshold of 150 (MI is typically <= 100)
            var exitCode = CliRunner.Run(new[]
            {
                "analyze",
                tempDir,
                "--fail-on-mi", "150"
            }, outWriter, errorWriter);

            exitCode.Should().Be(CliRunner.ExitCodeQualityGateFailed);
            errorWriter.ToString().Should().Contain("[Quality Gate FAILED]");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Run_WithQualityGate_Pass_ShouldReturnExitCode0()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_CliTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var basFile = Path.Combine(tempDir, "Clean.bas");
            File.WriteAllText(basFile, @"
Attribute VB_Name = ""Clean""
Public Sub DoClean()
    Dim a As Integer
    a = 1
End Sub
");

            using var outWriter = new StringWriter();
            using var errorWriter = new StringWriter();

            // Very low MI threshold (1.0) that will be exceeded
            var exitCode = CliRunner.Run(new[]
            {
                "analyze",
                tempDir,
                "--fail-on-mi", "1.0",
                "--fail-on-dead-code", "100"
            }, outWriter, errorWriter);

            exitCode.Should().Be(CliRunner.ExitCodeSuccess);
            outWriter.ToString().Should().Contain("[Quality Gate PASSED]");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion

    #region Helper and Utility Tests

    [Fact]
    public void SaveFile_ShouldCreateDirectoryAndWriteUtf8Content()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_SaveFileTest_" + Guid.NewGuid().ToString("N"), "nested", "folder");
        var filePath = Path.Combine(tempDir, "sample.txt");

        try
        {
            var testContent = "Test content with accents: àèéìòù & UTF-8 symbols: 🚀";
            CliRunner.SaveFile(filePath, testContent);

            File.Exists(filePath).Should().BeTrue();
            var readBack = File.ReadAllText(filePath, Encoding.UTF8);
            readBack.Should().Be(testContent);
        }
        finally
        {
            var rootDir = Path.GetDirectoryName(Path.GetDirectoryName(tempDir));
            if (rootDir != null && Directory.Exists(rootDir))
                Directory.Delete(rootDir, recursive: true);
        }
    }

    #endregion
}
