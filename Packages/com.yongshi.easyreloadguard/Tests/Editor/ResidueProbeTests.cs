using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
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
        public void Capture_SkipsTypesWithStaticConstructorUnlessAllowed()
        {
            var skipped = Capture(typeof(InitializedResidueFixture), false);
            Assert.AreEqual(1, skipped.Skipped);
            Assert.AreEqual(0, skipped.Read);

            InitializedResidueFixture.Items.Add(1);
            var read = Capture(typeof(InitializedResidueFixture), true);
            Assert.AreEqual(0, read.Skipped);
            Assert.AreEqual(1, read.Counts.Values.Single());
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
    }
}
