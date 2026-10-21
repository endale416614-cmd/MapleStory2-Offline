using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

[TestFixture]
public class TriageTests {
    [Test]
    public void Classify_KnownLines() {
        var testCases = new[] {
            ("Packet handler failed for 0x0020 SkillHandler", "[T1]"),
            ("Unhandled client packet 0x005A RequestSkipTutorial: no handler", "[T2]"),
            ("Trigger \"main\" not found in 52000196_qd.", "[T3]"),
            ("Could not find file 'C:\\Games\\Navmeshes\\52000196_qd.navmesh'.", "[T4]"),
            ("SQLite Error 5: 'database is locked'.", "[T5]"),
            ("This save was written by a newer version of the game (save format 9, this version understands 8).", "[T6]"),
            ("Only one usage of each socket address (protocol/network address/port) is normally permitted.", "[T7]"),
        };

        foreach ((string line, string expectedSection) in testCases) {
            var signature = Triage.Classify(line);
            Assert.That(signature, Is.Not.Null, $"Expected to find a known signature for line: {line}");
            Assert.That(signature!.Section, Is.EqualTo(expectedSection), $"Expected section {expectedSection} for line: {line}");
        }
    }

    [Test]
    public void Classify_UnknownLine_IsNull() {
        var signature = Triage.Classify("Player entered the world");
        Assert.That(signature, Is.Null);
    }

    [Test]
    public void Describe_CountsPerCategory() {
        var lines = new[] {
            "Trigger \"main\" not found in 52000196_qd.",
            "Could not find file 'C:\\Games\\Navmeshes\\52000196_qd.navmesh'.",
            "SQLite Error 5: 'database is locked'.",
            "Trigger \"test\" not found in 52000196_qd."
        };

        string result = Triage.Describe(lines);
        Assert.That(result, Contains.Substring("[T3] Map script missing: 2 line(s)"));
        Assert.That(result, Contains.Substring("[T5] Save file in use by another program: 1 line(s)"));
        Assert.That(result, Contains.Substring("Known problems (see TROUBLESHOOTING.txt):"));
    }

    [Test]
    public void EverySection_IsInTroubleshootingTxt() {
        // Find TROUBLESHOOTING.txt by walking up from the test directory
        string testDir = TestContext.CurrentContext.TestDirectory;
        string troubleshootingPath = null;
        for (string dir = testDir; dir != null && dir.Length > 1; dir = Path.GetDirectoryName(dir)) {
            string path = Path.Combine(dir, "TROUBLESHOOTING.txt");
            if (File.Exists(path)) {
                troubleshootingPath = path;
                break;
            }
        }

        Assert.That(troubleshootingPath, Is.Not.Null, "Could not find TROUBLESHOOTING.txt");

        string content = File.ReadAllText(troubleshootingPath);
        foreach (var signature in Triage.Known) {
            Assert.That(content, Contains.Substring(signature.Section + " " + signature.Category),
                $"TROUBLESHOOTING.txt does not contain section {signature.Section} with category {signature.Category}");
        }
    }

    [Test]
    public void Classify_KnownLines_Extra() {
        // Additional test cases for more coverage
        var testCases = new[] {
            ("Packet handler failed for 0x0012 ItemHandler", "[T1]"),
            ("Unhandled client packet 0x004B RequestServerList: no handler", "[T2]"),
            ("Trigger \"quest_start\" not found in 52000196_qd.", "[T3]"),
            ("Could not find file 'C:\\Games\\Navmeshes\\52000197_qd.navmesh'.", "[T4]"),
            ("database is locked", "[T5]"),
            ("written by a newer version of the game (save format 10, this version understands 8).", "[T6]"),
            ("address already in use", "[T7]"),
        };

        foreach ((string line, string expectedSection) in testCases) {
            var signature = Triage.Classify(line);
            Assert.That(signature, Is.Not.Null, $"Expected to find a known signature for line: {line}");
            Assert.That(signature!.Section, Is.EqualTo(expectedSection), $"Expected section {expectedSection} for line: {line}");
        }
    }

    [Test]
    public void Describe_MultipleCategories() {
        var lines = new[] {
            "Trigger \"main\" not found in 52000196_qd.",
            "Could not find file 'C:\\Games\\Navmeshes\\52000196_qd.navmesh'.",
            "SQLite Error 5: 'database is locked'.",
            "Packet handler failed for 0x0020 SkillHandler",
            "Trigger \"test\" not found in 52000196_qd.",
            "Only one usage of each socket address (protocol/network address/port) is normally permitted."
        };

        string result = Triage.Describe(lines);
        Assert.That(result, Contains.Substring("[T1] Server error in a game action: 1 line(s)"));
        Assert.That(result, Contains.Substring("[T3] Map script missing: 2 line(s)"));
        Assert.That(result, Contains.Substring("[T4] Map without server navigation data: 1 line(s)"));
        Assert.That(result, Contains.Substring("[T5] Save file in use by another program: 1 line(s)"));
        Assert.That(result, Contains.Substring("[T7] A server port is taken by another program: 1 line(s)"));
    }

    [Test]
    public void Describe_EmptyLines() {
        string result = Triage.Describe(new string[] { });
        Assert.That(result, Contains.Substring("No known problem found."));
    }

    [Test]
    public void Describe_NoKnownProblems() {
        var lines = new[] {
            "Player entered the world",
            "Character loaded successfully",
            "Game session started"
        };

        string result = Triage.Describe(lines);
        Assert.That(result, Contains.Substring("No known problem found."));
    }

    [Test]
    public void Classify_CaseInsensitive() {
        // Test that patterns are case insensitive
        var signature = Triage.Classify("PACKET HANDLER FAILED FOR 0x0020 SKILLHANDLER");
        Assert.That(signature, Is.Not.Null);
        Assert.That(signature!.Section, Is.EqualTo("[T1]"));
    }

    [Test]
    public void Classify_ExactMatch() {
        // Test exact matching
        var signature = Triage.Classify("Trigger \"main\" not found in 52000196_qd.");
        Assert.That(signature, Is.Not.Null);
        Assert.That(signature!.Section, Is.EqualTo("[T3]"));
    }
}