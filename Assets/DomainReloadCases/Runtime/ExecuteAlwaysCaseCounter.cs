using System;
using EasyReloadGuard;
using UnityEngine;

namespace DomainReloadCases
{
    /// <summary>编辑预览与运行计分共存的最小案例。所有生命周期回调由 Unity 驱动。</summary>
    [ExecuteAlways]
    public sealed class ExecuteAlwaysCaseCounter : MonoBehaviour
    {
        public const string PulseMessage = "[ExecuteAlwaysCase] pulse";
        public const int DefaultScore = 7;
        [SerializeField] int authoredValue = 42;

        public int AuthoredValue => authoredValue;
        public int PreviewUpdates { get; private set; }
        public int RuntimeUpdates { get; private set; }
        public int PreviewSignals { get; private set; }
        public int RuntimeSignals { get; private set; }

        public static int GameplayScore = DefaultScore;
        public static ExecuteAlwaysCaseCounter RuntimeOwner;
        [ReloadSafe("编辑预览的诊断计数跨 Play 保留；不是游戏存档或会话计分")]
        public static int PreviewSignalsTotal;

        bool subscribed;
        int? pendingLegacyWrite;

        void OnEnable()
        {
            if (subscribed) return;
            Application.logMessageReceived += OnPulse;
            subscribed = true;
        }

        void OnDisable()
        {
            if (subscribed) Application.logMessageReceived -= OnPulse;
            subscribed = false;
            if (ReferenceEquals(RuntimeOwner, this)) RuntimeOwner = null;
        }

        void Update()
        {
            if (Application.IsPlaying(gameObject))
            {
                RuntimeUpdates++;
                RuntimeOwner = this;
                return;
            }

            PreviewUpdates++;
            // 仅用于复现旧编辑工具的静态写入。测试请求一次，随后由真实编辑态 Update 执行。
            if (!pendingLegacyWrite.HasValue) return;
            GameplayScore = pendingLegacyWrite.Value;
            ExecuteAlwaysReproState.Score = pendingLegacyWrite.Value;
            pendingLegacyWrite = null;
        }

        public void RequestLegacyEditWrite(int value) => pendingLegacyWrite = value;
        public bool HasPendingLegacyWrite => pendingLegacyWrite.HasValue;

        void OnPulse(string message, string stackTrace, LogType type)
        {
            if (message != PulseMessage) return;
            // 全局 Application.isPlaying 无法排除 Play 中的 Prefab Stage 对象。
            if (Application.IsPlaying(gameObject))
            {
                RuntimeSignals++;
                GameplayScore++;
                RuntimeOwner = this;
            }
            else
            {
                PreviewSignals++;
                PreviewSignalsTotal++;
            }
        }

        [ResetMethod]
        static void ResetGameplay()
        {
            GameplayScore = DefaultScore;
            RuntimeOwner = null;
            // PreviewSignalsTotal 是编辑态拥有的状态，不能随 Play 重置。
        }

        [OnExitPlayMode]
        static void ReleaseRuntimeOwner() => RuntimeOwner = null;

        void OnGUI()
        {
            if (!Application.IsPlaying(gameObject)) return;
            GUILayout.BeginArea(new Rect(16, 16, 370, 150), GUI.skin.box);
            GUILayout.Label("ExecuteAlways: playing-world instance");
            GUILayout.Label("Gameplay score: " + GameplayScore + " / default: " + DefaultScore);
            GUILayout.Label("Runtime pulses: " + RuntimeSignals + " | updates: " + RuntimeUpdates);
            GUILayout.Label("Authored value (must remain 42): " + authoredValue);
            if (GUILayout.Button("Send one probe pulse")) Debug.Log(PulseMessage);
            GUILayout.EndArea();
        }
    }

    /// <summary>故意不重置的复现组；审计告警是此案例预期的未处理状态，不可冒充安全。</summary>
    public static class ExecuteAlwaysReproState
    {
        public static int Score = ExecuteAlwaysCaseCounter.DefaultScore;
    }
}
