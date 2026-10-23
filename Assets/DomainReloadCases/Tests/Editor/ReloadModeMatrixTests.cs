using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    /// <summary>DR-01/OBJ-04。对象字段是行为刻画，SessionValue 是明确的重置断言；不假定四组回调完全相同。</summary>
    [Category("Case.ReloadModeMatrix")]
    public sealed class ReloadModeMatrixTests : ReloadCaseTestBase
    {
        const string Key = "DomainReloadCases.ModeMatrix.";
        const string Root = "Assets/DomainReloadCases/GeneratedTestAssets/";

        [SetUp]
        public void CreateFixture()
        {
            if (SessionState.GetBool(Key + "owned", false)) return;
            Assert.IsNull(PrefabStageUtility.GetCurrentPrefabStage(), "先退出用户 Prefab 编辑，再执行该测试。");
            string directory = Root + "Modes-" + Guid.NewGuid().ToString("N");
            SessionState.SetString(Key + "directory", directory);
            SessionState.SetString(Key + "scene", directory + "/Modes.unity");
            SessionState.SetInt(Key + "previousHandle", SceneManager.GetActiveScene().handle);
            SessionState.SetString(Key + "previousPath", SceneManager.GetActiveScene().path);
            SessionState.SetInt(Key + "cycle", 0);
            SessionState.SetBool(Key + "owned", true);
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Add("Active", true, true);
            Add("Inactive", false, true);
            Add("Disabled", true, false);
            Assert.IsTrue(EditorSceneManager.SaveScene(scene, SessionState.GetString(Key + "scene", "")));
        }

        [UnityTearDown]
        public IEnumerator ReleaseFixture()
        {
            if (!SessionState.GetBool(Key + "owned", false)) yield break;
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            string path = SessionState.GetString(Key + "scene", "");
            var scene = SceneManager.GetSceneByPath(path);
            if (scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            int handle = SessionState.GetInt(Key + "previousHandle", -1);
            var previous = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
                .FirstOrDefault(x => x.handle == handle);
            if (!previous.isLoaded) previous = SceneManager.GetSceneByPath(SessionState.GetString(Key + "previousPath", ""));
            if (previous.isLoaded) SceneManager.SetActiveScene(previous);
            string directory = SessionState.GetString(Key + "directory", "");
            if (directory.StartsWith(Root + "Modes-", StringComparison.Ordinal))
                AssetDatabase.DeleteAsset(directory);
            SessionState.EraseBool(Key + "owned");
            SessionState.EraseInt(Key + "cycle");
        }

        [UnityTest, Description("DR-01 R0: Domain/Scene on; record identities and assert the session static reset.")]
        public IEnumerator R0_ReloadDomainAndScene()
        {
            return Observe(EnterPlayModeOptions.None);
        }

        [UnityTest, Description("DR-01 R1: Domain off/Scene on; three uninterrupted Play cycles.")]
        public IEnumerator R1_ReloadSceneOnly()
        {
            return Observe(EnterPlayModeOptions.DisableDomainReload);
        }

        [UnityTest, Explicit("R2 扩展模式；需单独选择，不能把 R1 通过自动推广为 Scene Reload 关闭支持。")]
        [Description("DR-01/OBJ-04 R2: Domain on/Scene off, separate instance-reuse characterization.")]
        public IEnumerator R2_ReloadDomainOnly()
        {
            return Observe(EnterPlayModeOptions.DisableSceneReload);
        }

        [UnityTest, Explicit("R3 扩展模式；需单独选择，并核对当前框架及第三方包对该组合的支持声明。")]
        [Description("DR-01/OBJ-04 R3: Domain/Scene off, separate instance-reuse characterization.")]
        public IEnumerator R3_ReloadNeither()
        {
            return Observe(EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload);
        }

        IEnumerator Observe(EnterPlayModeOptions options)
        {
            // SessionState survives real domain reloads; local loop counters do not.
            while (SessionState.GetInt(Key + "cycle", 0) < 3)
            {
                int beforeCycle = SessionState.GetInt(Key + "cycle", 0);
                var before = Find("Active");
                SessionState.SetString(Key + "domain", ReloadModeProbe.DomainIdentity);
                SessionState.SetInt(Key + "nativeId", before.GetInstanceID());
                SessionState.SetString(Key + "managedId", before.ManagedIdentity ?? "");
                ReloadModeProbe.SessionValue = 77;
                before.RuntimeValue = 100 + beforeCycle;
                before.PropertyValue = 200 + beforeCycle;
                yield return EnterWithOptions(options);

                int cycle = SessionState.GetInt(Key + "cycle", 0) + 1;
                SessionState.SetInt(Key + "cycle", cycle);
                var current = Find("Active");
                Assert.AreEqual(0, ReloadModeProbe.SessionValue, "域重载或声明式进入重置必须恢复会话静态值。");
                bool expectedDomainReload = (options & EnterPlayModeOptions.DisableDomainReload) == 0;
                bool domainChanged = SessionState.GetString(Key + "domain", "") != ReloadModeProbe.DomainIdentity;
                Assert.AreEqual(expectedDomainReload, domainChanged, "不得在关闭域重载的连续用例中悄悄换域。");
                Assert.IsFalse(Find("Inactive").gameObject.activeSelf);
                Assert.IsFalse(Find("Disabled").enabled);
                TestContext.WriteLine("DR-01 mode=" + options + ", cycle=" + cycle +
                    ", oldNative=" + SessionState.GetInt(Key + "nativeId", 0) + ", newNative=" + current.GetInstanceID() +
                    ", oldManaged=" + SessionState.GetString(Key + "managedId", "") + ", newManaged=" + current.ManagedIdentity +
                    ", serialized=" + current.SerializedValue + ", nonSerialized=" + current.RuntimeValue +
                    ", property=" + current.PropertyValue + ", Awake/Enable/Start=" +
                    current.AwakeCalls + "/" + current.EnableCalls + "/" + current.StartCalls);
                ReloadModeProbe.SessionValue = 99;
                yield return LeavePlay();
            }
        }

        static void Add(string name, bool active, bool enabled)
        {
            var item = new GameObject(name);
            item.SetActive(active);
            item.AddComponent<ReloadModeProbe>().enabled = enabled;
        }

        static ReloadModeProbe Find(string name) =>
            SceneManager.GetSceneByPath(SessionState.GetString(Key + "scene", "")).GetRootGameObjects()
                .Single(x => x.name == name).GetComponent<ReloadModeProbe>();
    }
}
