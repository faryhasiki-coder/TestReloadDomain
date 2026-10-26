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
            Assert.AreEqual(4, plan.Findings.Count(f => f.RuleId == RuleIds.InvalidDeclaration && f.Severity == Severity.Error));
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
            Assert.IsTrue(plan.Findings.Any(f => f.RuleId == RuleIds.GenericExpansion && f.Severity == Severity.Info));
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
            Assert.IsTrue(plan.Adapters.Single().Verified);
        }

        [Test]
        public void Adapter_MissingMembersReportBrokenErrors()
        {
            var plan = Plan(p => p.ApplyAdapter(typeof(BrokenAdapter), NoPackages));

            Assert.IsEmpty(plan.FieldSteps);
            Assert.AreEqual(3, plan.Findings.Count(f => f.RuleId == RuleIds.AdapterBroken && f.Severity == Severity.Error));
        }

        [Test]
        public void Adapter_UnverifiedVersionWarnsButStillApplies()
        {
            var plan = Plan(p => p.ApplyAdapter(typeof(UnverifiedAdapter), NoPackages));
            var status = plan.Adapters.Single();

            Assert.IsFalse(status.Verified);
            Assert.IsTrue(plan.IsSelfHandled(status.Target));
            Assert.IsTrue(plan.Findings.Any(f => f.RuleId == RuleIds.AdapterUnverified && f.Severity == Severity.Warning));
        }

        [Test]
        public void Adapter_DormantWhenTargetMissing()
        {
            var plan = Plan(p => p.ApplyAdapter(typeof(DormantAdapter), NoPackages));

            Assert.IsFalse(plan.Adapters.Single().Active);
            Assert.IsEmpty(plan.Findings);
        }

        static readonly IReadOnlyDictionary<string, string> NoPackages = new Dictionary<string, string>();

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
