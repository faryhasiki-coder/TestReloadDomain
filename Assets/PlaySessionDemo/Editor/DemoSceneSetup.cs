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

        [MenuItem("Tools/Play Sessions/Demo/Open Demo Scene")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play mode first.");
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save the currently modified scenes before opening the demo.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                EditorSceneManager.OpenScene(ScenePath);
                return;
            }
            if (!AssetDatabase.IsValidFolder("Assets/PlaySessionDemo/Scenes"))
                AssetDatabase.CreateFolder("Assets/PlaySessionDemo", "Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("Play Session Demo").AddComponent<DemoView>();
            if (Camera.main != null) Camera.main.backgroundColor = new Color(0.045f, 0.065f, 0.10f);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Failed to save the demo scene.");
        }
    }
}
