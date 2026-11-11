using FluentAssertions;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class WindowsNotificationServiceTests
{
    /// <summary>
    /// Verify that the Windows Notification Service handles null or uninitialized Window instances
    /// without raising exceptions at runtime (NullReferenceException or AccessViolationException).
    /// </summary>
    [Fact]
    public void WindowsNotificationService_ShouldHandleNullWindow_Gracefully()
    {
        // Act & Assert
        var flashAct = () => WindowsNotificationService.FlashTaskbar(null);
        flashAct.Should().NotThrow();

        var notifyAct = () => WindowsNotificationService.ShowWindowsNotification(null, "Test Title", "Test Message");
        notifyAct.Should().NotThrow();
    }

    /// <summary>
    /// Check for empty, null, or very long strings in notification messages
    /// do not cause buffer overflows or crashes in the underlying P/Invoke APIs.
    /// </summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("Short Title", "Short message")]
    [InlineData("Very Long Title ....................................................................................................", "Very long message body text ....................................................................................................................................................................................................................................................")]
    public void WindowsNotificationService_ShouldHandleVariousMessageLengths_Safely(string title, string message)
    {
        var act = () => WindowsNotificationService.ShowWindowsNotification(null, title, message);
        act.Should().NotThrow();
    }
}
