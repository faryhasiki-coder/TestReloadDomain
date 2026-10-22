using System.Linq;
using Faryhasiki.PlaySessions.Editor;
using NUnit.Framework;

namespace Faryhasiki.PlaySessions.Tests
{
    public sealed class CompatibilityTests
    {
        static PlaySessionCompatibilityAttribute Policy(DependencyHandling mode = DependencyHandling.Adapter) =>
            new PlaySessionCompatibilityAttribute(DependencyKind.Package, "com.example.vendor", "1.0.0", mode, "Test review") { ModuleId = "vendor" };
        static InstalledDependency Installed(string version = "1.0.0") =>
            new InstalledDependency { Kind = DependencyKind.Package, Name = "com.example.vendor", Version = version };
        static SessionPlan Plan() { var plan = new SessionPlan(); plan.Add("vendor", _ => { }); return plan; }

        [Test]
        public void ExactVersionAndRegisteredAdapterPass() =>
            Assert.IsEmpty(CompatibilityCheck.Evaluate(new[] { Policy() }, new[] { Installed() }, Plan(), true, false));

        [Test]
        public void PackageUpgradeRequiresNewReview() =>
            Assert.IsTrue(CompatibilityCheck.Evaluate(new[] { Policy() }, new[] { Installed("2.0.0") }, Plan(), true, false).Any(d => d.code == "PS203"));

        [Test]
        public void MissingAdapterDoesNotCountAsCoverage() =>
            Assert.IsTrue(CompatibilityCheck.Evaluate(new[] { Policy() }, new[] { Installed() }, new SessionPlan(), true, false).Any(d => d.code == "PS206"));

        [Test]
        public void SelfManagedDependencyNeedsNoAdapter() =>
            Assert.IsEmpty(CompatibilityCheck.Evaluate(new[] { Policy(DependencyHandling.SelfManaged) }, new[] { Installed() }, new SessionPlan(), true, false));

        [TestCase(DependencyHandling.FullReloadRequired)]
        [TestCase(DependencyHandling.EditorRestartRequired)]
        public void UnsafeLibrariesBlockFastPlay(DependencyHandling mode)
        {
            Assert.IsTrue(CompatibilityCheck.Evaluate(new[] { Policy(mode) }, new[] { Installed() }, Plan(), true, false).Any(d => d.code == "PS207"));
            Assert.IsEmpty(CompatibilityCheck.Evaluate(new[] { Policy(mode) }, new[] { Installed() }, Plan(), false, false));
        }

        [Test]
        public void DllWithUnchangedVersionButDifferentBytesRequiresReview()
        {
            var policy = new PlaySessionCompatibilityAttribute(DependencyKind.ManagedAssembly, "Vendor", "1.0.0.0", DependencyHandling.Adapter, "Test review") { ModuleId = "vendor", Sha256 = "old" };
            var dll = new InstalledDependency { Kind = DependencyKind.ManagedAssembly, Name = "Vendor", Version = "1.0.0.0", Sha256 = "new" };
            Assert.IsTrue(CompatibilityCheck.Evaluate(new[] { policy }, new[] { dll }, Plan(), true, false).Any(d => d.code == "PS205"));
        }

        [Test]
        public void OptionalAbsentDependencyPassesButInstalledWrongVersionFails()
        {
            var policy = Policy(); policy.Optional = true;
            Assert.IsEmpty(CompatibilityCheck.Evaluate(new[] { policy }, new InstalledDependency[0], Plan(), true, false));
            Assert.IsTrue(CompatibilityCheck.Evaluate(new[] { policy }, new[] { Installed("2.0.0") }, Plan(), true, false).Any(d => d.code == "PS203"));
        }

        [Test]
        public void UnreviewedDependencyIsVisibleAndCanBeMadeBlocking()
        {
            Assert.AreEqual("Info", CompatibilityCheck.Evaluate(new PlaySessionCompatibilityAttribute[0], new[] { Installed() }, Plan(), true, false).Single().severity);
            Assert.AreEqual("Error", CompatibilityCheck.Evaluate(new PlaySessionCompatibilityAttribute[0], new[] { Installed() }, Plan(), true, true).Single().severity);
        }

        [Test]
        public void DuplicatePolicyIsNotSilentlyAccepted() =>
            Assert.IsTrue(CompatibilityCheck.Evaluate(new[] { Policy(), Policy() }, new[] { Installed() }, Plan(), true, false).Any(d => d.code == "PS204"));
    }
}
