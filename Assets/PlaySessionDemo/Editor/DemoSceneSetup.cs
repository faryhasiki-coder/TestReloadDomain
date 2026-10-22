using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PlaySessionDemo.Editor
{
    public static class DemoSceneSetup
    {
        public const string ScenePath = "Assets/PlaySessionDemo/Scenes/PlaySessionDemo.unity";
        public const string ComparisonScenePath = "Assets/PlaySessionDemo/Scenes/CoinCodeComparison.unity";

        [MenuItem("Tools/Play Sessions/Demo/Open Demo Scene")]
        public static void Open() => OpenScene(ScenePath, "Play Session Demo", typeof(DemoView));

        [MenuItem("Tools/Play Sessions/Demo/Open Coin Code Comparison")]
        public static void OpenComparison() => OpenScene(ComparisonScenePath, "Coin Code Comparison", typeof(CodeComparison.CoinComparisonView));

        private static void OpenScene(string path, string objectName, Type viewType)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the currently modified scenes before opening the demo.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
            {
                EditorSceneManager.OpenScene(path);
                return;
            }
            if (!AssetDatabase.IsValidFolder("Assets/PlaySessionDemo/Scenes"))
                AssetDatabase.CreateFolder("Assets/PlaySessionDemo", "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject(objectName).AddComponent(viewType);
            if (Camera.main != null) Camera.main.backgroundColor = new Color(0.045f, 0.065f, 0.10f);
            if (!EditorSceneManager.SaveScene(scene, path)) throw new InvalidOperationException("Failed to save the demo scene.");
        }
    }
}
