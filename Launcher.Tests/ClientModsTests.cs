using System.IO;
using NUnit.Framework;

namespace MapleStory2Offline.Tests;

[TestFixture]
public class ClientModsTests {
    [Test]
    public void SetFile_Off_DisablesFile() {
        string temp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(temp);
        try {
            string modDir = Path.Combine(temp, "offline-shop", "Data", "Xml", "table");
            Directory.CreateDirectory(modDir);
            string featurePath = Path.Combine(modDir, "feature.xml");
            File.WriteAllText(featurePath, "test content");

            // Test that SetFile(false) leaves only feature.xml.off
            ClientMods.SetFile(temp, "offline-shop", Path.Combine("Data", "Xml", "table", "feature.xml"), false);
            Assert.IsFalse(File.Exists(featurePath));
            Assert.IsTrue(File.Exists(featurePath + ".off"));

            // Test that SetFile(false) again changes nothing
            ClientMods.SetFile(temp, "offline-shop", Path.Combine("Data", "Xml", "table", "feature.xml"), false);
            Assert.IsFalse(File.Exists(featurePath));
            Assert.IsTrue(File.Exists(featurePath + ".off"));

            // Test that SetFile(true) restores feature.xml
            ClientMods.SetFile(temp, "offline-shop", Path.Combine("Data", "Xml", "table", "feature.xml"), true);
            Assert.IsTrue(File.Exists(featurePath));
            Assert.IsFalse(File.Exists(featurePath + ".off"));

            // Test with neither file nothing happens and nothing throws
            File.Delete(featurePath);
            ClientMods.SetFile(temp, "offline-shop", Path.Combine("Data", "Xml", "table", "feature.xml"), false);
        } finally {
            Directory.Delete(temp, true);
        }
    }
}