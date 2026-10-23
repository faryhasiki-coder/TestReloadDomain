using System;
using System.Collections.Generic;
using UnityEngine;

namespace DomainReloadCases
{
    /// <summary>
    /// 复现案例：静态计数、静态集合、静态事件与 Application.logMessageReceived 订阅均不做处理。
    /// 关闭 Domain Reload 后这些状态跨 Play 会话累积。该类故意暴露问题，不要为它加入重置或清理逻辑。
    /// </summary>
    public sealed class LeakyCaseCounter : MonoBehaviour
    {
        public static int CreatedCount;
        public static readonly List<LeakyCaseCounter> Instances = new List<LeakyCaseCounter>();
        public static int LogCallbackCount;

        public static event Action Ticked;

        public static int TickedSubscriberCount => Ticked?.GetInvocationList().Length ?? 0;

        void Awake()
        {
            CreatedCount++;
            Instances.Add(this);
            Ticked += OnTicked;
            Application.logMessageReceived += OnLog;
        }

        void OnTicked()
        {
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (condition == CaseProbe.Message) LogCallbackCount++;
        }
    }
}
