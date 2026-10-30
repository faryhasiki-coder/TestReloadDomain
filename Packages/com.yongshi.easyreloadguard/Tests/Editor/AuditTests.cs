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
        public void ScanType_ReportsAsyncWorkThatOutlivesSession()
        {
            var report = Scan(new ResetPlan(), typeof(AuditFixture));

            Assert.AreEqual(RuleIds.AsyncVoid, RuleOf(report, nameof(AuditFixture.FireAndForget)));
            Assert.IsNull(RuleOf(report, nameof(AuditFixture.ReturnsTask)), "返回 Task 的 async 方法可由调用方等待与取消，不报告");
            Assert.AreEqual(RuleIds.AsyncResource, RuleOf(report, nameof(AuditFixture.Cancellation)));
            Assert.AreEqual(RuleIds.AsyncResource, RuleOf(report, nameof(AuditFixture.Pending)));
            Assert.AreEqual(RuleIds.AsyncResource, RuleOf(report, nameof(AuditFixture.Ticker)), "readonly 计时器同样跨会话运行");
            Assert.IsTrue(report.Findings.Where(f => f.RuleId == RuleIds.AsyncVoid || f.RuleId == RuleIds.AsyncResource)
                .All(f => f.Severity == Severity.Warning && f.Baselinable));
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

        [Test]
        public void Baseline_RemovedKeyRestoresReusedPlanFinding()
        {
            // 同域内多次检查复用同一计划及其 Finding（ReloadAudit.Run 的 AddRange），撤销基线后必须恢复为未基线
            var plan = new ResetPlan();
            var issue = new Finding(RuleIds.AdapterKnownIssue, Severity.Warning, "Vendor|Vendor.Cache.s_Instance", "known issue", baselinable: true);
            var broken = new Finding(RuleIds.AdapterBroken, Severity.Error, "Vendor.Adapter", "broken");
            plan.Findings.Add(issue);
            plan.Findings.Add(broken);

            var first = ReportOf(plan);
            Baseline.Apply(first, new HashSet<string> { issue.Key, broken.Key });
            Assert.IsTrue(issue.Baselined);
            Assert.AreEqual(0, first.Count(Severity.Warning));
            Assert.IsFalse(broken.Baselined, "适配器失效不可入基线");

            var second = ReportOf(plan);
            Baseline.Apply(second, new HashSet<string> { broken.Key });
            Assert.IsFalse(issue.Baselined, "基线条目移除后应恢复为未基线");
            Assert.AreEqual(1, second.Count(Severity.Warning));
            Assert.AreEqual(0, second.BaselinedCount);
            Assert.IsTrue(broken.IsBlocking);
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

        [Test]
        public void ScanType_AdviceFollowsInitializerWritersAndMutators()
        {
            var report = Scan(new ResetPlan(), typeof(ShapeFixture));

            var catalog = FindingOf(report, "s_Catalog");
            Assert.AreEqual(RuleIds.ReadonlyState, catalog.RuleId);
            Assert.AreEqual(Severity.Info, catalog.Severity, "只在类型初始化时填充的私有目录不会跨会话变化");
            StringAssert.Contains("不要用 ResetMode.Clear", catalog.Message);

            var session = FindingOf(report, "s_Session");
            Assert.AreEqual(Severity.Warning, session.Severity);
            StringAssert.Contains("Register", session.Message);
            StringAssert.Contains("ResetMode.Clear", session.Message);
            StringAssert.Contains("内容修改：Register", session.Context);
            StringAssert.Contains("范围：私有字段，结论完整", session.Context);

            var mixed = FindingOf(report, "s_Mixed");
            Assert.AreEqual(Severity.Warning, mixed.Severity);
            StringAssert.Contains("既在类型初始化时填充", mixed.Message);
            StringAssert.Contains("初始内容：类型初始化中填充", mixed.Context);

            Assert.AreEqual(Severity.Warning, FindingOf(report, "s_Slots").Severity, "数组元素在类型初始化外被改写");

            var constant = FindingOf(report, "s_Constant");
            Assert.AreEqual(RuleIds.MutableStatic, constant.RuleId);
            Assert.AreEqual(Severity.Info, constant.Severity);
            StringAssert.Contains("const 或 static readonly", constant.Message);
            Assert.AreEqual(Severity.Info, FindingOf(report, "s_InitOnly").Severity);

            var lazy = FindingOf(report, "s_Lazy");
            Assert.AreEqual(Severity.Warning, lazy.Severity);
            StringAssert.Contains("在 Get 中赋值、没有初值", lazy.Message);

            var seeded = FindingOf(report, "s_Seeded");
            StringAssert.Contains("有初值，并在 Rebuild 中重新赋值", seeded.Message);
            Assert.AreSame(typeof(ShapeFixture).GetField("s_Seeded", k_Static), seeded.Member);
        }

        [Test]
        public void ScanType_NamesWritersInsideAsyncAndLambda()
        {
            var finding = FindingOf(Scan(new ResetPlan(), typeof(WriterNameFixture)), "s_Count");

            StringAssert.Contains("BumpAsync（async/迭代器）", finding.Context);
            StringAssert.Contains("MakeReset（lambda）", finding.Context);
        }

        [Test]
        public void ScanType_AttributesAccessorWritesToCallers()
        {
            var report = Scan(new ResetPlan(), typeof(AccessorFixture));

            var instance = FindingOf(report, "<Instance>k__BackingField");
            StringAssert.Contains("赋值：Awake", instance.Context);
            StringAssert.Contains("范围：私有字段，结论完整", instance.Context, "private set 只能在类型内调用");

            var changed = FindingOf(report, nameof(AccessorFixture.Changed));
            Assert.AreEqual(RuleIds.StaticDelegate, changed.RuleId);
            StringAssert.Contains("赋值：Bind", changed.Context);
            StringAssert.Contains("范围：非私有字段", changed.Context, "公开的 add/remove 可在类型外订阅");
            StringAssert.DoesNotContain("内容修改", changed.Context, "Invoke 不修改委托");
        }

        [Test]
        public void ScanType_PublicFieldsKeepWarningsBecauseOutsideWritesAreNotAnalyzed()
        {
            var report = Scan(new ResetPlan(), typeof(AuditFixture));

            Assert.AreEqual(Severity.Warning, FindingOf(report, nameof(AuditFixture.Counter)).Severity);
            Assert.AreEqual(Severity.Warning, FindingOf(report, nameof(AuditFixture.Items)).Severity);
            StringAssert.Contains("范围：非私有字段", FindingOf(report, nameof(AuditFixture.Counter)).Context);
        }

        [Test]
        public void ReviewDeclarations_FlagsResetsThatBreakTheNextSession()
        {
            var plan = new ResetPlan();
            plan.AddField(ShapeField("s_InitOnly"), ResetMode.Default, null);
            plan.AddField(ShapeField("s_Catalog"), ResetMode.Clear, null);
            plan.AddField(ShapeField("s_Mixed"), ResetMode.Clear, null);
            plan.AddField(ShapeField("s_Session"), ResetMode.Clear, null);
            plan.AddField(ShapeField("s_Lazy"), ResetMode.Default, null);
            plan.AddField(ShapeField("s_Seeded"), ResetMode.Default, null);
            Assert.IsEmpty(plan.Findings, "声明本身合法");

            var report = new AuditReport(plan);
            ReloadAudit.ReviewDeclarations(plan, report);

            var risky = report.Findings.Where(f => f.RuleId == RuleIds.DeclarationRisk).Select(f => f.Subject.Split('.').Last()).ToList();
            CollectionAssert.AreEquivalent(new[] { "s_InitOnly", "s_Catalog", "s_Mixed" }, risky);
            Assert.IsTrue(report.Findings.All(f => f.Severity == Severity.Warning && f.Baselinable));
        }

        [Test]
        public void ScanType_ResetMethodOwnerMustCoverEveryMutableField()
        {
            var report = Scan(new ResetPlan(), typeof(CoverageFixture));

            var uncovered = report.Findings.Where(f => f.RuleId == RuleIds.OwnerCoverage).Select(f => f.Subject.Split('.').Last()).ToList();
            CollectionAssert.AreEqual(new[] { "s_Forgotten" }, uncovered, "经类型内方法重置、Clear 清理与 ReloadSafe 的字段都不应报告");
            Assert.IsFalse(report.Findings.Any(f => f.RuleId == RuleIds.MutableStatic), "owner 管理的类型不再按普通字段报告");
        }

        [Test]
        public void ToMarkdown_GroupsUnbaselinedFindingsByOriginAndType()
        {
            var report = Scan(new ResetPlan(), typeof(AuditFixture), typeof(ShapeFixture));
            report.AssemblyOrigins["EasyReloadGuard.Editor.Tests"] = AuditScope.Origin(null, false);

            var markdown = report.ToMarkdown();

            StringAssert.Contains("## 工作清单（2 个类型", markdown);
            StringAssert.Contains("| Assets | EasyReloadGuard.Editor.Tests | EasyReloadGuard.Editor.Tests.AuditFixture |", markdown);
            StringAssert.Contains("  - 上下文：", markdown);
            Assert.AreEqual(("Asm", "Ns.Type"), AuditReport.SplitSubject("Asm|Ns.Type.s_Field"));
            Assert.AreEqual("DLL（包 com.example）", AuditScope.Origin("com.example", true));
        }

        const System.Reflection.BindingFlags k_Static =
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;

        static System.Reflection.FieldInfo ShapeField(string name) => typeof(ShapeFixture).GetField(name, k_Static);

        static AuditReport Scan(ResetPlan plan, params System.Type[] types)
        {
            var report = new AuditReport(plan);
            foreach (var type in types) ReloadAudit.ScanType(type, plan, report);
            return report;
        }

        static AuditReport ReportOf(ResetPlan plan)
        {
            var report = new AuditReport(plan);
            report.Findings.AddRange(plan.Findings);
            return report;
        }

        static Finding FindingOf(AuditReport report, string member) =>
            report.Findings.SingleOrDefault(f => f.Subject.EndsWith("." + member, System.StringComparison.Ordinal));

        static string RuleOf(AuditReport report, string member) => FindingOf(report, member)?.RuleId;
    }
}
