using UnityEngine;

namespace ReloadIntegrationSamples
{
    /// <summary>分数显示，分数变化时刷新文本并做一次放大反馈。</summary>
    public sealed class ScoreLabel : MonoBehaviour
    {
        public string Text { get; private set; } = "0";

        void OnEnable() => ScoreSystem.ScoreChanged += Show;

        void Show(int score)
        {
            Text = score.ToString();
            transform.localScale = Vector3.one * 1.2f;
        }
    }
}
