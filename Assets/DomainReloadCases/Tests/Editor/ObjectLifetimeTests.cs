using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    /// <summary>
    /// OBJ-02/03 的 R1 代表性用例。临时 Prefab/场景只在未来显式运行测试时创建，
    /// 存入唯一 GeneratedTestAssets 子目录；不保存、替换或删除用户场景。
    /// </summary>
    public sealed class ObjectLifetimeTests : ReloadCaseTestBase
    {
        string m_Folder;
        string m_ScenePath;
        Scene m_AuthoringScene;
        Scene m_OriginalActiveScene;

        [UnityTearDown]
        public IEnumerator RemoveOwnedFixtures()
        {
            if (Application.isPlaying) yield return LeavePlay();
            if (!string.IsNullOrEmpty(m_ScenePath))
            {
                var loaded = SceneManager.GetSceneByPath(m_ScenePath);
                if (loaded.IsValid() && loaded.isLoaded) EditorSceneManager.CloseScene(loaded, true);
            }
            // 尚未保存就断言失败的纯 EditMode 路径也需要关闭自己的场景。
            if (m_AuthoringScene.IsValid() && m_AuthoringScene.isLoaded)
                EditorSceneManager.CloseScene(m_AuthoringScene, true);
            if (m_OriginalActiveScene.IsValid() && m_OriginalActiveScene.isLoaded)
                SceneManager.SetActiveScene(m_OriginalActiveScene);
            if (!string.IsNullOrEmpty(m_Folder) && AssetDatabase.IsValidFolder(m_Folder))
                Assert.IsTrue(AssetDatabase.DeleteAsset(m_Folder), "只删除此用例的唯一目录及其 .meta");
            m_Folder = null;
            m_ScenePath = null;
        }

        [Test, Description("OBJ-02：纯 EditMode 序列化字段结构对照，不声称验证跨 Play 或热重载")]
        public void OBJ02_SerializedObjectDistinguishesFieldsFromRuntimeOnlyState()
        {
            BeginAuthoringScene();
            try
            {
                var probe = CreateProbe("FieldSchema", true, true);
                using (var serialized = new SerializedObject(probe))
                {
                    Assert.IsNotNull(serialized.FindProperty(nameof(ObjectLifetimeProbe.PublicSerialized)));
                    Assert.IsNotNull(serialized.FindProperty("m_PrivateSerialized"));
                    Assert.IsNotNull(serialized.FindProperty(nameof(ObjectLifetimeProbe.SerializedNumbers)));
                    Assert.IsNull(serialized.FindProperty(nameof(ObjectLifetimeProbe.NonSerializedValue)));
                    Assert.IsNull(serialized.FindProperty("<AutoProperty>k__BackingField"));
                    Assert.IsNull(serialized.FindProperty(nameof(ObjectLifetimeProbe.NullableValue)));
                }
            }
            finally { RestoreOriginalActiveScene(); }
        }

        [UnityTest, Description("OBJ-02：Prefab 作者配置与 NonSerialized/属性/nullable 初值，R1 三轮")]
        public IEnumerator OBJ02_PrefabInstancesRestoreSerializedConfigurationAndRuntimeInitializers()
        {
            BeginAuthoringScene();
            var prefabPath = m_Folder + "/FieldMatrix.prefab";
            try
            {
                var authored = CreateProbe("AuthoredFieldMatrix", true, true);
                authored.PublicSerialized = 11;
                authored.PrivateSerialized = 22;
                authored.SerializedNumbers.Clear();
                authored.SerializedNumbers.AddRange(new[] { 3, 5, 8 });
                authored.NonSerializedValue = 930;
                authored.AutoProperty = 940;
                authored.NullableValue = 960;
                Assert.IsNotNull(PrefabUtility.SaveAsPrefabAsset(authored.gameObject, prefabPath));
                SaveAuthoringScene();
            }
            finally { RestoreOriginalActiveScene(); }

            ObjectLifetimeProbe previous = null;
            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return EnterFastPlay();
                if (cycle > 0) Assert.IsTrue(previous == null, "上一局的运行实例已经销毁");
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                Assert.IsNotNull(prefab);
                var current = UnityEngine.Object.Instantiate(prefab).GetComponent<ObjectLifetimeProbe>();
                yield return WaitUntil(() => current.StartCalls > 0);
                Assert.AreEqual(11, current.PublicSerialized);
                Assert.AreEqual(22, current.PrivateSerialized);
                CollectionAssert.AreEqual(new[] { 3, 5, 8 }, current.SerializedNumbers);
                Assert.AreEqual(30, current.NonSerializedValue, "NonSerialized 不从作者临时对象复制");
                Assert.AreEqual(40, current.AutoProperty, "未声明 field:SerializeField 的属性使用新对象初值");
                Assert.AreEqual(60, current.NullableValue, "nullable 不属于本例场景序列化字段");
                Assert.AreEqual(1, current.StartCalls);
                current.MutateAllFields(1000 + cycle);
                Assert.AreEqual(1000 + cycle, current.PrivateSerialized);
                Assert.AreEqual(4, current.SerializedNumbers.Count);
                previous = current;
                yield return LeavePlay();
                Assert.IsTrue(current == null);
            }
        }

        [UnityTest, Description("OBJ-03：预存 active/inactive/disabled/从未激活对象及动态对象，R1 三轮")]
        public IEnumerator OBJ03_SceneAndDynamicObjectLifetimesHaveObservableCallbacks()
        {
            BeginAuthoringScene();
            try
            {
                CreateProbe("AuthoredActive", true, true);
                CreateProbe("AuthoredInactive", false, true);
                CreateProbe("AuthoredDisabled", true, false);
                CreateProbe("NeverActivated", false, true);
                SaveAuthoringScene();
            }
            finally { RestoreOriginalActiveScene(); }

            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return EnterFastPlay();
                var authoredScene = SceneManager.GetSceneByPath(m_ScenePath);
                Assert.IsTrue(authoredScene.IsValid() && authoredScene.isLoaded);
                var active = FindRootProbe(authoredScene, "AuthoredActive");
                var inactive = FindRootProbe(authoredScene, "AuthoredInactive");
                var disabled = FindRootProbe(authoredScene, "AuthoredDisabled");
                var neverActivated = FindRootProbe(authoredScene, "NeverActivated");
                yield return WaitUntil(() => active.StartCalls > 0);
                Assert.GreaterOrEqual(active.AwakeCalls, 1);
                Assert.GreaterOrEqual(active.EnableCalls, 1);
                Assert.AreEqual(1, active.StartCalls);
                Assert.AreEqual(0, inactive.StartCalls);
                Assert.AreEqual(0, disabled.StartCalls);
                Assert.AreEqual(0, neverActivated.StartCalls);
                Assert.IsFalse(neverActivated.gameObject.activeSelf);

                inactive.gameObject.SetActive(true);
                disabled.enabled = true;
                yield return WaitUntil(() => inactive.StartCalls > 0 && disabled.StartCalls > 0);
                Assert.AreEqual(1, inactive.StartCalls);
                Assert.AreEqual(1, disabled.StartCalls);
                Assert.Less(active.Lifecycle.IndexOf("Awake"), active.Lifecycle.IndexOf("Start"));
                var disableBefore = active.DisableCalls;
                var enableBefore = active.EnableCalls;
                active.enabled = false;
                Assert.AreEqual(disableBefore + 1, active.DisableCalls);
                active.enabled = true;
                Assert.AreEqual(enableBefore + 1, active.EnableCalls);
                Assert.AreEqual(1, active.StartCalls, "反复启停组件不会重复 Start");

                var dynamicProbe = new GameObject("OwnedDynamicProbe").AddComponent<ObjectLifetimeProbe>();
                yield return WaitUntil(() => dynamicProbe.StartCalls > 0);
                Assert.AreEqual(1, dynamicProbe.StartCalls);
                UnityEngine.Object.Destroy(dynamicProbe.gameObject);
                yield return null;
                Assert.IsTrue(dynamicProbe == null);
                Assert.AreEqual(1, dynamicProbe.DestroyCalls);
                yield return LeavePlay();

                Assert.IsTrue(active == null && inactive == null && disabled == null,
                    "Play 中的实例已退出，Edit 场景恢复的是另一组实例");
                Assert.IsTrue(neverActivated == null, "从未激活的对象也必须退出本轮所有权范围");
                // 从未激活对象不保证收到 OnDestroy，不能把该回调当成唯一资源清理入口。
                Assert.IsFalse(ReferenceEquals(neverActivated, null), "保留托管壳以区别 Unity 判空");
            }
        }

        void BeginAuthoringScene()
        {
            const string parent = "Assets/DomainReloadCases/GeneratedTestAssets";
            if (!AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder("Assets/DomainReloadCases", "GeneratedTestAssets");
            var leaf = "ObjectLifetime_" + Guid.NewGuid().ToString("N");
            m_Folder = parent + "/" + leaf;
            Assert.IsNotEmpty(AssetDatabase.CreateFolder(parent, leaf));
            m_ScenePath = m_Folder + "/Fixture.unity";
            m_OriginalActiveScene = SceneManager.GetActiveScene();
            m_AuthoringScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            SceneManager.SetActiveScene(m_AuthoringScene);
        }

        void SaveAuthoringScene() => Assert.IsTrue(EditorSceneManager.SaveScene(m_AuthoringScene, m_ScenePath));

        void RestoreOriginalActiveScene()
        {
            if (m_OriginalActiveScene.IsValid() && m_OriginalActiveScene.isLoaded)
                SceneManager.SetActiveScene(m_OriginalActiveScene);
        }

        static ObjectLifetimeProbe CreateProbe(string name, bool active, bool enabled)
        {
            var go = new GameObject(name);
            go.SetActive(active);
            var probe = go.AddComponent<ObjectLifetimeProbe>();
            probe.enabled = enabled;
            return probe;
        }

        static ObjectLifetimeProbe FindRootProbe(Scene scene, string name)
        {
            var root = scene.GetRootGameObjects().Single(go => go.name == name);
            var probe = root.GetComponent<ObjectLifetimeProbe>();
            Assert.IsNotNull(probe);
            return probe;
        }
    }
}
