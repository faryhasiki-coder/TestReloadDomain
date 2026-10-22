using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace EasyReloadGuard.Editor.Tests
{
    public class AuditTests
    {
        [Test]
        public void ScanType_ClassifiesUnhandledStaticState()
        {
            var report = Scan(new ResetPlan(), typeof(AuditFixture));

            Assert.AreEqual(RuleIds.StaticDelegate, RuleOf(report, "Changed"));
            Assert.AreEqual(RuleIds.StaticUnityObject, RuleOf(report, nameof(AuditFixture.Instance)));
            Assert.AreEqual(RuleIds.MutableStatic, RuleOf(report, nameof(AuditFixture.Counter)));
            Assert.AreEqual(RuleIds.MutableStatic, RuleOf(report, "<AutoProperty>k__BackingField"));
            Assert.AreEqual(RuleIds.ReadonlyState, RuleOf(report, nameof(AuditFixture.Items)));
            Assert.AreEqual(Severity.Info, FindingOf(report, nameof(AuditFixture.Table)).Severity);
            Assert.AreEqual(RuleIds.MissingReason, RuleOf(report, nameof(AuditFixture.MissingReason)));
            Assert.IsNull(RuleOf(report, nameof(AuditFixture.Constant)));
            Assert.IsNull(RuleOf(report, nameof(AuditFixture.Literal)));
            Assert.IsNull(RuleOf(report, nameof(AuditFixture.Intentional)));
        }

        [Test]
        public void ScanType_SkipsHandledFieldsAndOwnerManagedTypes()
        {
            var plan = new ResetPlan();
            plan.AddField(typeof(AuditFixture).GetField(nameof(AuditFixture.Counter)), ResetMode.Default, null);
            plan.AddMethod(typeof(OwnerFixture).GetMethod(nameof(OwnerFixture.Reset)), 0, true, null);
            var report = Scan(plan, typeof(AuditFixture), typeof(OwnerFixture), typeof(SafeFixture), typeof(LegacyFixture));

            Assert.IsNull(RuleOf(report, nameof(AuditFixture.Counter)));
            Assert.IsFalse(report.Findings.Any(f => f.Subject.Contains(nameof(OwnerFixture))));
            Assert.IsFalse(report.Findings.Any(f => f.Subject.Contains(nameof(SafeFixture))));
            Assert.IsFalse(report.Findings.Any(f => f.Subject.Contains(nameof(LegacyFixture))));
            Assert.AreEqual(1, report.LegacySelfResetTypes.Count);
        }

        [Test]
        public void Baseline_SuppressesOnlyLegacyFindingsAndReportsStaleEntries()
        {
            var report = Scan(new ResetPlan(), typeof(AuditFixture));
            var delegateFinding = FindingOf(report, "Changed");
            var reasonFinding = FindingOf(report, nameof(AuditFixture.MissingReason));

            Baseline.Apply(report, new HashSet<string> { delegateFinding.Key, reasonFinding.Key, "RG003|Gone|Old.Field" });

            Assert.IsTrue(delegateFinding.Baselined);
            Assert.IsFalse(delegateFinding.IsBlocking);
            Assert.IsFalse(reasonFinding.Baselined, "声明错误不可入基线");
            Assert.IsTrue(FindingOf(report, nameof(AuditFixture.Instance)).IsBlocking);
            Assert.IsTrue(report.Findings.Any(f => f.RuleId == RuleIds.StaleBaseline && f.Subject == "RG003|Gone|Old.Field"));
            Assert.AreEqual(1, report.Findings.Count(f => f.RuleId == RuleIds.StaleBaseline), "仍存在的问题不应报为过期");
        }

        [TestCase("Packages/com.example.tool/Runtime/A.cs", "com.example.tool")]
        [TestCase("Library/PackageCache/com.unity.inputsystem@02433b2481ab/X.dll", "com.unity.inputsystem")]
        [TestCase("Assets/Plugins/Packages/Vendor/X.dll", null)]
        [TestCase("Assets/Scripts/C.cs", null)]
        public void PackageNameFromPath(string path, string expected)
        {
            Assert.AreEqual(expected, AuditScope.PackageNameFromPath(path));
        }

        [Test]
        public void ResolveType_UsesLoadedAssembliesOnly()
        {
            Assert.AreEqual(typeof(ResetFixture), ReflectionUtil.ResolveType("EasyReloadGuard.Editor.Tests.ResetFixture, EasyReloadGuard.Editor.Tests"));
            Assert.IsNull(ReflectionUtil.ResolveType("EasyReloadGuard.Editor.Tests.ResetFixture, Not.Loaded.Assembly"));
            Assert.IsNull(ReflectionUtil.ResolveType("NoAssemblyPart"));
        }

        static AuditReport Scan(ResetPlan plan, params System.Type[] types)
        {
            var report = new AuditReport(plan);
            foreach (var type in types) ReloadAudit.ScanType(type, plan, report);
            return report;
        }

        static Finding FindingOf(AuditReport report, string member) =>
            report.Findings.SingleOrDefault(f => f.Subject.EndsWith("." + member, System.StringComparison.Ordinal));

        static string RuleOf(AuditReport report, string member) => FindingOf(report, member)?.RuleId;
    }
}
