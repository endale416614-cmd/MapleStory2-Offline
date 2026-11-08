using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

[TestFixture]
public class CrashEventTests {
    [Test]
    public void Describe_ValidProperties_ReturnsExpectedString() {
        // Arrange
        var properties = new List<string?> {
            "MapleStory2.exe",
            "1.0.0.1",
            "5f3a",
            "NxCharacter64.dll",
            "1.0.0.0",
            "5f3b",
            "c0000005",
            "00000000000a1b2c",
            "1234",
            "01d9",
            "C:\\Games\\x64\\MapleStory2.exe",
            "C:\\Games\\x64\\NxCharacter64.dll"
        };

        // Act
        string? result = CrashEvent.Describe(properties);

        // Assert
        Assert.That(result, Is.EqualTo("MapleStory2.exe 1.0.0.1: faulting module NxCharacter64.dll 1.0.0.0, exception c0000005 at offset 00000000000a1b2c"));
        Assert.That(result, Does.Not.Contain("\\"), "no path: paths hold the user name");
    }

    [Test]
    public void Describe_NonMapleStory2Executable_ReturnsNull() {
        // Arrange
        var properties = new List<string?> {
            "OtherProgram.exe",
            "1.0.0.1",
            "5f3a",
            "NxCharacter64.dll",
            "1.0.0.0",
            "5f3b",
            "c0000005",
            "00000000000a1b2c"
        };

        // Act
        string? result = CrashEvent.Describe(properties);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Describe_LessThan8Properties_ReturnsNull() {
        // Arrange
        var properties = new List<string?> {
            "MapleStory2.exe",
            "1.0.0.1"
        };

        // Act
        string? result = CrashEvent.Describe(properties);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public void Describe_CaseInsensitiveComparison() {
        // Arrange
        var properties = new List<string?> {
            "maplestory2.exe",  // lowercase
            "1.0.0.1",
            "5f3a",
            "NxCharacter64.dll",
            "1.0.0.0",
            "5f3b",
            "c0000005",
            "00000000000a1b2c"
        };

        // Act
        string? result = CrashEvent.Describe(properties);

        // Assert
        Assert.That(result, Is.EqualTo("maplestory2.exe 1.0.0.1: faulting module NxCharacter64.dll 1.0.0.0, exception c0000005 at offset 00000000000a1b2c"));
    }

    // BT-217: the client patch's crash line of this session, for the report's Game crash section.
    [Test]
    public void FromGuardLog_TakesTheNewestCrashLineOfTheSession() {
        string[] lines = [
            "2026-11-05 22:10:00 crash: exception 0xC0000005 (read of 0x0000000000000000) at MapleStory2.exe+0x10, thread 7",
            "2026-11-06 14:00:01 hook crash/SetUnhandledExceptionFilter INSTALLED",
            "2026-11-06 14:01:00 crash: exception 0xC0000005 (read of 0x0000000000000000) at MapleStory2.exe+0x20, thread 9",
            "2026-11-06 14:02:03 crash: exception 0xC0000005 (write of 0x0000000000000010) at MapleStory2.exe+0x1a2b3c, thread 1234",
            "2026-11-06 14:02:04 blocked DNS lookup example.com (getaddrinfo)",
        ];

        Assert.That(CrashEvent.FromGuardLog(lines, new DateTime(2026, 11, 6, 14, 0, 0)), Is.EqualTo(lines[3]));
        Assert.That(CrashEvent.FromGuardLog(lines, new DateTime(2026, 11, 6, 14, 2, 4)), Is.Null, "a crash before the session");
        Assert.That(CrashEvent.FromGuardLog(lines.Take(2), new DateTime(2026, 11, 6, 14, 0, 0)), Is.Null, "the hook's own line is no crash");
    }
}
