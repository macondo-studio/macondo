using FluentAssertions;
using Macondo.App.Models;
using Macondo.App.Services;
using Macondo.Semantic.Model;
using Xunit;

namespace Macondo.Tests.KotlinSpecific;

/// <summary>
/// Test suite to validate discovery, hierarchical resolution and aggregation
/// of Kotlin and Android projects (Gradle multi-module and standalone) in Macondo.
/// Prevents regressions related to orphaned 'app' submodules, empty root folders, and build script inclusion.
/// </summary>
public class KotlinProjectDiscoveryTests
{
    /// <summary>
    /// Verify that a classic Android application with root directory (containing build.gradle.kts and settings.gradle.kts)
    /// and submodule 'app/' is recognized as a SINGLE project having the name of the application,
    /// aggregating all Kotlin source files contained in 'app/' without generating duplicate or orphaned submodules named 'app'.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverAndroidApp_WithNestedAppModule_AsSingleProject()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_Android_" + Guid.NewGuid().ToString("N"));
        var appModuleDir = Path.Combine(tempDir, "app");
        var srcDir = Path.Combine(appModuleDir, "src", "main", "java", "com", "example", "demo");
        var testDir = Path.Combine(appModuleDir, "src", "test", "java", "com", "example", "demo");

        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(testDir);

        var rootSettings = Path.Combine(tempDir, "settings.gradle.kts");
        File.WriteAllText(rootSettings, """
            rootProject.name = "MyAwesomeApp"
            include(":app")
            """);

        var rootBuild = Path.Combine(tempDir, "build.gradle.kts");
        File.WriteAllText(rootBuild, """
            plugins {
                id("com.android.application") version "8.1.0" apply false
                id("org.jetbrains.kotlin.android") version "1.9.0" apply false
            }
            """);

        var appBuild = Path.Combine(appModuleDir, "build.gradle.kts");
        File.WriteAllText(appBuild, """
            plugins {
                id("com.android.application")
                id("org.jetbrains.kotlin.android")
            }
            """);

        var mainActivityPath = Path.Combine(srcDir, "MainActivity.kt");
        File.WriteAllText(mainActivityPath, """
            package com.example.demo
            class MainActivity {
                fun onCreate() {}
            }
            """);

        var unitTestPath = Path.Combine(testDir, "ExampleUnitTest.kt");
        File.WriteAllText(unitTestPath, """
            package com.example.demo
            class ExampleUnitTest {
                fun test() {}
            }
            """);

        try
        {
            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { tempDir });

            // Assert
            descriptors.Should().HaveCount(1, "l'intera applicazione Android deve essere rappresentata da un unico progetto radice");
            var proj = descriptors[0];

            proj.Name.Should().Be("MyAwesomeApp");
            proj.FolderPath.Should().Be(tempDir);
            proj.SourceFiles.Should().HaveCount(2);
            proj.SourceFiles.Should().Contain(mainActivityPath);
            proj.SourceFiles.Should().Contain(unitTestPath);

            // Build scripts (.gradle.kts, settings.gradle.kts) should not be counted in source
            proj.SourceFiles.Should().NotContain(rootBuild);
            proj.SourceFiles.Should().NotContain(rootSettings);
            proj.SourceFiles.Should().NotContain(appBuild);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Verify that by opening a workspace folder containing multiple Android applications (such as Android-Practice-main),
    /// all and only the individual application projects with their respective source files are discovered,
    /// with no empty projects at the top or duplicates named 'app'.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverMultipleAndroidApps_InWorkspaceFolder_WithoutAppDuplicates()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_MultiAndroid_" + Guid.NewGuid().ToString("N"));
        var app1Dir = Path.Combine(workspaceDir, "BroadcastApp");
        var app2Dir = Path.Combine(workspaceDir, "DataBindingApp");

        var app1Src = Path.Combine(app1Dir, "app", "src", "main", "java", "com", "broadcast");
        var app2Src = Path.Combine(app2Dir, "app", "src", "main", "java", "com", "databinding");

        Directory.CreateDirectory(app1Src);
        Directory.CreateDirectory(app2Src);

        File.WriteAllText(Path.Combine(app1Dir, "settings.gradle.kts"), "rootProject.name = \"BroadcastApp\"\ninclude(\":app\")");
        File.WriteAllText(Path.Combine(app1Dir, "build.gradle.kts"), "// root build");
        File.WriteAllText(Path.Combine(app1Dir, "app", "build.gradle.kts"), "// app build");
        var app1File = Path.Combine(app1Src, "BroadcastReceiver.kt");
        File.WriteAllText(app1File, "package com.broadcast; class BroadcastReceiver {}");

        File.WriteAllText(Path.Combine(app2Dir, "settings.gradle.kts"), "rootProject.name = \"DataBindingApp\"\ninclude(\":app\")");
        File.WriteAllText(Path.Combine(app2Dir, "build.gradle.kts"), "// root build");
        File.WriteAllText(Path.Combine(app2Dir, "app", "build.gradle.kts"), "// app build");
        var app2File = Path.Combine(app2Src, "Vehicle.kt");
        File.WriteAllText(app2File, "package com.databinding; class Vehicle {}");

        try
        {
            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { workspaceDir });

            // Assert
            descriptors.Should().HaveCount(2, "devono essere rilevate esattamente le due applicazioni radice");

            var proj1 = descriptors.FirstOrDefault(p => p.Name == "BroadcastApp");
            proj1.Should().NotBeNull();
            proj1!.SourceFiles.Should().ContainSingle().Which.Should().Be(app1File);

            var proj2 = descriptors.FirstOrDefault(p => p.Name == "DataBindingApp");
            proj2.Should().NotBeNull();
            proj2!.SourceFiles.Should().ContainSingle().Which.Should().Be(app2File);

            descriptors.Should().NotContain(p => p.Name.Equals("app", StringComparison.OrdinalIgnoreCase), "nessun progetto deve chiamarsi 'app'");
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }

    /// <summary>
    /// Verify that the Gradle script files (*.gradle, *.gradle.kts, settings.gradle, settings.gradle.kts)
    /// are strictly excluded from the analyzable sources, while generic Kotlin scripts (.kts not gradle)
    /// and standard sources (.kt) are correctly included.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldFilterOutGradleScripts_FromSourceFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_GradleScriptsFilter_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var buildGradleKts = Path.Combine(tempDir, "build.gradle.kts");
        var settingsGradleKts = Path.Combine(tempDir, "settings.gradle.kts");
        var customGradleKts = Path.Combine(tempDir, "convention.gradle.kts");
        var ktSource = Path.Combine(tempDir, "Service.kt");
        var genericScript = Path.Combine(tempDir, "automation.kts");

        File.WriteAllText(buildGradleKts, "plugins {}");
        File.WriteAllText(settingsGradleKts, "rootProject.name = 'Test'");
        File.WriteAllText(customGradleKts, "// custom convention plugin");
        File.WriteAllText(ktSource, "class Service {}");
        File.WriteAllText(genericScript, "println(\"Hello automation\")");

        try
        {
            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { buildGradleKts });

            // Assert
            descriptors.Should().HaveCount(1);
            var proj = descriptors[0];

            proj.SourceFiles.Should().Contain(ktSource);
            proj.SourceFiles.Should().Contain(genericScript);

            proj.SourceFiles.Should().NotContain(buildGradleKts);
            proj.SourceFiles.Should().NotContain(settingsGradleKts);
            proj.SourceFiles.Should().NotContain(customGradleKts);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Verify that by directly passing the path to a Kotlin source file (.kt),
    /// a single-file project descriptor is created.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverSingleKotlinFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "SingleComponent_" + Guid.NewGuid().ToString("N") + ".kt");
        File.WriteAllText(tempFile, "package com.demo; class SingleComponent {}");

        try
        {
            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { tempFile });

            // Assert
            descriptors.Should().HaveCount(1);
            var proj = descriptors[0];
            proj.Name.Should().Be(Path.GetFileNameWithoutExtension(tempFile));
            proj.SourceFiles.Should().ContainSingle().Which.Should().Be(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    /// <summary>
    /// Verify that by directly invoking the 'build.gradle.kts' file of the internal module 'app',
    /// the resolver automatically goes back to the root folder of the parent project by assigning the correct name.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_WhenGivenNestedAppBuildFile_ShouldResolveToParentProject()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_NestedApp_" + Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(tempDir, "app");
        var srcDir = Path.Combine(appDir, "src", "main", "java");

        Directory.CreateDirectory(srcDir);

        File.WriteAllText(Path.Combine(tempDir, "settings.gradle.kts"), "rootProject.name = \"ParentApp\"\ninclude(\":app\")");
        File.WriteAllText(Path.Combine(tempDir, "build.gradle.kts"), "// root build");
        var appBuildPath = Path.Combine(appDir, "build.gradle.kts");
        File.WriteAllText(appBuildPath, "// app build");

        var ktFile = Path.Combine(srcDir, "Entry.kt");
        File.WriteAllText(ktFile, "class Entry {}");

        try
        {
            // Act: directly pass the build.gradle.kts of app/
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { appBuildPath });

            // Assert
            descriptors.Should().HaveCount(1);
            var proj = descriptors[0];
            proj.Name.Should().Be("ParentApp");
            proj.SourceFiles.Should().Contain(ktFile);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Tests the generation of the Project Explorer hierarchical tree (ExplorerTreeItem.BuildTree)
    /// on a multi-app workspace, ensuring that project nodes display the Kotlin language badge ('KT')
    /// and group the classes within the respective application, without orphaned 'app' nodes.
    /// </summary>
    [Fact]
    public void ExplorerTreeItem_ShouldBuildTree_WithCorrectKotlinAppHierarchy()
    {
        var workspaceDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_TreeHierarchy_" + Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(workspaceDir, "CalculatorApp");
        var srcDir = Path.Combine(appDir, "app", "src", "main", "java");

        Directory.CreateDirectory(srcDir);

        File.WriteAllText(Path.Combine(appDir, "settings.gradle.kts"), "rootProject.name = \"CalculatorApp\"\ninclude(\":app\")");
        File.WriteAllText(Path.Combine(appDir, "build.gradle.kts"), "// root build");
        File.WriteAllText(Path.Combine(appDir, "app", "build.gradle.kts"), "// app build");

        var activityFile = Path.Combine(srcDir, "CalculatorActivity.kt");
        File.WriteAllText(activityFile, """
            package com.calc
            class CalculatorActivity {
                fun calculate(): Int = 42
            }
            """);

        try
        {
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { workspaceDir });
            descriptors.Should().HaveCount(1);

            // Analyze the project by extracting semantic symbols in the CodeGraph
            var visualGraph = ProjectAnalysisService.AnalyzeProjects(descriptors);

            // Builds the Project Explorer tree
            var tree = ExplorerTreeItem.BuildTree(visualGraph, workspaceDir);

            // Assert
            tree.Should().NotBeNull();
            tree.Name.Should().Be("CalculatorApp");
            tree.LanguageBadgeText.Should().Be("KT", "il progetto Kotlin deve esporre il badge 'KT'");

            // The 'Class Modules' group must exist with 'CalculatorActivity' inside
            var classGroup = tree.Children.FirstOrDefault(c => c.IsGroup);
            classGroup.Should().NotBeNull("le classi Kotlin devono essere raggruppate sotto il relativo gruppo");
            classGroup!.Children.Should().ContainSingle(item => item.Name == "CalculatorActivity");
        }
        finally
        {
            if (Directory.Exists(workspaceDir))
                Directory.Delete(workspaceDir, recursive: true);
        }
    }
}
