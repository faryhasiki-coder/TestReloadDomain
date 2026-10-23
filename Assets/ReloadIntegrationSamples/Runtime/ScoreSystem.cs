using System;
using UnityEngine;

namespace ReloadIntegrationSamples
{
    /// <summary>计分系统。场景入口在开局时调用 Initialize，之后通过事件总线接收击杀消息。</summary>
    public static class ScoreSystem
    {
        public static event Action<int> ScoreChanged;
        public static int Score { get; private set; }

        static float s_Multiplier = 1f;
        static bool s_Initialized;

        /// <summary>开局调用，可重复调用。</summary>
        public static void Initialize()
        {
            if (s_Initialized) return;
            s_Initialized = true;
            EventBus<EnemyKilled>.Subscribe(OnEnemyKilled);
        }

        /// <summary>拾取双倍积分道具，本局剩余时间生效。</summary>
        public static void ActivateDoubleScore() => s_Multiplier = 2f;

        static void OnEnemyKilled(EnemyKilled message)
        {
            Score += Mathf.RoundToInt(message.Points * s_Multiplier);
            ScoreChanged?.Invoke(Score);
        }
    }
}
