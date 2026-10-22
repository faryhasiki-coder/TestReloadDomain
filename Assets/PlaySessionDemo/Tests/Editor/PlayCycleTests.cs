using System;
using System.Collections;
using System.Linq;
using Faryhasiki.PlaySessions;
using Faryhasiki.PlaySessions.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using BeforeWallet = PlaySessionDemo.CodeComparison.Before.CoinWallet;
using AfterWallet = PlaySessionDemo.CodeComparison.After.CoinWallet;

namespace PlaySessionDemo.Tests
{
    public sealed class PlayCycleTests
    {
        private bool originalEnabled;
        private EnterPlayModeOptions originalOptions;

        [SetUp]
        public void ConfigureFastPlay()
        {
            Assert.That(EditorApplication.isPlaying, Is.False);
            originalEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            originalOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }

        [UnityTearDown]
        public IEnumerator RestoreConfiguration()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            var deadline = EditorApplication.timeSinceStartup + 4;
            while (PlaySessionRuntime.Current != null && PlaySessionRuntime.Current.PendingTasks > 0 && EditorApplication.timeSinceStartup < deadline)
            {
                PlaySessionRuntime.Pump();
                yield return null;
            }
            PlaySessionRuntime.Pump();
            EditorSettings.enterPlayModeOptions = originalOptions;
            EditorSettings.enterPlayModeOptionsEnabled = originalEnabled;
        }

        [UnityTest]
        public IEnumerator ThreeFastPlayCyclesResetStateWithoutDuplicateSubscribers()
        {
            var previousId = Guid.Empty;
            var previousRaw = DemoRaw.Counter;
            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return new EnterPlayMode(false);
                var host = PlaySessionRuntime.Current;
                Assert.That(host, Is.Not.Null);
                Assert.That(host.Status, Is.EqualTo(SessionStatus.Ready));
                Assert.That(host.Id, Is.Not.EqualTo(previousId));
                Assert.That(DemoState.Counter, Is.Zero);
                Assert.That(DemoRaw.Counter, Is.EqualTo(previousRaw + 1), "The comparison field must actually persist without domain reload.");
                Assert.That(DemoSignals.SubscriberCount, Is.EqualTo(1));
                Assert.That(DemoLease.LiveCount, Is.EqualTo(1));
                DemoSignals.Raise();
                Assert.That(DemoState.Counter, Is.EqualTo(1));
                Assert.That(DemoState.Current.SendToolRequest(previousId), Is.False);
                Assert.That(DemoState.Current.SendToolRequest(host.Id), Is.True);
                previousId = host.Id;
                previousRaw = DemoRaw.Counter;
                yield return new ExitPlayMode();
                Assert.That(host.CanStartNext, Is.True);
                Assert.That(host.ResourceCount, Is.Zero);
                Assert.That(DemoSignals.SubscriberCount, Is.Zero);
                Assert.That(DemoLease.LiveCount, Is.Zero);
                Assert.That(DemoState.Current, Is.Null);
            }
        }

        [UnityTest]
        public IEnumerator CoinSampleShowsActualBeforeAndAfterAcrossFastPlay()
        {
            var originalCoins = BeforeWallet.Coins;
            for (var cycle = 0; cycle < 2; cycle++)
            {
                yield return new EnterPlayMode(false);
                Assert.That(PlaySessionRuntime.IsReady, Is.True);
                Assert.That(BeforeWallet.Coins, Is.EqualTo(originalCoins + cycle * 3));
                Assert.That(AfterWallet.Coins, Is.Zero, "The registered reset must run before the scene starts.");
                for (var coin = 0; coin < 3; coin++)
                {
                    BeforeWallet.CollectCoin();
                    AfterWallet.CollectCoin();
                }
                Assert.That(BeforeWallet.Coins, Is.EqualTo(originalCoins + (cycle + 1) * 3));
                Assert.That(AfterWallet.Coins, Is.EqualTo(3));
                yield return new ExitPlayMode();
            }
        }

        [UnityTest]
        public IEnumerator ExitingPlayCancelsBackgroundWorkAndReleasesResources()
        {
            yield return new EnterPlayMode(false);
            var host = PlaySessionRuntime.Current;
            DemoState.Current.LoadLater();
            Assert.That(host.PendingTasks, Is.EqualTo(1));
            yield return new ExitPlayMode();
            var deadline = EditorApplication.timeSinceStartup + 4;
            while (!host.CanStartNext && EditorApplication.timeSinceStartup < deadline)
            {
                host.Pump();
                yield return null;
            }
            Assert.That(host.CanStartNext, Is.True);
            Assert.That(host.PendingTasks, Is.Zero);
            Assert.That(host.ResourceCount, Is.Zero);
            Assert.That(DemoSignals.SubscriberCount, Is.Zero);
            Assert.That(DemoLease.LiveCount, Is.Zero);
        }

        [Test]
        public void UnsupportedSceneReloadModeHasAnExplicitDiagnostic()
        {
            EditorSettings.enterPlayModeOptions |= EnterPlayModeOptions.DisableSceneReload;
            var issues = PlaySessionEditorBridge.ValidateEnvironment(CompositionDiscovery.Discover());
            Assert.That(issues.Any(issue => issue.Code == "PLAY114"), Is.True);
        }
    }
}
