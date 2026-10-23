using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace DomainReloadCases.Tests
{
    [Category("Case.ExecuteAlways")]
    public sealed class ExecuteAlwaysTests
    {
        const string Saved = "ExecuteAlwaysCases.Saved.";

        [SetUp]
        public void SetUp()
        {
            // 普通 Domain Reload 后 Test Runner 会再次执行 SetUp，不能覆盖原设置。
            if (SessionState.GetBool(Saved + "active", false)) return;
            Assert.IsNull(PrefabStageUtility.GetCurrentPrefabStage(), "请先结束当前 Prefab 编辑再运行本案例。");
            Assert.IsTrue(File.Exists(ExecuteAlwaysFixture.ScenePath));
            Assert.IsTrue(File.Exists(ExecuteAlwaysFixture.PrefabPath));
            Assert.IsFalse(SceneManager.GetSceneByPath(ExecuteAlwaysFixture.ScenePath).isLoaded,
                "请在其他场景运行测试；测试以附加方式打开自己的 fixture。");

            SessionState.SetString(Saved + "scene", SceneManager.GetActiveScene().path);
            SessionState.SetInt(Saved + "sceneHandle", SceneManager.GetActiveScene().handle);
            SessionState.SetInt(Saved + "options", (int)(EditorSettings.enterPlayModeOptionsEnabled
                ? EditorSettings.enterPlayModeOptions : EnterPlayModeOptions.None));
            SessionState.SetBool(Saved + "dirty", EditorUtility.IsDirty(SettingsAsset()));
            SessionState.SetBool(Saved + "background", Application.runInBackground);
            SessionState.SetString(Saved + "prefabHash", Hash(ExecuteAlwaysFixture.PrefabPath));
            SessionState.SetString(Saved + "sceneHash", Hash(ExecuteAlwaysFixture.ScenePath));
            SessionState.SetBool(Saved + "active", true);
            Application.runInBackground = true;
            var scene = EditorSceneManager.OpenScene(ExecuteAlwaysFixture.ScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // SetUp 未取得测试场景所有权时，不能关闭用户本来打开的 Stage 或 Play。
            if (!SessionState.GetBool(Saved + "active", false)) yield break;
            StageUtility.GoToMainStage();
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            var scene = SceneManager.GetSceneByPath(ExecuteAlwaysFixture.ScenePath);
            if (scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            // UTF 的 bootstrap 可能尚未保存；先使用句柄，跨域重载后再回退到路径。
            int previousHandle = SessionState.GetInt(Saved + "sceneHandle", -1);
            var previous = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt).FirstOrDefault(x => x.handle == previousHandle);
            if (!previous.isLoaded)
                previous = SceneManager.GetSceneByPath(SessionState.GetString(Saved + "scene", ""));
            if (previous.isLoaded) SceneManager.SetActiveScene(previous);
            ApplyOptions((EnterPlayModeOptions)SessionState.GetInt(Saved + "options", 0));
            Application.runInBackground = SessionState.GetBool(Saved + "background", false);
            if (!SessionState.GetBool(Saved + "dirty", false)) EditorUtility.ClearDirty(SettingsAsset());
            SessionState.SetBool(Saved + "active", false);
            Assert.IsNull(ExecuteAlwaysCaseCounter.RuntimeOwner);
            Assert.AreEqual(SessionState.GetString(Saved + "prefabHash", ""), Hash(ExecuteAlwaysFixture.PrefabPath));
            Assert.AreEqual(SessionState.GetString(Saved + "sceneHash", ""), Hash(ExecuteAlwaysFixture.ScenePath));
        }

        [UnityTest, Order(1), Category("Scenario.EditWorld")]
        [Description("EA-01: Actual editor Update and callbacks execute without gameplay writes.")]
        public IEnumerator EditorPreviewDoesNotExecuteGameplay()
        {
            var item = SceneCounter();
            int score = ExecuteAlwaysCaseCounter.GameplayScore;
            yield return WaitFor(() => item.PreviewUpdates > 0);
            int before = item.PreviewSignals;
            Debug.Log(ExecuteAlwaysCaseCounter.PulseMessage);
            Assert.AreEqual(before + 1, item.PreviewSignals);
            Assert.AreEqual(0, item.RuntimeUpdates);
            Assert.AreEqual(0, item.RuntimeSignals);
            Assert.AreEqual(score, ExecuteAlwaysCaseCounter.GameplayScore);
            Assert.AreEqual(42, item.AuthoredValue);
            Assert.IsFalse(Application.IsPlaying(item.gameObject));
            TestContext.WriteLine("EA-01: editor Update observed, one preview pulse, zero gameplay writes.");
        }

        [UnityTest, Order(2), Category("Scenario.EventOwnership")]
        [Description("EA-02: Repeated enable/disable and destruction balance real Application event subscriptions.")]
        public IEnumerator EnableDisableAndDestroyBalanceSubscriptions()
        {
            var item = SceneCounter();
            yield return null; // 订阅在真实 OnEnable 中建立，不依赖编辑态 Update 的刷新频率。
            for (int cycle = 1; cycle <= 3; cycle++)
            {
                item.enabled = false;
                int before = ExecuteAlwaysCaseCounter.PreviewSignalsTotal;
                Debug.Log(ExecuteAlwaysCaseCounter.PulseMessage);
                Assert.AreEqual(before, ExecuteAlwaysCaseCounter.PreviewSignalsTotal);
                item.enabled = true;
                before = ExecuteAlwaysCaseCounter.PreviewSignalsTotal;
                Debug.Log(ExecuteAlwaysCaseCounter.PulseMessage);
                Assert.AreEqual(before + 1, ExecuteAlwaysCaseCounter.PreviewSignalsTotal);
            }
            Object.DestroyImmediate(item.gameObject);
            int afterDestroy = ExecuteAlwaysCaseCounter.PreviewSignalsTotal;
            Debug.Log(ExecuteAlwaysCaseCounter.PulseMessage);
            Assert.AreEqual(afterDestroy, ExecuteAlwaysCaseCounter.PreviewSignalsTotal);
            TestContext.WriteLine("EA-02: 3 enable/disable cycles each respond 1/0 times; destroyed instance responds 0 times.");
        }

        [UnityTest, Order(3), Category("Scenario.RepeatedPlay")]
        [Description("EA-03: Editing after exit can contaminate statics; entry reset restores gameplay defaults for 3 Fast Plays.")]
        public IEnumerator EditPollutionIsResetAcrossThreeFastPlays()
        {
            ApplyOptions(EnterPlayModeOptions.DisableDomainReload);
            for (int cycle = 1; cycle <= 3; cycle++)
            {
                var editItem = SceneCounter();
                int pollution = 70 + cycle;
                editItem.RequestLegacyEditWrite(pollution);
                yield return WaitFor(() => !editItem.HasPendingLegacyWrite);
                Assert.AreEqual(pollution, ExecuteAlwaysCaseCounter.GameplayScore);
                Assert.AreEqual(pollution, ExecuteAlwaysReproState.Score);
                int previewTotal = ExecuteAlwaysCaseCounter.PreviewSignalsTotal;

                yield return new EnterPlayMode(false);
                Application.runInBackground = true;
                var playItem = SceneCounter();
                int runtimeUpdates = playItem.RuntimeUpdates;
                yield return WaitFor(() => playItem.RuntimeUpdates > runtimeUpdates);
                Assert.AreEqual(ExecuteAlwaysCaseCounter.DefaultScore, ExecuteAlwaysCaseCounter.GameplayScore);
                Assert.AreEqual(pollution, ExecuteAlwaysReproState.Score, "复现组保留真实编辑态污染。");
                Assert.AreEqual(previewTotal, ExecuteAlwaysCaseCounter.PreviewSignalsTotal, "进入 Play 不能清空编辑态拥有的诊断值。");
                int pulses = playItem.RuntimeSignals;
                Debug.Log(ExecuteAlwaysCaseCounter.PulseMessage);
                Assert.AreEqual(pulses + 1, playItem.RuntimeSignals);
                Assert.AreEqual(8, ExecuteAlwaysCaseCounter.GameplayScore, "每个探针只能计分一次。");
                TestContext.WriteLine("EA-03 cycle=" + cycle + ", editor pollution=" + pollution + ", guarded=7, repro=" + ExecuteAlwaysReproState.Score + ", after pulse=8");
                yield return new ExitPlayMode();
                Assert.IsNull(ExecuteAlwaysCaseCounter.RuntimeOwner);
                var restored = SceneCounter();
                int restoredUpdates = restored.PreviewUpdates;
                yield return WaitFor(() => restored.PreviewUpdates > restoredUpdates);
                int scoreAfterExit = ExecuteAlwaysCaseCounter.GameplayScore;
                int previewPulses = restored.PreviewSignals;
                Debug.Log(ExecuteAlwaysCaseCounter.PulseMessage);
                Assert.AreEqual(scoreAfterExit, ExecuteAlwaysCaseCounter.GameplayScore);
                Assert.AreEqual(previewPulses + 1, restored.PreviewSignals, "退出后编辑态订阅仍应正常工作。");
            }
        }

        [UnityTest, Order(4), Category("Scenario.PrefabWorld")]
        [Description("EA-04: While Play is running, Prefab Stage remains a non-playing world and cannot own gameplay state.")]
        public IEnumerator PrefabStageCannotMutatePlayingWorld()
        {
            ApplyOptions(EnterPlayModeOptions.DisableDomainReload);
            yield return new EnterPlayMode(false);
            Application.runInBackground = true;
            var runtime = SceneCounter();
            yield return WaitFor(() => runtime.RuntimeUpdates > 0);
            var stage = PrefabStageUtility.OpenPrefab(ExecuteAlwaysFixture.PrefabPath);
            var preview = stage.prefabContentsRoot.GetComponent<ExecuteAlwaysCaseCounter>();
            yield return WaitFor(() => preview.PreviewUpdates > 0);
            Assert.IsTrue(Application.isPlaying);
            Assert.IsTrue(Application.IsPlaying(runtime.gameObject));
            Assert.IsFalse(Application.IsPlaying(preview.gameObject));
            Assert.IsFalse(stage.scene.isDirty, "只读诊断不应弄脏 Prefab Stage。");
            int runtimePulses = runtime.RuntimeSignals;
            int previewPulses = preview.PreviewSignals;
            int score = ExecuteAlwaysCaseCounter.GameplayScore;
            Debug.Log(ExecuteAlwaysCaseCounter.PulseMessage);
            Assert.AreEqual(runtimePulses + 1, runtime.RuntimeSignals);
            Assert.AreEqual(previewPulses + 1, preview.PreviewSignals);
            Assert.AreEqual(0, preview.RuntimeSignals);
            Assert.AreEqual(0, preview.RuntimeUpdates);
            Assert.AreEqual(score + 1, ExecuteAlwaysCaseCounter.GameplayScore);
            Assert.AreSame(runtime, ExecuteAlwaysCaseCounter.RuntimeOwner);
            Assert.AreEqual(42, preview.AuthoredValue);
            Assert.AreEqual(Vector3.one, preview.transform.localScale);
            Assert.IsFalse(stage.scene.isDirty);
            StageUtility.GoToMainStage();
            int previewTotal = ExecuteAlwaysCaseCounter.PreviewSignalsTotal;
            runtimePulses = runtime.RuntimeSignals;
            Debug.Log(ExecuteAlwaysCaseCounter.PulseMessage);
            Assert.AreEqual(runtimePulses + 1, runtime.RuntimeSignals);
            Assert.AreEqual(previewTotal, ExecuteAlwaysCaseCounter.PreviewSignalsTotal, "关闭 Stage 后旧预览实例必须退订。");
            TestContext.WriteLine("EA-04: Application.isPlaying=True; IsPlaying(runtime)=True, IsPlaying(prefab)=False; runtime/preview pulse=1/1, prefab gameplay=0; prefab clean.");
            yield return new ExitPlayMode();
        }

        [UnityTest, Order(5), Category("Scenario.ReloadBaseline")]
        [Description("EA-05: Normal Domain Reload clears editor-written statics without relying on Fast Play reset.")]
        public IEnumerator NormalDomainReloadProvidesCleanBaseline()
        {
            ApplyOptions(EnterPlayModeOptions.None);
            var item = SceneCounter();
            item.RequestLegacyEditWrite(999);
            yield return WaitFor(() => !item.HasPendingLegacyWrite);
            yield return new EnterPlayMode(true);
            Application.runInBackground = true;
            Assert.AreEqual(7, ExecuteAlwaysCaseCounter.GameplayScore);
            Assert.AreEqual(7, ExecuteAlwaysReproState.Score);
            Assert.AreEqual(42, SceneCounter().AuthoredValue);
            TestContext.WriteLine("EA-05: normal Domain Reload restores guarded and unguarded defaults to 7.");
            yield return new ExitPlayMode();
        }

        static ExecuteAlwaysCaseCounter SceneCounter() => SceneManager.GetSceneByPath(ExecuteAlwaysFixture.ScenePath)
            .GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<ExecuteAlwaysCaseCounter>()).Single();

        static IEnumerator WaitFor(Func<bool> predicate)
        {
            double deadline = EditorApplication.timeSinceStartup + 10;
            while (!predicate() && EditorApplication.timeSinceStartup < deadline)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
                yield return null;
            }
            Assert.IsTrue(predicate(), "Unity 回调没有在 10 秒内产生预期观测；没有手工调用 Update。 " +
                "playing=" + Application.isPlaying + ", focused=" + Application.isFocused +
                ", editorActive=" + UnityEditorInternal.InternalEditorUtility.isApplicationActive +
                ", paused=" + EditorApplication.isPaused + ", background=" + Application.runInBackground);
        }

        static void ApplyOptions(EnterPlayModeOptions options)
        {
            EditorSettings.enterPlayModeOptions = options;
            EditorSettings.enterPlayModeOptionsEnabled = options != EnterPlayModeOptions.None;
        }

        static Object SettingsAsset() => AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/EditorSettings.asset")[0];

        static string Hash(string path)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path)));
        }
    }
}
