using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace EasyReloadGuard
{
    /// <summary>
    /// Play 会话边界。每次进入 Play 生成新的 Id 与 Token；Editor 退出 Play、Player 退出应用时取消 Token。
    /// 关闭 Domain Reload 时用它阻止上一会话的异步结果写入当前会话。
    /// </summary>
    [ReloadSafe("会话编号需要跨 Play 递增；令牌由 Begin/End 管理")]
    public static class PlaySession
    {
        static readonly object s_Lock = new object();
        static int s_Id;
        static bool s_Active;
        static CancellationTokenSource s_Cts = CreateCancelledSource();

        /// <summary>当前会话编号；尚未进入过 Play 时为 0。</summary>
        public static int Id => s_Id;

        /// <summary>是否处于 Play 会话中。</summary>
        public static bool IsActive => s_Active;

        /// <summary>当前会话的取消令牌；会话之外返回已取消的令牌。</summary>
        public static CancellationToken Token => s_Cts.Token;

        /// <summary>捕获当前会话，异步回写前用 <see cref="PlaySessionStamp.IsAlive"/> 判断是否仍属当前会话。</summary>
        public static PlaySessionStamp Current => new PlaySessionStamp(s_Id);

        /// <summary>
        /// 登记一个在下次进入 Play 时执行的重置动作，执行后自动移除。
        /// 用于泛型单例等无法用特性静态描述的状态。Player 构建中调用点被编译器移除。
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void RegisterReset(Action reset)
        {
#if UNITY_EDITOR
            if (reset == null) throw new ArgumentNullException(nameof(reset));
            lock (s_Lock)
            {
                s_RegisteredResets.Add(reset);
            }
#endif
        }

        /// <summary>
        /// 登记一个在每次进入 Play 时重复执行的重置动作（执行后自动重新登记）。
        /// 适合 ScriptableObject 等 OnEnable 只调用一次的对象。Player 构建中调用点被编译器移除。
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void RegisterPersistentReset(Action reset)
        {
#if UNITY_EDITOR
            if (reset == null) throw new ArgumentNullException(nameof(reset));
            void ReRegister() => RegisterReset(() => { reset(); ReRegister(); });
            ReRegister();
#endif
        }

        /// <summary>
        /// 运行时检测到无法恢复的残留状态时调用：下次进入 Play 前强制重载域。Player 构建中调用点被移除。
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void RequestDomainReload(string reason)
        {
#if UNITY_EDITOR
            PendingReloadReason = string.IsNullOrWhiteSpace(reason) ? "未说明原因" : reason;
            UnityEngine.Debug.LogWarning($"[Easy Reload Guard] 运行时请求在下次进入 Play 前重载域：{PendingReloadReason}");
#endif
        }

        internal static void Begin()
        {
            End(); // 上次退出未被观察到时，先结束旧会话
            s_Id++;
            s_Cts = new CancellationTokenSource();
            s_Active = true;
        }

        internal static void End()
        {
            if (!s_Active) return;
            s_Active = false;
            // 不 Dispose：Token 在会话外仍需可读。取消回调的异常不能阻断后续会话切换
            try
            {
                s_Cts.Cancel();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("[Easy Reload Guard] PlaySession.Token 的取消回调抛出异常：");
                UnityEngine.Debug.LogException(e);
            }
        }

        static CancellationTokenSource CreateCancelledSource()
        {
            var source = new CancellationTokenSource();
            source.Cancel();
            return source;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnSubsystemRegistration()
        {
            Begin();
#if UNITY_EDITOR
            EditorEnterPlayHook?.Invoke();
#else
            Application.quitting -= End;
            Application.quitting += End;
#endif
        }

#if UNITY_EDITOR
        static readonly List<Action> s_RegisteredResets = new List<Action>();

        /// <summary>由 Editor 程序集设置，进入 Play 的 SubsystemRegistration 阶段调用。</summary>
        internal static Action EditorEnterPlayHook;

        internal static string PendingReloadReason;

        internal static List<Action> DrainRegisteredResets()
        {
            lock (s_Lock)
            {
                var drained = new List<Action>(s_RegisteredResets);
                s_RegisteredResets.Clear();
                return drained;
            }
        }
#endif
    }

    /// <summary>某一 Play 会话的标记。</summary>
    public readonly struct PlaySessionStamp
    {
        internal PlaySessionStamp(int id)
        {
            Id = id;
        }

        public int Id { get; }

        /// <summary>该会话仍是当前活跃会话。</summary>
        public bool IsAlive => PlaySession.IsActive && PlaySession.Id == Id;
    }
}
