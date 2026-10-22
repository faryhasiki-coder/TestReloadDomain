using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.TestTools;

namespace UniTaskValidation
{
    // Native package characterization. Adapter-dependent tests are archived in
    // TestCases/References/UniTaskPlaySessions and are not compiled on this branch.
    [Category("Case.UniTask")]
    public sealed class UniTaskValidationTests
    {
        const string SettingsKey = "UniTaskValidation.Original.";
        const string EvidenceDirectory = "outputs/test-cases/unitask";
        readonly List<CancellationTokenSource> cancellations = new List<CancellationTokenSource>();
        Action<Exception> unmanagedHandler;

        [SetUp]
        public void SetUp()
        {
            UnityEditor.SessionState.SetBool(SettingsKey + "enabled", EditorSettings.enterPlayModeOptionsEnabled);
            UnityEditor.SessionState.SetInt(SettingsKey + "options", (int)EditorSettings.enterPlayModeOptions);
            UnityEditor.SessionState.SetBool(SettingsKey + "background", Application.runInBackground);
            Directory.CreateDirectory(EvidenceDirectory);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var cts in cancellations) cts.Cancel();
            if (unmanagedHandler != null) UniTaskScheduler.UnobservedTaskException -= unmanagedHandler;
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            foreach (var cts in cancellations) cts.Dispose();
            cancellations.Clear();
            EditorSettings.enterPlayModeOptionsEnabled = UnityEditor.SessionState.GetBool(SettingsKey + "enabled", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)UnityEditor.SessionState.GetInt(SettingsKey + "options", 0);
            Application.runInBackground = UnityEditor.SessionState.GetBool(SettingsKey + "background", false);
            var context = TestContext.CurrentContext;
            File.AppendAllText(EvidenceDirectory + "/test-results.jsonl", JsonUtility.ToJson(new TestEvidence {
                utc = DateTime.UtcNow.ToString("O"), test = context.Test.FullName,
                state = context.Result.Outcome.Status.ToString(), message = context.Result.Message }) + "\n");
        }

        [UnityTest]
        [Category("Scenario.Async")]
        [Description("UT-01: Editor Yield/Delay/WhenAll return 42; native AsTask cancellation is Faulted.")]
        public IEnumerator BasicAwaitWhenAllAndCancellationWorkInEditMode()
        {
            var work = EditModeWork(NewCancellation().Token).AsTask();
            yield return WaitFor(work);
            Assert.AreEqual(42, work.Result);
            var cts = NewCancellation();
            var canceled = UniTask.Delay(TimeSpan.FromHours(1), cancellationToken: cts.Token, cancelImmediately: true).AsTask();
            cts.Cancel();
            AssertCancellationFault(canceled);
        }

        [UnityTest]
        [Category("Scenario.ReloadBaseline")]
        [Description("UT-02: Domain Reload baseline has exactly one node per UniTask runner/yielder/context.")]
        public IEnumerator NormalReloadHasOneRunnerPerTimingAndCompletesWork()
        {
            ConfigurePlay(false);
            yield return new EnterPlayMode();
            Application.runInBackground = true;
            AssertRunnerCounts();
            var task = BasicWork(NewCancellation().Token).AsTask();
            yield return WaitFor(task);
            Assert.AreEqual(42, task.Result);
            Record("normal-play", 1);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        [Category("Scenario.RepeatedPlay")]
        [Category("Regression.KnownFailure")]
        [Description("UT-03: Three Fast Play cycles must not accumulate PlayerLoop nodes or Editor callbacks.")]
        public IEnumerator FastPlayDoesNotAccumulatePlayerLoopOrEditorCallbacks()
        {
            ConfigurePlay(true);
            int baseline = ReadOnlyEditorProbe.CountUniTaskCallbacks();
            Assert.GreaterOrEqual(baseline, 16, "The version-specific observation probe must find UniTask callbacks.");
            var counts = new List<int> { baseline };
            Record("fast-callback-baseline", 0);
            for (int cycle = 1; cycle <= 3; cycle++)
            {
                yield return new EnterPlayMode();
                Application.runInBackground = true;
                AssertRunnerCounts();
                var task = BasicWork(NewCancellation().Token).AsTask();
                yield return WaitFor(task);
                Assert.AreEqual(42, task.Result);
                Record("fast-play", cycle);
                yield return new ExitPlayMode();
                counts.Add(ReadOnlyEditorProbe.CountUniTaskCallbacks());
                Record("fast-exit", cycle);
            }
            // A vendor defect remains a failing regression, never an expected-pass assertion.
            Assert.AreEqual(baseline, counts.Last(), "UniTask Editor callbacks accumulated: " + string.Join(" -> ", counts));
        }

        [UnityTest]
        [Category("Scenario.Cancellation")]
        [Description("UT-04: Deferred cancellation waits for PlayerLoop; immediate cancellation completes synchronously.")]
        public IEnumerator DefaultCancellationWaitsForPlayerLoopButImmediateCancellationDoesNot()
        {
            ConfigurePlay(true);
            yield return new EnterPlayMode();
            Application.runInBackground = true;
            var deferredCts = NewCancellation();
            var deferred = UniTask.DelayFrame(100000, cancellationToken: deferredCts.Token).AsTask();
            deferredCts.Cancel();
            Assert.IsFalse(deferred.IsCompleted, "Default cancellation is observed by the runner.");
            yield return WaitFor(deferred, allowCancellation: true, allowFault: true);
            AssertCancellationFault(deferred);
            var immediateCts = NewCancellation();
            var immediate = UniTask.DelayFrame(100000, cancellationToken: immediateCts.Token, cancelImmediately: true).AsTask();
            immediateCts.Cancel();
            AssertCancellationFault(immediate);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        [Category("Scenario.EventOwnership")]
        [Description("UT-05: Characterizes native user-subscription persistence; TearDown explicitly unsubscribes.")]
        public IEnumerator NativeExceptionSubscriptionRemainsAfterFastPlayExit()
        {
            ConfigurePlay(true);
            int calls = 0;
            unmanagedHandler = _ => calls++;
            yield return new EnterPlayMode();
            UniTaskScheduler.UnobservedTaskException += unmanagedHandler;
            UniTask.FromException(new InvalidOperationException("validation before exit")).Forget();
            Assert.AreEqual(1, calls);
            yield return new ExitPlayMode();
            UniTask.FromException(new InvalidOperationException("validation after exit")).Forget();
            Assert.AreEqual(2, calls, "Characterization: user subscriptions still require explicit cleanup.");
        }

        static async UniTask<int> BasicWork(CancellationToken token)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, token, cancelImmediately: true);
            await UniTask.NextFrame(token, cancelImmediately: true);
            await UniTask.WhenAll(UniTask.DelayFrame(2, cancellationToken: token, cancelImmediately: true),
                UniTask.Delay(TimeSpan.FromMilliseconds(20), ignoreTimeScale: true, cancellationToken: token, cancelImmediately: true));
            return 42;
        }

        static async UniTask<int> EditModeWork(CancellationToken token)
        {
            // EditorApplication.update does not guarantee Time.frameCount advancement.
            await UniTask.Yield(PlayerLoopTiming.Update, token, cancelImmediately: true);
            await UniTask.WhenAll(UniTask.Delay(TimeSpan.FromMilliseconds(20), cancellationToken: token, cancelImmediately: true),
                UniTask.Delay(TimeSpan.FromMilliseconds(30), cancellationToken: token, cancelImmediately: true));
            return 42;
        }

        static void AssertCancellationFault(Task task)
        {
            Assert.IsTrue(task.IsFaulted, "Native UniTask.AsTask represents cancellation as a fault in 2.5.11.");
            Assert.IsInstanceOf<OperationCanceledException>(task.Exception.GetBaseException());
        }

        CancellationTokenSource NewCancellation()
        {
            var cts = new CancellationTokenSource(); cancellations.Add(cts); return cts;
        }

        static IEnumerator WaitFor(Task task, bool allowCancellation = false, bool allowFault = false)
        {
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (!task.IsCompleted && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.IsTrue(task.IsCompleted, "UniTask did not complete within 10 seconds. Playing=" + Application.isPlaying +
                ", background=" + Application.runInBackground + ", frame=" + Time.frameCount + ", paused=" + EditorApplication.isPaused);
            if (!allowFault) Assert.IsFalse(task.IsFaulted, task.Exception?.ToString());
            if (!allowCancellation) Assert.IsFalse(task.IsCanceled);
        }

        static void ConfigurePlay(bool fast)
        {
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = fast ? EnterPlayModeOptions.DisableDomainReload : EnterPlayModeOptions.None;
        }

        static Dictionary<string, int> RunnerCounts()
        {
            var result = new Dictionary<string, int>();
            void Visit(PlayerLoopSystem loop)
            {
                if (loop.type?.DeclaringType == typeof(UniTaskLoopRunners) || loop.type == typeof(UniTaskSynchronizationContext))
                {
                    string name = loop.type.FullName;
                    result[name] = result.TryGetValue(name, out int count) ? count + 1 : 1;
                }
                if (loop.subSystemList != null) foreach (var child in loop.subSystemList) Visit(child);
            }
            Visit(PlayerLoop.GetCurrentPlayerLoop());
            return result;
        }

        static void AssertRunnerCounts()
        {
            Assert.IsTrue(PlayerLoopHelper.IsInjectedUniTaskPlayerLoop());
            var counts = RunnerCounts();
            Assert.AreEqual(Enum.GetValues(typeof(PlayerLoopTiming)).Length * 2 + 1, counts.Count);
            foreach (var pair in counts) Assert.AreEqual(1, pair.Value, pair.Key);
        }

        [Serializable]
        sealed class TestEvidence
        {
            public string utc;
            public string test;
            public string state;
            public string message;
        }

        [Serializable]
        sealed class Observation
        {
            public string utc;
            public string phase;
            public int cycle;
            public int editorCallbacks;
            public int runnerKinds;
            public int duplicateRunners;
        }

        static void Record(string phase, int cycle)
        {
            var counts = RunnerCounts();
            var observation = new Observation { utc = DateTime.UtcNow.ToString("O"), phase = phase, cycle = cycle,
                editorCallbacks = ReadOnlyEditorProbe.CountUniTaskCallbacks(), runnerKinds = counts.Count,
                duplicateRunners = counts.Count(x => x.Value != 1) };
            File.AppendAllText(EvidenceDirectory + "/observations.jsonl", JsonUtility.ToJson(observation) + "\n");
        }
    }

    // Read-only diagnostics for Unity 6000.3.8f1. Never detaches or overwrites vendor callbacks.
    static class ReadOnlyEditorProbe
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        public static int CountUniTaskCallbacks()
        {
            Assert.AreEqual("6000.3.8f1", Application.unityVersion, "Review the probe after changing Unity.");
            object tracker = RequiredField(typeof(EditorApplication), "m_PlayModeStateChangedEvent").GetValue(null);
            object holder = RequiredField(tracker.GetType(), "m_Delegate").GetValue(tracker);
            object entry = RequiredField(holder.GetType(), "m_DelegateOrList").GetValue(holder);
            return Count(entry);
        }

        static int Count(object value)
        {
            if (value == null) return 0;
            if (value is Delegate callback)
                return callback.GetInvocationList().Count(d => d.Method.DeclaringType?.Assembly == typeof(PlayerLoopHelper).Assembly &&
                    d.Method.DeclaringType.FullName.StartsWith(typeof(PlayerLoopHelper).FullName, StringComparison.Ordinal));
            if (value is Array entries) { int count = 0; foreach (var entry in entries) count += Count(entry); return count; }
            return Count(RequiredField(value.GetType(), "Reference").GetValue(value));
        }

        static FieldInfo RequiredField(Type type, string name)
        {
            var field = type.GetField(name, Flags);
            Assert.IsNotNull(field, "Unity event storage changed: " + type.FullName + "." + name);
            return field;
        }
    }
}
