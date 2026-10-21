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
}
