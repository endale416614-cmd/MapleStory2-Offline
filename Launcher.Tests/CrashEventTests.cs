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
}
