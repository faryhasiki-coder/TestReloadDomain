using System.Collections;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using MathRandom = Unity.Mathematics.Random;

namespace MathematicsValidation
{
    [Category("Case.Mathematics")]
    public sealed class MathematicsValidationTests
    {
        const uint Seed = 1234;
        const string SettingsKey = "MathematicsValidation.Original.";
        static MathRandom retainedRandom;

        [SetUp]
        public void SetUp()
        {
            SessionState.SetBool(SettingsKey + "enabled", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(SettingsKey + "options", (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(SettingsKey + "background", Application.runInBackground);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            retainedRandom = default;
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(SettingsKey + "enabled", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(SettingsKey + "options", 0);
            Application.runInBackground = SessionState.GetBool(SettingsKey + "background", false);
        }

        [Test]
        [Category("Scenario.Stateless")]
        [Description("MATH-01: Public vector operations give known results without lifecycle hooks.")]
        public void VectorOperationsHaveKnownResults()
        {
            var a = new float3(1, 2, 3);
            var b = new float3(3, 2, 1);
            Assert.AreEqual(new float3(4, 4, 4), a + b);
            Assert.AreEqual(10f, math.dot(a, b));
            Assert.AreEqual(5f, math.length(new float3(3, 4, 0)));
            Assert.AreEqual(new float3(0, 0, 1), math.cross(new float3(1, 0, 0), new float3(0, 1, 0)));
        }

        [UnityTest]
        [Category("Scenario.RepeatedPlay")]
        [Category("Scenario.RandomState")]
        [Description("MATH-02: A consumer-owned static RNG continues across Fast Play; explicit reseeding reproduces the baseline.")]
        public IEnumerator RandomStateOwnershipAcrossThreeFastPlayCycles()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            retainedRandom = new MathRandom(Seed);
            var reference = new MathRandom(Seed);
            var firstSequence = DrawFour(ref reference);
            reference = new MathRandom(Seed);

            for (int cycle = 1; cycle <= 3; cycle++)
            {
                uint stateBeforePlay = retainedRandom.state;
                yield return new EnterPlayMode();
                Application.runInBackground = true;
                Assert.AreEqual(stateBeforePlay, retainedRandom.state, "Fast Play must preserve this consumer-owned field.");

                var continued = DrawFour(ref retainedRandom);
                CollectionAssert.AreEqual(DrawFour(ref reference), continued, "The retained RNG must continue its sequence.");
                if (cycle > 1) CollectionAssert.AreNotEqual(firstSequence, continued);

                var resetForThisSession = new MathRandom(Seed);
                CollectionAssert.AreEqual(firstSequence, DrawFour(ref resetForThisSession), "A seeded session starts reproducibly.");
                Assert.AreEqual(new float3(4, 4, 4), new float3(1, 2, 3) + new float3(3, 2, 1));
                TestContext.WriteLine("MATH-02 cycle=" + cycle + ", retained=" + string.Join(",", continued) +
                    ", reseeded=" + string.Join(",", firstSequence));
                yield return new ExitPlayMode();
            }
        }

        static uint[] DrawFour(ref MathRandom random)
        {
            return new[] { random.NextUInt(), random.NextUInt(), random.NextUInt(), random.NextUInt() };
        }
    }
}
