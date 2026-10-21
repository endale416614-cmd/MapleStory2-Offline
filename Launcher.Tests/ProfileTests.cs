using NUnit.Framework;
using System;

namespace MapleStory2Offline.Tests;

[TestFixture]
public class ProfileTests
{
    [Test]
    public void SaveDirFor_MainSave_Returns_SaveData()
    {
        string result = Settings.SaveDirFor("root", "");
        Assert.That(result, Is.EqualTo("root\\SaveData"));
    }

    [Test]
    public void SaveDirFor_SecondProfile_Returns_SaveData_Second()
    {
        string result = Settings.SaveDirFor("root", "Second");
        Assert.That(result, Is.EqualTo("root\\SaveData\\Second"));
    }

    [TestCase("")]
    [TestCase("Second run")]
    [TestCase("a-b_c")]
    public void ValidateProfile_ValidNames_Returns_Null(string name)
    {
        string? result = Settings.ValidateProfile(name);
        Assert.That(result, Is.Null);
    }

    [Test]
    public void ValidateProfile_TooLongName_Returns_Message()
    {
        string? result = Settings.ValidateProfile(new string('x', 33));
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Use letters, digits, spaces, - or _"));
    }

    [Test]
    public void ValidateProfile_InvalidCharacter_Returns_Message()
    {
        string? result = Settings.ValidateProfile("a/b");
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Use letters, digits, spaces, - or _"));
    }

    [Test]
    public void ValidateProfile_DotDot_Returns_Message()
    {
        string? result = Settings.ValidateProfile("..");
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("Use letters, digits, spaces, - or _"));
    }

    [Test]
    public void ValidateProfile_Backups_Returns_Message()
    {
        string? result = Settings.ValidateProfile("backups");
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("That name is reserved"));
    }

    [Test]
    public void ValidateProfile_Backups_CaseInsensitive_Returns_Message()
    {
        string? result = Settings.ValidateProfile("BACKUPS");
        Assert.That(result, Is.Not.Null);
        Assert.That(result, Does.Contain("That name is reserved"));
    }
}