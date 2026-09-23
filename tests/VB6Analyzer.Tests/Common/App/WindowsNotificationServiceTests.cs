using FluentAssertions;
using Macondo.App.Services;
using Xunit;

namespace Macondo.Tests.Common.App;

public class WindowsNotificationServiceTests
{
    /// <summary>
    /// Verifica che il servizio di notifica Windows gestisca istanze Window nulle o non inizializzate
    /// senza sollevare eccezioni a runtime (NullReferenceException o AccessViolationException).
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
    /// Verifica che stringhe vuote, nulle o molto lunghe nei messaggi di notifica
    /// non causino buffer overflow o crash nelle API P/Invoke sottostanti.
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
