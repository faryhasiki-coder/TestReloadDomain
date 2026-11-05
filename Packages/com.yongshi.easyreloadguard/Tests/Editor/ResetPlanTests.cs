using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace EasyReloadGuard.Editor.Tests
{
    public class ResetPlanTests
    {
        const BindingFlags k_Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            ResetFixture.Counter = 0;
            ResetFixture.Seeded = 5;
            ResetFixture.Label = "init";
            ResetFixture.Items.Clear();
            ResetFixture.Mutable = new List<int>();
            ResetFixture.Calls.Clear();
            ResetFixture.Property = 0;
            ResetFixture.AutoProp = 0;
            ResetFixture.ManualProp = 0;
            ResetFixture.ClearChanged();
            ResetFixture.ClearAutoEvent();
            GenericHolder<string>.Value = null;
        }

        [Test]
        public void Default_WritesDefaultValue()
        {
            var plan = Plan(p => p.AddField(Field(nameof(ResetFixture.Counter)), ResetMode.Default, null));
            RunEnter(plan);
            ResetFixture.Counter = 3;
            RunEnter(plan);

            Assert.AreEqual(0, ResetFixture.Counter);
            Assert.IsEmpty(plan.Findings);
        }

        [Test]
        public void Default_WarnsWhenInitialValueIsNotDefault()
        {
            var plan = Plan(p => p.AddField(Field(nameof(ResetFixture.Seeded)), ResetMode.Default, null));
            LogAssert.Expect(LogType.Warning, new Regex("ResetMode.Default"));
            RunEnter(plan);

            Assert.AreEqual(0, ResetFixture.Seeded);
        }

        [Test]
        public void InitialValue_RestoresFirstObservedValue()
        {
            var plan = Plan(p => p.AddField(Field(nameof(ResetFixture.Label)), ResetMode.InitialValue, null));
            RunEnter(plan);
            ResetFixture.Label = "changed";
            RunEnter(plan);

            Assert.AreEqual("init", ResetFixture.Label);
        }

        [TestCase(ResetMode.Default)]
        [TestCase(ResetMode.Clear)]
        public void DefaultAndClear_DoNotRetainObservedReference(ResetMode mode)
        {
            var plan = Plan(p => p.AddField(Field(nameof(ResetFixture.Mutable)), mode, null));
            RunEnter(plan);
            ResetFixture.Mutable = new List<int> { 1 };
            RunEnter(plan);

            Assert.IsNull(k_Initial.GetValue(plan.FieldSteps.Single()), "只有 InitialValue 需要长期持有初值");
        }

        [Test]
        public void Clear_EmptiesReadonlyCollectionAndKeepsReference()
        {
            var items = ResetFixture.Items;
            var plan = Plan(p => p.AddField(Field(nameof(ResetFixture.Items)), ResetMode.Clear, null));
            items.Add(1);
            RunEnter(plan);

            Assert.AreSame(items, ResetFixture.Items);
            Assert.IsEmpty(ResetFixture.Items);
        }

        [Test]
        public void EventAndAutoPropertyBackingFields_AreReset()
        {
            var plan = Plan(p =>
            {
                p.AddField(Field("Changed"), ResetMode.Default, null);
                p.AddField(Field("<Property>k__BackingField"), ResetMode.Default, null);
            });
            RunEnter(plan);
            ResetFixture.Changed += () => { };
            ResetFixture.Property = 7;
            RunEnter(plan);

            Assert.IsFalse(ResetFixture.HasChangedSubscribers);
            Assert.AreEqual(0, ResetFixture.Property);
        }

        [Test]
        public void InvalidFieldDeclarations_ReportErrorsAndCreateNoSteps()
        {
            var plan = Plan(p =>
            {
                p.AddField(Field(nameof(ResetFixture.Constant)), ResetMode.Default, null);     // readonly
                p.AddField(Field(nameof(ResetFixture.Mutable)), ResetMode.InitialValue, null); // 可变引用
                p.AddField(Field(nameof(ResetFixture.Counter)), ResetMode.Clear, null);        // 值类型
                p.AddField(typeof(InstanceFixture).GetField(nameof(InstanceFixture.Value)), ResetMode.Default, null);
            });

            Assert.IsEmpty(plan.FieldSteps);
            Assert.AreEqual(4, plan.Findings.Count(f => f.RuleId == RuleIds.InvalidDeclaration && f.Severity == Severity.Violation));
            Assert.IsTrue(plan.Findings.All(f => !f.Baselinable));
        }

        [Test]
        public void ResetMethods_RunByOrderAndMarkOwnerManaged()
        {
            var plan = Plan(p =>
            {
                p.AddMethod(Method(nameof(ResetFixture.ResetLate)), 10, true, null);
                p.AddMethod(Method(nameof(ResetFixture.ResetEarly)), -10, true, null);
            });
            RunEnter(plan);

            CollectionAssert.AreEqual(new[] { "early", "late" }, ResetFixture.Calls);
            Assert.IsTrue(plan.IsOwnerManaged(typeof(ResetFixture)));
        }

        [Test]
        public void InvalidMethodDeclarations_ReportErrors()
        {
            var plan = Plan(p =>
            {
                p.AddMethod(Method(nameof(ResetFixture.AsyncReset)), 0, true, null);
                p.AddMethod(Method(nameof(ResetFixture.WithArgument)), 0, true, null);
                p.AddMethod(Method(nameof(ResetFixture.ReturnsValue)), 0, false, null);
                p.AddMethod(typeof(InstanceFixture).GetMethod(nameof(InstanceFixture.Reset)), 0, true, null);
            });

            Assert.IsEmpty(plan.EnterSteps);
            Assert.IsEmpty(plan.ExitSteps);
            Assert.AreEqual(4, plan.Findings.Count(f => f.RuleId == RuleIds.InvalidDeclaration));
            Assert.IsFalse(plan.IsOwnerManaged(typeof(ResetFixture)));
        }

        [Test]
        public void GenericField_ExpandsClosedConstructionsFromDerivedTypes()
        {
            var plan = Plan(p => p.AddField(typeof(GenericHolder<>).GetField("Value", k_Static), ResetMode.Default, null));
            RunEnter(plan);
            GenericHolder<string>.Value = "x";
            RunEnter(plan);

            Assert.AreEqual(1, plan.FieldSteps.Count);
            Assert.IsNull(GenericHolder<string>.Value);
            Assert.IsTrue(plan.Findings.Any(f => f.RuleId == RuleIds.GenericExpansion && f.Severity == Severity.Suggestion));
        }

        [Test]
        public void PropertyAttribute_AutoDetectsBackingField()
        {
            var prop = typeof(ResetFixture).GetProperty(nameof(ResetFixture.AutoProp), k_Static);
            var plan = Plan(p => p.AddAutoProperty(prop, ResetMode.Default, null));
            RunEnter(plan);
            ResetFixture.AutoProp = 42;
            RunEnter(plan);

            Assert.AreEqual(0, ResetFixture.AutoProp);
            Assert.IsEmpty(plan.Findings);
        }

        [Test]
        public void PropertyAttribute_ReportsErrorForNonAutoProperty()
        {
            var prop = typeof(ResetFixture).GetProperty(nameof(ResetFixture.ManualProp), k_Static);
            var plan = Plan(p => p.AddAutoProperty(prop, ResetMode.Default, null));

            Assert.IsEmpty(plan.FieldSteps);
            Assert.AreEqual(1, plan.Findings.Count(f => f.RuleId == RuleIds.InvalidDeclaration));
        }

        [Test]
        public void PropertyAttribute_SkipsDuplicateWhenBackingFieldAlreadyHandled()
        {
            var prop = typeof(ResetFixture).GetProperty(nameof(ResetFixture.AutoProp), k_Static);
            var backing = typeof(ResetFixture).GetField("<AutoProp>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            var plan = Plan(p =>
            {
                p.AddField(backing, ResetMode.Default, null);
                p.AddAutoProperty(prop, ResetMode.Default, null);
            });

            Assert.AreEqual(1, plan.FieldSteps.Count);
        }

        [Test]
        public void MemberAttributes_DiscoverStaticMembersAndRejectInstanceMembers()
        {
            var plan = Plan(p =>
            {
                p.AddMemberAttributes(typeof(ResetFixture));
                p.AddMemberAttributes(typeof(InstanceAttributeFixture));
            });

            CollectionAssert.AreEquivalent(new[] { "<AutoProp>k__BackingField", nameof(ResetFixture.AutoEvent) },
                plan.FieldSteps.Select(s => s.Field.Name));
            var errors = plan.Findings.Where(f => f.RuleId == RuleIds.InvalidDeclaration).ToList();
            Assert.AreEqual(2, errors.Count);
            Assert.IsTrue(errors.All(f => f.Subject.Contains(nameof(InstanceAttributeFixture))));
            Assert.IsTrue(errors.All(f => f.Severity == Severity.Violation && !f.Baselinable), "声明错误不可暂缓");
        }

        [Test]
        public void EventAttribute_AutoDetectsBackingField()
        {
            var evt = typeof(ResetFixture).GetEvent(nameof(ResetFixture.AutoEvent), k_Static);
            var plan = Plan(p => p.AddEvent(evt, ResetMode.Default, null));
            RunEnter(plan);
            ResetFixture.AutoEvent += () => { };
            RunEnter(plan);

            Assert.IsFalse(ResetFixture.HasAutoEventSubscribers);
            Assert.IsEmpty(plan.Findings);
        }

        [Test]
        public void EventAttribute_ReportsErrorForCustomAccessorEvent()
        {
            var evt = typeof(ResetFixture).GetEvent(nameof(ResetFixture.CustomEvent), k_Static);
            var plan = Plan(p => p.AddEvent(evt, ResetMode.Default, null));

            Assert.IsEmpty(plan.FieldSteps);
            Assert.AreEqual(1, plan.Findings.Count(f => f.RuleId == RuleIds.InvalidDeclaration));
        }

        [Test]
        public void Adapter_ResolvesMembersByNameAndRegistersSteps()
        {
            var plan = Plan(p => p.ApplyAdapter(typeof(WorkingAdapter), NoPackages));
            ResetFixture.Counter = 0;
            RunEnter(plan);
            ResetFixture.Counter = 9;
            RunEnter(plan);

            Assert.AreEqual(0, ResetFixture.Counter);
            CollectionAssert.AreEqual(new[] { "adapter", "adapter" }, ResetFixture.Calls);
            Assert.IsTrue(plan.IsFieldHandled(Field(nameof(ResetFixture.Counter))));
            Assert.AreEqual(1, plan.RequiredReloadReasons.Count);
            Assert.IsFalse(plan.Adapters.Single().BuiltIn, "项目适配器不比较版本");
            Assert.IsTrue(plan.RequiresReload(plan.Adapters.Single().Target));
        }

        [Test]
        public void Adapter_MissingMembersReportBrokenErrors()
        {
            var plan = Plan(p => p.ApplyAdapter(typeof(BrokenAdapter), NoPackages));

            Assert.IsEmpty(plan.FieldSteps);
            Assert.AreEqual(2, plan.Findings.Count(f => f.RuleId == RuleIds.AdapterBroken && f.Severity == Severity.Violation));
            Assert.AreEqual(1, plan.Findings.Count(f => f.RuleId == RuleIds.AdapterInvalid));
        }

        [Test]
        public void Adapter_KnownIssueIsDeferrableButDoesNotPermitAssemblySelfHandled()
        {
            var plan = Plan(p => p.ApplyAdapter(typeof(KnownIssueAdapter), NoPackages));

            var issue = plan.Findings.Single(f => f.RuleId == RuleIds.AdapterKnownIssue);
            Assert.AreEqual(Severity.Violation, issue.Severity);
            Assert.IsTrue(issue.Baselinable);
            StringAssert.EndsWith("." + nameof(ResetFixture.Counter), issue.Subject);
            Assert.AreEqual(1, plan.Findings.Count(f => f.RuleId == RuleIds.AdapterBroken), "成员不存在时报告适配器失效");
            Assert.IsFalse(plan.IsSelfHandled(plan.Adapters.Single().Target));
            Assert.IsTrue(plan.Findings.Any(f => f.RuleId == RuleIds.AdapterInvalid && !f.Baselinable));
        }

        [Test]
        public void Adapter_UnverifiedBuiltInVersionDoesNotTrustTarget()
        {
            var plan = Plan(p => p.ApplyAdapter(typeof(UnverifiedAdapter), OfficialPackages, builtIn: true));
            var status = plan.Adapters.Single();

            Assert.IsFalse(status.Verified);
            Assert.IsFalse(plan.IsSelfHandled(status.Target));
            Assert.IsTrue(plan.Findings.Any(f => f.RuleId == RuleIds.AdapterUnverified && f.Severity == Severity.Violation && !f.Baselinable));
        }

        [Test]
        public void Adapter_ProjectAdapterDoesNotCompareVersions()
        {
            var plan = Plan(p => p.ApplyAdapter(typeof(UnverifiedAdapter), OfficialPackages));
            var status = plan.Adapters.Single();

            Assert.IsFalse(status.BuiltIn);
            Assert.IsFalse(plan.Findings.Any(f => f.RuleId == RuleIds.AdapterUnverified));
            Assert.AreSame(status, plan.ProjectSelfHandler(status.Target), "官方包的 SelfHandled 交给静态字段比对");
        }

        [Test]
        public void Adapter_DormantWhenTargetMissing()
        {
            var plan = Plan(p => p.ApplyAdapter(typeof(DormantAdapter), NoPackages));

            Assert.IsFalse(plan.Adapters.Single().Active);
            Assert.IsEmpty(plan.Findings);
        }

        [Test]
        public void InstalledPackage_DescribesSource()
        {
            var git = new InstalledPackage("1.0.0", "Git", "1.1.69", "70699041c85434b2ea7988df51c4b47052d484ed");
            StringAssert.Contains("1.1.69", git.Describe());
            StringAssert.Contains("70699041", git.Describe());
            StringAssert.Contains("嵌入", new InstalledPackage("1.0.0", "Embedded", null, null).Describe());
            Assert.AreEqual("17.3.0", new InstalledPackage("17.3.0", "Registry", null, null).Describe());
        }

        [Test]
        public void AdapterReview_SignaturesSkipConstantsAndCompilerCaches()
        {
            var signatures = AdapterReview.Signatures(typeof(ReviewFixture));
            CollectionAssert.AreEqual(new[]
            {
                "<Current>k__BackingField:String",
                "s_Map:Dictionary<String,List<Int32>> readonly",
                "s_Values:Int32[]",
            }, signatures);
        }

        [Test]
        public void AdapterReview_ReportsDifferenceFromRecordedLine()
        {
            var subject = ReflectionUtil.Subject(typeof(ReviewFixture));
            var prefix = RuleIds.AdapterReviewChanged + "|" + subject;
            var baseline = new HashSet<string> { prefix + "|字段=s_Old:Int32;s_Values:Int32[]" };

            var previous = AdapterReview.Previous(baseline, prefix);
            CollectionAssert.AreEquivalent(new[] { "s_Old:Int32", "s_Values:Int32[]" }, previous);
            Assert.IsNull(AdapterReview.Previous(new HashSet<string>(), prefix));

            var message = AdapterReview.Describe("A", "SelfHandled", AdapterReview.Signatures(typeof(ReviewFixture)), previous);
            StringAssert.Contains("删除 s_Old:Int32", message);
            StringAssert.Contains("s_Map", message);
            StringAssert.DoesNotContain("s_Values", message, "没变的字段不列出");
        }

        static readonly IReadOnlyDictionary<string, InstalledPackage> NoPackages = new Dictionary<string, InstalledPackage>();

        static readonly IReadOnlyDictionary<string, InstalledPackage> OfficialPackages = new Dictionary<string, InstalledPackage>
        {
            ["com.unity.test-fixture"] = new InstalledPackage("1.0.0", "Registry", null, null, true),
        };

        [Test]
        public void Adapter_SelfHandledRejectsNonOfficialPackage()
        {
            var packages = new Dictionary<string, InstalledPackage>
            {
                ["com.unity.test-fixture"] = new InstalledPackage("1.0.0", "Embedded", null, null),
            };
            var plan = Plan(p => p.ApplyAdapter(typeof(UnverifiedAdapter), packages));
            Assert.IsFalse(plan.IsSelfHandled("com.unity.test-fixture"), "包名不是来源证明");
            Assert.IsTrue(plan.Findings.Any(f => f.RuleId == RuleIds.AdapterInvalid && !f.Baselinable));
        }

        [Test]
        public void Adapter_ResetTypeRegistersExecutableReset()
        {
            var plan = Plan(p => p.ApplyAdapter(typeof(TypeResetAdapter), NoPackages));
            OwnerFixture.Value = 17;
            RunEnter(plan);
            Assert.AreEqual(0, OwnerFixture.Value);
            Assert.IsTrue(plan.IsResetType(typeof(OwnerFixture)));
            Assert.IsEmpty(plan.Findings);
        }

        [Test]
        public void GenericField_ExpandsConstraintsAndExplicitTypeArguments()
        {
            var plan = Plan(p => p.ApplyAdapter(typeof(GenericResetAdapter), NoPackages));
            CollectionAssert.AreEquivalent(new[] { typeof(ConstrainedHolder<ConcreteConstraint>), typeof(GenericHolder<int>) },
                plan.FieldSteps.Select(s => s.Field.DeclaringType));
            RunEnter(plan);
            ConstrainedHolder<ConcreteConstraint>.Value = 9;
            GenericHolder<int>.Value = 4;
            RunEnter(plan);
            Assert.AreEqual(0, ConstrainedHolder<ConcreteConstraint>.Value);
            Assert.AreEqual(0, GenericHolder<int>.Value);
            Assert.IsFalse(plan.Findings.Any(f => f.IsViolation));
        }

        [Test]
        public void GenericField_InvalidOrOpenArgumentsCannotBeDeferred()
        {
            var plan = Plan(p => p.AddGenericField(typeof(ConstrainedHolder<>).GetField("Value"), ResetMode.Default, "test",
                new[] { typeof(string), typeof(GenericHolder<>), null }));
            Assert.IsEmpty(plan.FieldSteps);
            Assert.IsTrue(plan.Findings.Any(f => f.RuleId == RuleIds.AdapterBroken));
            Assert.IsTrue(plan.Findings.All(f => f.IsViolation && !f.Baselinable));
        }

        [Test]
        public void InitialValue_ObservedBeforeCallbacksRestoresOriginalValue()
        {
            var plan = Plan(p => p.AddField(Field(nameof(ResetFixture.Seeded)), ResetMode.InitialValue, null));
            plan.FieldSteps.Single().Observe();
            ResetFixture.Seeded = 7; // 模拟 InitializeOnEnterPlayMode 或编辑对象退出回调。
            RunEnter(plan);
            Assert.AreEqual(5, ResetFixture.Seeded);
            ResetFixture.Seeded = 11;
            RunEnter(plan);
            Assert.AreEqual(5, ResetFixture.Seeded, "第二轮不能重新采集已被业务修改的值");
        }

        static readonly FieldInfo k_Initial = typeof(FieldResetStep).GetField("m_Initial", BindingFlags.Instance | BindingFlags.NonPublic);

        static FieldInfo Field(string name) => typeof(ResetFixture).GetField(name, k_Static);

        static MethodInfo Method(string name) => typeof(ResetFixture).GetMethod(name, k_Static);

        static ResetPlan Plan(System.Action<ResetPlan> configure)
        {
            var plan = new ResetPlan();
            configure(plan);
            plan.Seal();
            return plan;
        }

        static void RunEnter(ResetPlan plan)
        {
            foreach (var step in plan.FieldSteps) step.Execute();
            foreach (var step in plan.EnterSteps) step.Execute();
        }
    }
}
