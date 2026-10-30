using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace EasyReloadGuard.Editor.Tests
{
    public class ResidueProbeTests
    {
        const BindingFlags k_Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        [TearDown]
        public void TearDown()
        {
            ResidueFixture.Callback = null;
            ResidueFixture.Toggle = null;
            ResidueFixture.Items = null;
            ResidueFixture.Destroyed = null;
            InitializedResidueFixture.Items.Clear();
        }

        [Test]
        public void Compare_ReportsDestroyedReferencesAlwaysAndGrowthFromSecondReturn()
        {
            var probe = new GameObject("ResidueProbeTests");
            ResidueFixture.Destroyed = probe;
            ResidueFixture.Toggle = probe.SetActive;
            Object.DestroyImmediate(probe);
            ResidueFixture.Callback = OnCallback;
            ResidueFixture.Items = new List<int> { 1 };
            var first = Capture(typeof(ResidueFixture), false);

            ResidueFixture.Callback += OnCallback;
            ResidueFixture.Items.Add(2);
            var second = Capture(typeof(ResidueFixture), false);

            var initial = ResidueProbe.Compare(null, first);
            Assert.AreEqual(2, initial.Destroyed.Count, "首次只报告已销毁对象：字段本身与回调目标");
            Assert.IsEmpty(initial.Growth);

            var result = ResidueProbe.Compare(first, second);
            Assert.IsTrue(result.Destroyed.Any(d => d.Contains(nameof(ResidueFixture.Destroyed)) && d.Contains("GameObject")));
            Assert.IsTrue(result.Destroyed.Any(d => d.Contains(nameof(ResidueFixture.Toggle)) && d.Contains("目标已销毁")));
            Assert.AreEqual(2, result.Growth.Count);
            Assert.IsTrue(result.Growth.Any(g => g.Contains(nameof(ResidueFixture.Callback)) && g.Contains("1 → 2")));
            Assert.IsTrue(result.Growth.Any(g => g.Contains(nameof(ResidueFixture.Items)) && g.Contains("1 → 2")));
        }

        [Test]
        public void Capture_SkipsImpureStaticConstructorsUnlessAllowed()
        {
            var skipped = Capture(typeof(ImpureResidueFixture), false);
            Assert.AreEqual(1, skipped.Skipped);
            Assert.AreEqual(0, skipped.Read);

            var read = Capture(typeof(ImpureResidueFixture), true);
            Assert.AreEqual(0, read.Skipped);
            Assert.AreEqual(1, read.Counts.Values.Single());
        }

        [Test]
        public void Capture_ReadsTypesWithPureStaticConstructorByDefault()
        {
            InitializedResidueFixture.Items.Add(1);
            var snapshot = Capture(typeof(InitializedResidueFixture), false);

            Assert.AreEqual(0, snapshot.Skipped);
            Assert.AreEqual(1, snapshot.Counts.Values.Single());
        }

        [Test]
        public void InitializerPurity_AcceptsDataOnlyInitializersAndRejectsExternalCalls()
        {
            Assert.IsTrue(InitializerPurity.IsPure(typeof(ResidueFixture)), "没有静态构造");
            Assert.IsTrue(InitializerPurity.IsPure(typeof(InitializedResidueFixture)));
            Assert.IsTrue(InitializerPurity.IsPure(typeof(ShapeFixture)), "静态构造中填充 BCL 集合");
            Assert.IsTrue(InitializerPurity.IsPure(typeof(PureInitializerFixture)), "lambda 缓存、本类型纯方法、值类型构造、typeof、数组初始化");
            Assert.IsFalse(InitializerPurity.IsPure(typeof(ImpureResidueFixture)), "Environment 不在白名单");
            Assert.IsFalse(InitializerPurity.IsPure(typeof(UnityInitializerFixture)), "读取 UnityEngine 静态成员");
        }

        [Test]
        public void Capture_ReadsEditorEventsStoredInPerformanceTrackerContainers()
        {
            var container = typeof(EditorApplication).GetField("m_PlayModeStateChangedEvent", k_Static);
            Assume.That(container, Is.Not.Null, "本 Unity 版本的 playModeStateChanged 不使用 EventWithPerformanceTracker");
            Assert.IsTrue(EditorCompat.IsTrackedEventType(container.FieldType));

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            try
            {
                var snapshot = ResidueSnapshot.Capture(new[] { container }, Enumerable.Empty<FieldInfo>(), false);

                Assert.AreEqual(0, snapshot.Unreadable);
                var counts = snapshot.Callbacks.Values.Single();
                Assert.AreEqual(2, counts[typeof(ResidueProbeTests).FullName + "." + nameof(OnPlayModeStateChanged)]);
            }
            finally
            {
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
                EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            }
        }

        [Test]
        public void Capture_AlwaysReadsEngineCallbackFields()
        {
            var engine = typeof(ResidueFixture).GetField(nameof(ResidueFixture.Callback), k_Static);
            ResidueFixture.Callback = OnCallback;

            var snapshot = ResidueSnapshot.Capture(new[] { engine }, Enumerable.Empty<FieldInfo>(), false);

            Assert.AreEqual(1, snapshot.Read);
            Assert.AreEqual(1, snapshot.Callbacks.Values.Single().Values.Single());
        }

        static ResidueSnapshot Capture(System.Type type, bool readTypeInitializers) =>
            ResidueSnapshot.Capture(Enumerable.Empty<FieldInfo>(), type.GetFields(k_Static), readTypeInitializers);

        static void OnCallback()
        {
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
        }
    }
}
