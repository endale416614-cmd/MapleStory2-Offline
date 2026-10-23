using NUnit.Framework;

namespace MapleStory2Offline.Tests;

[TestFixture]
public class SendReportsTests {
    [TestCase(null, "ask")]
    [TestCase("", "ask")]
    [TestCase(" Always ", "always")]
    [TestCase("yes", "always")]
    [TestCase("NEVER", "never")]
    [TestCase("off", "never")]
    [TestCase("maybe", "ask")]
    public void Parse_AnythingUnclearMeansAsk(string? value, string expected) {
        Assert.That(Settings.ParseSendReports(value), Is.EqualTo(expected));
    }

    [TestCase("ask", true, "always")]
    [TestCase("never", true, "always")]
    [TestCase("always", true, "always")]
    [TestCase("always", false, "ask")]
    [TestCase("ask", false, "ask")]
    [TestCase("never", false, "never")]
    public void AfterTheCheckbox_UntickingNeverLosesAnOptOut(string current, bool ticked, string expected) {
        Assert.That(Settings.SendReportsAfterCheckbox(current, ticked), Is.EqualTo(expected));
    }

    [TestCase(null, 0, "ask")]
    [TestCase(null, 1, "always")]
    [TestCase("", 1, "always")]
    [TestCase("  ", 0, "ask")]
    [TestCase("never", 1, "never")]
    [TestCase("never", 0, "never")]
    [TestCase("ask", 1, "ask")]
    [TestCase("always", 0, "always")]
    public void DefaultSendReports(string? written, int reportNotice, string expected) {
        Assert.That(Settings.DefaultSendReports(written, reportNotice), Is.EqualTo(expected));
    }

    [TestCase(null, 0, true)]
    [TestCase("", 0, true)]
    [TestCase(null, 1, false)]
    [TestCase("never", 0, false)]
    [TestCase("ask", 0, false)]
    [TestCase("always", 0, false)]
    public void NeedsReportNotice(string? written, int reportNotice, bool expected) {
        Assert.That(Settings.NeedsReportNotice(written, reportNotice), Is.EqualTo(expected));
    }
}
