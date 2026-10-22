using System;
using System.Collections.Generic;
using System.Threading;
using EasyReloadGuard;
using UnityEngine;

namespace DomainReloadCases
{
    /// <summary>
    /// 修复案例：与 LeakyCaseCounter 同样的状态，用 Easy Reload Guard 的特性声明处理方式。
    /// 外部静态事件订阅由类型自己持有，退出 Play 时成对解除。
    /// </summary>
    public sealed class GuardedCaseCounter : MonoBehaviour
    {
        [ResetStatic] public static int CreatedCount;
        [ResetStatic(ResetMode.Clear)] public static readonly List<GuardedCaseCounter> Instances = new List<GuardedCaseCounter>();
        [ResetStatic(ResetMode.InitialValue)] public static string Label = "initial";
        [ResetStatic] public static int LogCallbackCount;
        [ResetStatic] public static CancellationToken LastToken;
        [ResetStatic] public static PlaySessionStamp LastStamp;
        [ResetStatic] static bool s_LogSubscribed;

        [field: ResetStatic]
        public static event Action Ticked;

        public static int TickedSubscriberCount => Ticked?.GetInvocationList().Length ?? 0;

        void Awake()
        {
            CreatedCount++;
            Instances.Add(this);
            Ticked += OnTicked;
            LastToken = PlaySession.Token;
            LastStamp = PlaySession.Current;
            if (s_LogSubscribed) return;
            Application.logMessageReceived += OnLog;
            s_LogSubscribed = true;
        }

        [OnExitPlayMode]
        static void ReleaseLogSubscription()
        {
            Application.logMessageReceived -= OnLog;
            s_LogSubscribed = false;
        }

        void OnTicked()
        {
        }

        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (condition == CaseProbe.Message) LogCallbackCount++;
        }
    }
}
