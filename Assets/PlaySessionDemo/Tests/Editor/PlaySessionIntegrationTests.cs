using System.Collections;
using System.Linq;
using Faryhasiki.PlaySessions;
using Faryhasiki.PlaySessions.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PlaySessionDemo.Tests
{
    public sealed class PlaySessionIntegrationTests
    {
        const string Key = "PlaySessions.Tests.";
        [SetUp]
        public void SaveSettings()
        {
            UnityEditor.SessionState.SetBool(Key + "enabled", EditorSettings.enterPlayModeOptionsEnabled);
            UnityEditor.SessionState.SetInt(Key + "options", (int)EditorSettings.enterPlayModeOptions);
        }

        [UnityTearDown]
        public IEnumerator RestoreSettings()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            EditorSettings.enterPlayModeOptionsEnabled = UnityEditor.SessionState.GetBool(Key + "enabled", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)UnityEditor.SessionState.GetInt(Key + "options", 0);
        }

        [UnityTest]
        public IEnumerator ThreeFastPlayCyclesResetStateAndNeverDuplicateSubscriptions()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            for (int i = 0; i < 3; i++)
            {
                yield return new EnterPlayMode();
                Assert.AreEqual(Faryhasiki.PlaySessions.SessionState.Running, PlaySession.Host.State);
                Assert.AreEqual(0, Wallet.Coins); Assert.AreEqual(0, Inventory.Items.Count);
                Assert.AreEqual(0, LegacyBus.Count); Assert.AreEqual(1, LegacyBus.Subscribers);
                Assert.IsNotNull(DemoInput.Action); Assert.IsTrue(DemoInput.Action.enabled);
                Assert.AreEqual(0, DemoInput.PerformedCount);
                var keyboard = InputSystem.AddDevice<Keyboard>();
                try
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState(UnityEngine.InputSystem.Key.Space));
                    InputSystem.Update();
                    Assert.AreEqual(1, DemoInput.PerformedCount, "Input callback must fire exactly once per session.");
                }
                finally { InputSystem.RemoveDevice(keyboard); }
                Wallet.CollectCoin(); Inventory.Items.Add("coin"); LegacyBus.Publish();
                Assert.AreEqual(1, Wallet.Coins); Assert.AreEqual(1, Inventory.EventCount);
                yield return new ExitPlayMode();
                Assert.AreEqual(Faryhasiki.PlaySessions.SessionState.Idle, PlaySession.Host.State);
                Assert.AreEqual(0, LegacyBus.Subscribers);
                Assert.IsNull(DemoInput.Action);
            }
        }

        [UnityTest]
        public IEnumerator NormalDomainReloadUsesTheSameLifecycle()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
            yield return new EnterPlayMode();
            Assert.AreEqual(Faryhasiki.PlaySessions.SessionState.Running, PlaySession.Host.State);
            Assert.AreEqual(0, Wallet.Coins); Assert.AreEqual(1, LegacyBus.Subscribers);
            yield return new ExitPlayMode();
            Assert.AreEqual(0, LegacyBus.Subscribers);
        }

        [Test]
        public void FullProjectAuditFindsDemoAndHasNoUnknownDemoState()
        {
            var report = PlaySessionAudit.Run();
            CollectionAssert.Contains(report.scannedAssemblies, "PlaySessionDemo");
            Assert.IsFalse(report.diagnostics.Any(d => d.code == "PS101" && d.target.StartsWith("PlaySessionDemo.")));
            Assert.IsFalse(report.HasErrors, string.Join("\n", report.diagnostics));
        }

        [Test]
        public void SceneReloadDisabledIsDiagnosed()
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            Assert.IsTrue(PlaySessionAudit.Run(false).diagnostics.Any(d => d.code == "PS302"));
        }

        [Test]
        public void UnityUpgradeRequiresExplicitRevalidation()
        {
            var settings = PlaySessionsSettings.instance;
            var oldVersion = settings.verifiedUnityVersion;
            try
            {
                EditorSettings.enterPlayModeOptionsEnabled = true;
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
                settings.verifiedUnityVersion = "unverified-version";
                Assert.IsTrue(PlaySessionAudit.Run(false).diagnostics.Any(d => d.code == "PS301"));
            }
            finally { settings.verifiedUnityVersion = oldVersion; }
        }

        [Test]
        public void NativeRestartRequirementSurvivesChangingPlayOptions()
        {
            var previous = UnityEditor.SessionState.GetBool(PlaySessionEditor.RestartRequiredKey, false);
            try
            {
                UnityEditor.SessionState.SetBool(PlaySessionEditor.RestartRequiredKey, true);
                EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
                Assert.IsTrue(PlaySessionAudit.Run(false).diagnostics.Any(d => d.code == "PS212"));
            }
            finally { UnityEditor.SessionState.SetBool(PlaySessionEditor.RestartRequiredKey, previous); }
        }
    }
}
