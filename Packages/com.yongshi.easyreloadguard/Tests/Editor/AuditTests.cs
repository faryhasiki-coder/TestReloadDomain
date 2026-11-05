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
            Assert.AreEqual(Severity.Violation, FindingOf(report, nameof(AuditFixture.Table)).Severity);
            Assert.AreEqual(RuleIds.MissingReason, RuleOf(report, nameof(AuditFixture.MissingReason)));
            Assert.IsNull(RuleOf(report, nameof(AuditFixture.Constant)));
            Assert.IsNull(RuleOf(report, nameof(AuditFixture.Literal)));
            Assert.AreEqual(RuleIds.SafeDrift, RuleOf(report, nameof(AuditFixture.Intentional)));
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
                .All(f => f.Severity == Severity.Violation && !f.Baselinable));
        }

        [Test]
        public void ScanType_HandlesResetsButDoesNotExemptSafeOrLegacyTypes()
        {
            var plan = new ResetPlan();
            plan.AddField(typeof(AuditFixture).GetField(nameof(AuditFixture.Counter)), ResetMode.Default, null);
            plan.AddMethod(typeof(OwnerFixture).GetMethod(nameof(OwnerFixture.Reset)), 0, true, null);
            var report = Scan(plan, typeof(AuditFixture), typeof(OwnerFixture), typeof(SafeFixture), typeof(LegacyFixture));

            Assert.IsNull(RuleOf(report, nameof(AuditFixture.Counter)));
            Assert.IsFalse(report.Findings.Any(f => f.Subject.Contains(nameof(OwnerFixture))));
            Assert.IsTrue(report.Findings.Any(f => f.Subject.Contains(nameof(SafeFixture)) && f.RuleId == RuleIds.SafeDrift));
            Assert.IsTrue(report.Findings.Any(f => f.Subject.Contains(nameof(LegacyFixture)) && f.RuleId == RuleIds.MutableStatic));
            Assert.IsTrue(report.Findings.Any(f => f.RuleId == RuleIds.EnterPlayModeHook && !f.Baselinable));
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
            var issue = new Finding(RuleIds.AdapterKnownIssue, Severity.Violation, "Vendor|Vendor.Cache.s_Instance", "known issue", baselinable: true);
            var broken = new Finding(RuleIds.AdapterBroken, Severity.Violation, "Vendor.Adapter", "broken");
            plan.Findings.Add(issue);
            plan.Findings.Add(broken);

            var first = ReportOf(plan);
            Baseline.Apply(first, new HashSet<string> { issue.Key, broken.Key });
            Assert.IsTrue(issue.Baselined);
            Assert.AreEqual(2, first.ViolationCount);
            Assert.AreEqual(1, first.DeferredCount);
            Assert.AreEqual(1, first.BlockingCount);
            Assert.IsFalse(first.IsConformant, "暂缓仍是不符合规范");
            Assert.IsFalse(broken.Baselined, "适配器失效不可入基线");

            var second = ReportOf(plan);
            Baseline.Apply(second, new HashSet<string> { broken.Key });
            Assert.IsFalse(issue.Baselined, "基线条目移除后应恢复为未基线");
            Assert.AreEqual(2, second.BlockingCount);
            Assert.AreEqual(0, second.DeferredCount);
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
        public void ExcludeMarker_FoundInAncestorUpToPackageOrProjectRoot()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ErgMarker_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var plugin = System.IO.Path.Combine(root, "Assets", "Temp", "Plugin");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(plugin, "Scripts", "Editor"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(plugin, AuditScope.ExcludeMarkerFileName), "本机插件\n不随项目\n");

                var package = System.IO.Path.Combine(root, "Packages", "com.example");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(package, "Runtime"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(package, "package.json"), "{}");
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, "Packages", AuditScope.ExcludeMarkerFileName), "");

                var empty = System.IO.Path.Combine(root, "Assets", "Other");
                System.IO.Directory.CreateDirectory(empty);
                System.IO.File.WriteAllText(System.IO.Path.Combine(root, AuditScope.ExcludeMarkerFileName), "");

                var markers = new AuditScope.ExcludeMarkers(root);
                Assert.IsTrue(markers.TryFind("Assets/Temp/Plugin/Scripts/Editor/Plugin.Editor.asmdef", out var marker));
                Assert.AreEqual("Assets/Temp/Plugin/：本机插件 不随项目", marker);
                Assert.IsTrue(markers.TryFind(System.IO.Path.Combine(plugin, "Plugin.dll"), out _), "绝对路径同样查找");
                Assert.IsFalse(markers.TryFind("Packages/com.example/Runtime/A.asmdef", out _), "包根目录以上的标记不生效");
                Assert.IsFalse(markers.TryFind("Assets/Other/B.asmdef", out _), "工程根目录的标记不生效");

                System.IO.File.WriteAllText(System.IO.Path.Combine(empty, AuditScope.ExcludeMarkerFileName), "  ");
                Assert.IsTrue(new AuditScope.ExcludeMarkers(root).TryFind("Assets/Other/B.asmdef", out var noReason));
                Assert.AreEqual("Assets/Other/：未说明原因", noReason);
            }
            finally
            {
                if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
            }
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

            Assert.IsNull(FindingOf(report, "s_Catalog"), "只在初始化填充、只通过白名单读取的私有目录符合规范");

            var session = FindingOf(report, "s_Session");
            Assert.AreEqual(Severity.Violation, session.Severity);
            StringAssert.Contains("Register", session.Message);
            StringAssert.Contains("ResetMode.Clear", session.Message);
            StringAssert.Contains("内容修改：Register", session.Context);
            StringAssert.Contains("范围：私有字段，结论完整", session.Context);

            var mixed = FindingOf(report, "s_Mixed");
            Assert.AreEqual(Severity.Violation, mixed.Severity);
            StringAssert.Contains("既在静态初始化时填充", mixed.Message);
            StringAssert.Contains("初始内容：静态初始化中填充", mixed.Context);

            Assert.AreEqual(Severity.Violation, FindingOf(report, "s_Slots").Severity, "数组元素在类型初始化外被改写");

            var constant = FindingOf(report, "s_Constant");
            Assert.AreEqual(RuleIds.MutableStatic, constant.RuleId);
            Assert.AreEqual(Severity.Suggestion, constant.Severity);
            StringAssert.Contains("const 或 static readonly", constant.Message);
            Assert.AreEqual(Severity.Violation, FindingOf(report, "s_InitOnly").Severity, "object 无法证明内容不变");

            var lazy = FindingOf(report, "s_Lazy");
            Assert.AreEqual(Severity.Violation, lazy.Severity);
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
        public void ScanType_PublicFieldsAreUnprovenBeforeGlobalWriteIndex()
        {
            var report = Scan(new ResetPlan(), typeof(AuditFixture));

            Assert.AreEqual(Severity.Violation, FindingOf(report, nameof(AuditFixture.Counter)).Severity);
            Assert.AreEqual(Severity.Violation, FindingOf(report, nameof(AuditFixture.Items)).Severity);
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
            CollectionAssert.AreEquivalent(new[] { "s_InitOnly", "s_Catalog", "s_Mixed", "s_Seeded" }, risky);
            Assert.IsTrue(report.Findings.All(f => f.Severity == Severity.Violation && !f.Baselinable));
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
        public void ScanType_ReloadSafeFieldsWrittenInSessionAreReportedAsDrift()
        {
            var report = Scan(new ResetPlan(), typeof(SafeDriftFixture), typeof(SafeTypeDriftFixture), typeof(CoverageFixture), typeof(AuditFixture));

            var drifted = report.Findings.Where(f => f.RuleId == RuleIds.SafeDrift).Select(f => f.Subject.Split('.').Last()).ToList();
            CollectionAssert.AreEquivalent(new[] { "s_Cache", "s_Count", "s_Value", "s_Kept", "Intentional", "MissingReason" }, drifted,
                "只读表能证明；非私有可变字段不能仅凭类型内没有写入获得证明");
            Assert.IsTrue(report.Findings.Where(f => f.RuleId == RuleIds.SafeDrift).All(f => f.Severity == Severity.Violation && !f.Baselinable));

            var count = FindingOf(report, "s_Count");
            StringAssert.Contains("测试：有意跨会话保留", count.Message);
            StringAssert.Contains("在 Bump 中赋值", count.Message);
            Assert.IsNull(count.KeyDetail, "不能再凭复核写入位置豁免 ReloadSafe");
        }

        [Test]
        public void Baseline_CannotDeferUnprovenReloadSafe()
        {
            var report = Scan(new ResetPlan(), typeof(SafeDriftFixture));
            var count = FindingOf(report, "s_Count");

            Baseline.Apply(report, new HashSet<string> { RuleIds.SafeDrift + "|" + count.Subject, RuleIds.SafeDrift + "|" + count.Subject + "|赋值=Old;修改=" });
            Assert.IsFalse(count.Baselined, "写入位置与复核时不同，基线条目失效");

            Baseline.Apply(report, new HashSet<string> { count.Key });
            Assert.IsFalse(count.Baselined);
            Assert.IsTrue(count.IsBlocking);
        }

        [Test]
        public void ToMarkdown_GroupsUnbaselinedFindingsByOriginAndType()
        {
            var report = Scan(new ResetPlan(), typeof(AuditFixture), typeof(ShapeFixture));
            report.AssemblyOrigins["EasyReloadGuard.Editor.Tests"] = AuditScope.Origin(null, false);

            var markdown = report.ToMarkdown();

            StringAssert.Contains("## 按类型汇总（2 个类型", markdown);
            StringAssert.Contains("| Assets | EasyReloadGuard.Editor.Tests | EasyReloadGuard.Editor.Tests.AuditFixture |", markdown);
            StringAssert.Contains("### " + RuleIds.Title(RuleIds.MutableStatic), markdown, "条目按问题类型分小节");
            StringAssert.Contains("  - 读写位置：", markdown);
            StringAssert.DoesNotContain("RG0", markdown, "给人看的报告不出现规则编号");
            Assert.AreEqual(("Asm", "Ns.Type"), AuditReport.SplitSubject("Asm|Ns.Type.s_Field"));
            Assert.AreEqual("DLL（包 com.example）", AuditScope.Origin("com.example", true));
        }

        [Test]
        public void ChangeProof_RejectsEscapesAliasesMutableElementsAndCustomCalls()
        {
            var report = Scan(new ResetPlan(), typeof(ProofFixture));
            Assert.IsNull(FindingOf(report, "s_Lookup"), "私有字典、不可变元素和 TryGetValue 构成只读证明");
            Assert.IsNull(FindingOf(report, "s_Array"), "私有数组只读取元素");
            foreach (var name in new[] { "s_Returned", "s_Local", "s_Linq", "s_MutableElements", "s_FromFactory", "s_Custom" })
                Assert.IsTrue(FindingOf(report, name).IsViolation, name);
            Assert.AreEqual(Severity.Suggestion, FindingOf(report, "s_Zero").Severity);
        }

        [Test]
        public void ChangeProof_DoesNotMistakeAnotherCollectionsInitializerForExclusiveOwnership()
        {
            var finding = FindingOf(Scan(new ResetPlan(), typeof(InitializerAliasFixture)), "s_Aliased");
            Assert.IsTrue(finding.IsViolation);
            StringAssert.Contains("不能证明独占", finding.Context);
        }

        [Test]
        public void ReviewDeclarations_AcceptsExplicitZeroButNotConditionalOrUnknownCollectionInitializers()
        {
            var plan = new ResetPlan();
            plan.AddField(typeof(ProofFixture).GetField("s_Zero", k_Static), ResetMode.Default, null);
            plan.AddField(typeof(ProofFixture).GetField("s_Conditional", k_Static), ResetMode.Default, null);
            plan.AddField(typeof(ProofFixture).GetField("s_FromFactory", k_Static), ResetMode.Clear, null);
            var report = new AuditReport(plan);
            ReloadAudit.ReviewDeclarations(plan, report);
            Assert.IsNull(FindingOf(report, "s_Zero"));
            Assert.IsTrue(FindingOf(report, "s_Conditional").IsBlocking);
            Assert.IsTrue(FindingOf(report, "s_FromFactory").IsBlocking);
            Assert.IsTrue(report.Findings.All(f => !f.Baselinable));
        }

        [Test]
        public void ScanType_ExitMethodMustCancelOrDisposeTheActualField()
        {
            var report = Scan(new ResetPlan(), typeof(AsyncStopFixture));
            var asyncFields = report.Findings.Where(f => f.RuleId == RuleIds.AsyncResource).ToList();
            Assert.AreEqual(1, asyncFields.Count);
            StringAssert.EndsWith(".s_Forgotten", asyncFields[0].Subject);
            Assert.IsFalse(asyncFields[0].Baselinable);
        }

        [Test]
        public void InvokeOnReset_ReceivesTheSameCoverageCheckAsResetMethod()
        {
            var plan = new ResetPlan();
            plan.AddMethod(typeof(ShapeFixture).GetMethod(nameof(ShapeFixture.Rebuild)), 0, true, "adapter");
            var report = Scan(plan, typeof(ShapeFixture));
            Assert.IsFalse(report.Findings.Any(f => f.Subject.EndsWith(".s_Seeded")));
            Assert.IsTrue(report.Findings.Any(f => f.RuleId == RuleIds.OwnerCoverage && f.Subject.EndsWith(".s_Session") && !f.Baselinable));
        }

        [Test]
        public void RequireDomainReload_CoversStaticFieldsButStillChecksAsyncResources()
        {
            var plan = new ResetPlan();
            var report = new AuditReport(plan);
            ReloadAudit.ScanType(typeof(AuditFixture), plan, report, reloaded: true);
            Assert.IsFalse(report.Findings.Any(f => f.RuleId == RuleIds.MutableStatic || f.RuleId == RuleIds.StaticDelegate));
            Assert.IsTrue(report.Findings.Any(f => f.RuleId == RuleIds.AsyncResource));
        }

        [Test]
        public void Report_DeferredViolationsNeverBecomeConformant()
        {
            var report = new AuditReport(new ResetPlan());
            var finding = new Finding(RuleIds.MutableStatic, Severity.Violation, "A|T.Value", "没有处理方式", true);
            report.Findings.Add(finding);
            Baseline.Apply(report, new HashSet<string> { finding.Key });
            Assert.AreEqual(0, report.BlockingCount);
            Assert.AreEqual(1, report.DeferredCount);
            Assert.IsFalse(report.IsConformant);
            StringAssert.Contains("不符合规范：1 项", report.ToMarkdown());
            StringAssert.Contains("暂缓（仍不符合规范）", report.ToMarkdown());
        }

        [TestCase(null, false)]
        [TestCase("pending", false)]
        [TestCase("violation", true)]
        public void Cli_UsesExplicitConformancePolicy(string policy, bool expected) =>
            Assert.AreEqual(expected, ReloadGuardCli.FailOnAnyViolation(policy));

        [TestCase("error")]
        [TestCase("warning")]
        [TestCase("unknown")]
        public void Cli_RejectsRemovedOrUnknownPolicies(string policy) =>
            Assert.Throws<System.ArgumentException>(() => ReloadGuardCli.FailOnAnyViolation(policy));

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
            report.Findings.Where(f => f.Subject.EndsWith("." + member, System.StringComparison.Ordinal))
                .OrderBy(f => f.RuleId == RuleIds.MissingReason ? 0 : f.RuleId == RuleIds.AsyncResource ? 1 : 2).FirstOrDefault();

        static string RuleOf(AuditReport report, string member) => FindingOf(report, member)?.RuleId;
    }
}
