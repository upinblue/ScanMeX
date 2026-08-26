using System;
using Xunit;

namespace NAPS2.Sap.Tests;

/// <summary>
/// Reading the timeout settings, including out of a connection written before they existed.
/// </summary>
public class SapConnectionTimeoutSettingsTests
{
    /// <summary>
    /// The migration. Neither element is in a connection stored by an earlier version, so both
    /// deserialize to zero -- and taking that at face value would mean every existing installation gives
    /// up on SAP the instant it asks it anything.
    /// </summary>
    [Fact]
    public void AConnectionWrittenBeforeTheseSettingsExistedGetsTheDefaults()
    {
        var connection = new SapConnectionConfig { Host = "https://sap.example.com" };

        Assert.Equal(SapConnectionConfig.DefaultConnectTimeoutSeconds,
            (int) connection.GetConnectTimeout().TotalSeconds);
        Assert.Equal(SapConnectionConfig.DefaultUploadTimeoutSeconds,
            (int) connection.GetUploadTimeout().TotalSeconds);
    }

    /// <summary>
    /// The two are separate on purpose: a gateway that won't hand out a token in half a minute is
    /// unreachable, while an upload of a large scan can legitimately run for minutes. One value cannot
    /// be right for both, which is what the single 60 second window was.
    /// </summary>
    [Fact]
    public void TheUploadDeadlineIsTheLongerOneByDefault()
    {
        var connection = new SapConnectionConfig();

        Assert.True(connection.GetUploadTimeout() > connection.GetConnectTimeout());
    }

    [Fact]
    public void AConfiguredValueIsUsed()
    {
        var connection = new SapConnectionConfig { ConnectTimeoutSeconds = 45, UploadTimeoutSeconds = 600 };

        Assert.Equal(TimeSpan.FromSeconds(45), connection.GetConnectTimeout());
        Assert.Equal(TimeSpan.FromSeconds(600), connection.GetUploadTimeout());
    }

    /// <summary>
    /// For hand-edited config files. A one-second upload deadline would fail every scan and read as SAP
    /// being down.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-30)]
    public void AnUnusablySmallValueIsBroughtUpToSomethingWorkable(int seconds)
    {
        var connection = new SapConnectionConfig { UploadTimeoutSeconds = seconds };

        Assert.True(connection.GetUploadTimeout() >= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AnAbsurdlyLargeValueIsCappedAtAnHour()
    {
        var connection = new SapConnectionConfig { UploadTimeoutSeconds = 999999 };

        Assert.Equal(TimeSpan.FromHours(1), connection.GetUploadTimeout());
    }

    /// <summary>
    /// Both dialogs build a fresh connection object on every save, so a changed timeout has to count as a
    /// change or it would look like there was nothing to store.
    /// </summary>
    [Fact]
    public void TwoConnectionsDifferingOnlyInTheirTimeoutsAreNotEqual()
    {
        var a = new SapConnectionConfig { Host = "https://sap.example.com", UploadTimeoutSeconds = 120 };
        var b = new SapConnectionConfig { Host = "https://sap.example.com", UploadTimeoutSeconds = 600 };

        Assert.NotEqual(a, b);
    }
}
