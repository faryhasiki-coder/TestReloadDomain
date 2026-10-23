using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DomainReloadCases.Tests
{
    public static class ExecuteAlwaysFixture
    {
        public const string Root = "Assets/DomainReloadCases/ExecuteAlways";
        public const string ScenePath = Root + "/ExecuteAlwaysCase.unity";
        public const string PrefabPath = Root + "/ExecuteAlwaysCase.prefab";

        // 显式生成一次资产；测试只读取，不覆盖 fixture 或用户场景。
        [MenuItem("Tools/Domain Reload Cases/Create ExecuteAlways Fixtures")]
        public static void CreateAssets()
        {
            if (File.Exists(ScenePath) || File.Exists(PrefabPath))
                throw new System.InvalidOperationException("ExecuteAlways fixtures already exist.");

            Directory.CreateDirectory(Root);
            AssetDatabase.Refresh();
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
                item.name = "ExecuteAlwaysCase";
                item.AddComponent<ExecuteAlwaysCaseCounter>();
                PrefabUtility.SaveAsPrefabAsset(item, PrefabPath);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally
            {
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
