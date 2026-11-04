using UnityEngine;

namespace ReloadIntegrationSamples
{
    /// <summary>关卡计分板：编辑时在 Scene 视图预览含关卡奖励的得分，运行时从默认分开始累计。</summary>
    [ExecuteAlways]
    public sealed class ExecuteAlwaysCaseCounter : MonoBehaviour
    {
        public const int DefaultScore = 7;
        public static int GameplayScore = DefaultScore;

        [SerializeField] int previewBonus = 3;

        public int PreviewScore => DefaultScore + previewBonus;

        void Update()
        {
            if (Application.IsPlaying(gameObject)) return;
            GameplayScore = PreviewScore;
        }

        public void AddPoint() => GameplayScore++;

        void OnGUI()
        {
            if (!Application.IsPlaying(gameObject)) return;
            GUILayout.BeginArea(new Rect(16, 16, 260, 70), GUI.skin.box);
            GUILayout.Label("Score: " + GameplayScore + " (default " + DefaultScore + ")");
            if (GUILayout.Button("Add point")) AddPoint();
            GUILayout.EndArea();
        }
    }
}
