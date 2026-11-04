using System;
using System.IO;
using NUnit.Framework;
using Status = EasyReloadGuard.Editor.ReloadGuardSkillInstaller.SkillStatus;

namespace EasyReloadGuard.Editor.Tests
{
    public class SkillInstallerTests
    {
        string m_Root;
        string m_Source;
        string m_Target;

        [SetUp]
        public void SetUp()
        {
            m_Root = Path.Combine(Path.GetTempPath(), "ErgSkill_" + Guid.NewGuid().ToString("N"));
            m_Source = Path.Combine(m_Root, "source");
            m_Target = Path.Combine(m_Root, "skills", ReloadGuardSkillInstaller.SkillName);
            Directory.CreateDirectory(Path.Combine(m_Source, "references"));
            File.WriteAllText(Path.Combine(m_Source, "SKILL.md"), "skill");
            File.WriteAllText(Path.Combine(m_Source, "references", "a.md"), "a");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true);
        }

        [Test]
        public void Install_CopiesTreeAndTracksContentHash()
        {
            var hash = ReloadGuardSkillInstaller.ComputeHash(m_Source);
            Assert.AreEqual(Status.NotInstalled, ReloadGuardSkillInstaller.GetStatus(m_Target, hash));

            ReloadGuardSkillInstaller.Install(m_Source, m_Target, "1.0.0");
            Assert.AreEqual("a", File.ReadAllText(Path.Combine(m_Target, "references", "a.md")));
            Assert.AreEqual(Status.UpToDate, ReloadGuardSkillInstaller.GetStatus(m_Target, hash));
            Assert.AreEqual(hash, ReloadGuardSkillInstaller.ComputeHash(m_Target), "标记文件不计入内容哈希");

            // 包版本不变、内容变化（用 tag 或提交号引用时常见）：仍判为有更新
            File.WriteAllText(Path.Combine(m_Source, "SKILL.md"), "skill v2");
            File.Delete(Path.Combine(m_Source, "references", "a.md"));
            var newHash = ReloadGuardSkillInstaller.ComputeHash(m_Source);
            Assert.AreNotEqual(hash, newHash);
            Assert.AreEqual(Status.Outdated, ReloadGuardSkillInstaller.GetStatus(m_Target, newHash));

            File.WriteAllText(Path.Combine(m_Target, "stale.md"), "old");
            ReloadGuardSkillInstaller.Install(m_Source, m_Target, "1.0.0");
            Assert.IsFalse(File.Exists(Path.Combine(m_Target, "stale.md")), "更新时完整替换，不留旧文件");
            Assert.IsFalse(File.Exists(Path.Combine(m_Target, "references", "a.md")));
            Assert.AreEqual(Status.UpToDate, ReloadGuardSkillInstaller.GetStatus(m_Target, newHash));
        }

        [Test]
        public void Install_RefusesDirectoryNotInstalledByPackage()
        {
            Directory.CreateDirectory(m_Target);
            File.WriteAllText(Path.Combine(m_Target, "SKILL.md"), "user's own skill");

            Assert.AreEqual(Status.NotManaged, ReloadGuardSkillInstaller.GetStatus(m_Target, ReloadGuardSkillInstaller.ComputeHash(m_Source)));
            Assert.Throws<InvalidOperationException>(() => ReloadGuardSkillInstaller.Install(m_Source, m_Target, "1.0.0"));
            Assert.AreEqual("user's own skill", File.ReadAllText(Path.Combine(m_Target, "SKILL.md")));
        }

        [Test]
        public void PackageShipsSkillSource()
        {
            var source = ReloadGuardSkillInstaller.SourceDirectory;
            Assert.IsNotNull(source, "找不到本包的 PackageInfo");
            var skill = Path.Combine(source, "SKILL.md");
            Assert.IsTrue(File.Exists(skill), skill);
            StringAssert.Contains("name: " + ReloadGuardSkillInstaller.SkillName, File.ReadAllText(skill));
        }
    }
}
