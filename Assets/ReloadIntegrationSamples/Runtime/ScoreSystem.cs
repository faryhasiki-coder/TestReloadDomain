using System;
using UnityEngine;

namespace ReloadIntegrationSamples
{
    /// <summary>计分系统。场景入口在开局时调用 Initialize，之后通过事件总线接收击杀消息。</summary>
    public static class ScoreSystem
    {
        [field: EasyReloadGuard.ResetStatic]
        public static event Action<int> ScoreChanged;
        [field: EasyReloadGuard.ResetStatic]
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

        /// <summary>退出计分流程时解除击杀订阅，可重复调用；重新开局前调用 Initialize。</summary>
        [EasyReloadGuard.OnExitPlayMode]
        public static void Shutdown()
        {
            EventBus<EnemyKilled>.Unsubscribe(OnEnemyKilled);
            s_Initialized = false;
            Score = 0;
            s_Multiplier = 1f;
        }

#if UNITY_EDITOR
        [EasyReloadGuard.ResetMethod]
        static void ResetForPlay()
        {
            ScoreChanged = null;
            Score = 0;
            s_Multiplier = 1f;
            s_Initialized = false;
        }
#endif

        static void OnEnemyKilled(EnemyKilled message)
        {
            Score += Mathf.RoundToInt(message.Points * s_Multiplier);
            ScoreChanged?.Invoke(Score);
        }
    }
}
