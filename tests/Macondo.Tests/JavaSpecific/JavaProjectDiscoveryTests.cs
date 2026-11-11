using FluentAssertions;
using Macondo.App.Models;
using Xunit;

namespace Macondo.Tests.JavaSpecific;

public class JavaProjectDiscoveryTests
{
    /// <summary>
    /// Verify that ProjectDescriptor.DiscoverFromPaths recognizes a Maven 'pom.xml' file,
    /// extract the project name from <artifactId> and locate the Java source files contained in the folder.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverMavenProjectFromPomXml()
    {
        // Arrange: Create a temporary directory with pom.xml and Java sources
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_Maven_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDir, "src", "main", "java", "com", "acme"));
        Directory.CreateDirectory(Path.Combine(tempDir, "target", "classes")); // cartella di build da ignorare

        var pomPath = Path.Combine(tempDir, "pom.xml");
        File.WriteAllText(pomPath, """
            <project xmlns="http://maven.apache.org/POM/4.0.0">
                <modelVersion>4.0.0</modelVersion>
                <groupId>com.acme</groupId>
                <artifactId>order-management-service</artifactId>
                <version>1.0.0</version>
            </project>
            """);

        var javaSrcPath = Path.Combine(tempDir, "src", "main", "java", "com", "acme", "OrderService.java");
        File.WriteAllText(javaSrcPath, "package com.acme; public class OrderService {}");

        var targetClassPath = Path.Combine(tempDir, "target", "classes", "IgnoredClass.java");
        File.WriteAllText(targetClassPath, "package com.acme; public class IgnoredClass {}");

        try
        {
            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { pomPath });

            // Assert
            descriptors.Should().HaveCount(1);
            var proj = descriptors[0];
            proj.Name.Should().Be("order-management-service");
            proj.PomXmlPath.Should().Be(pomPath);
            proj.SourceFiles.Should().Contain(javaSrcPath);
            proj.SourceFiles.Should().NotContain(targetClassPath); // target/ deve essere ignorata
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Verify that ProjectDescriptor.DiscoverFromPaths recognizes a Gradle script 'build.gradle' or 'build.gradle.kts'.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverGradleProject()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "Macondo_Test_Gradle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(tempDir, "src", "main", "java"));
        Directory.CreateDirectory(Path.Combine(tempDir, ".gradle")); // da ignorare

        var gradlePath = Path.Combine(tempDir, "build.gradle");
        File.WriteAllText(gradlePath, "plugins { id 'java' }");

        var javaSrcPath = Path.Combine(tempDir, "src", "main", "java", "App.java");
        File.WriteAllText(javaSrcPath, "public class App {}");

        try
        {
            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { gradlePath });

            // Assert
            descriptors.Should().HaveCount(1);
            var proj = descriptors[0];
            proj.GradlePath.Should().Be(gradlePath);
            proj.SourceFiles.Should().Contain(javaSrcPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Tests opening a single isolated .java file.
    /// </summary>
    [Fact]
    public void ProjectDescriptor_ShouldDiscoverSingleJavaFile()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), "SingleService_" + Guid.NewGuid().ToString("N") + ".java");
        File.WriteAllText(tempFile, "public class SingleService {}");

        try
        {
            // Act
            var descriptors = ProjectDescriptor.DiscoverFromPaths(new[] { tempFile });

            // Assert
            descriptors.Should().HaveCount(1);
            descriptors[0].SourceFiles.Should().ContainSingle().Which.Should().Be(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
