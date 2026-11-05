using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace EasyReloadGuard
{
    /// <summary>
    /// Editor 中关闭 Domain Reload 时的重置登记与重载请求入口。公开成员只有带 Conditional("UNITY_EDITOR") 的 void 命令，
    /// Player 构建中调用点连同实参被编译器移除；Play 计数、进入 Play 的钩子与登记表只存在于 Editor，
    /// Player 中本类型没有状态、启动入口或退出订阅，不参与业务的取消、回写与释放。
    /// </summary>
    [ReloadSafe("Play 计数需要一直累加；重置登记由登记方解除，或在登记方对象销毁后自动移除；只存在于 Editor")]
    public static class PlaySession
    {
        /// <summary>
        /// 登记一个在下次进入 Play 时执行的重置动作，执行后自动移除。
        /// 用于工程自有的 Editor 代码登记无法用特性静态描述的状态；业务代码优先使用已有成员上的特性或 Editor 适配器。
        /// Player 构建中调用点被编译器移除。
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void RegisterReset(Action reset)
        {
#if UNITY_EDITOR
            Resets.Add(reset);
#endif
        }

        /// <summary>
        /// 登记一个在每次进入 Play 时都执行的重置动作，直到 <see cref="UnregisterPersistentReset"/> 解除或登记方对象被销毁。
        /// 动作的目标（<see cref="Delegate.Target"/>）是 UnityEngine.Object 时，该对象销毁或卸载后自动解除；
        /// 同一目标与方法重复登记只保留一份；某次执行失败不影响登记。Player 构建中调用点被编译器移除。
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void RegisterPersistentReset(Action reset)
        {
#if UNITY_EDITOR
            Resets.AddPersistent(reset);
#endif
        }

        /// <summary>
        /// 解除 <see cref="RegisterPersistentReset"/> 的登记（按目标与方法匹配）。Player 构建中调用点被编译器移除。
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void UnregisterPersistentReset(Action reset)
        {
#if UNITY_EDITOR
            Resets.RemovePersistent(reset);
#endif
        }

        /// <summary>
        /// 检测到无法恢复的残留状态时调用：下次进入 Play 前强制重载域。Player 构建中调用点被移除。
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void RequestDomainReload(string reason)
        {
#if UNITY_EDITOR
            PendingReloadReason = string.IsNullOrWhiteSpace(reason) ? "未说明原因" : reason;
            UnityEngine.Debug.LogWarning($"[Easy Reload Guard] 运行时请求在下次进入 Play 前重新加载脚本：{PendingReloadReason}");
#endif
        }

#if UNITY_EDITOR
        static int s_Id;
        static bool s_Begun;

        /// <summary>本次脚本加载后的第几次 Play，仅用于 Editor 日志；尚未进入过 Play 时为 0。</summary>
        internal static int Id => s_Id;

        /// <summary>RegisterReset / RegisterPersistentReset 的登记表，进入 Play 时由 Editor 取出执行。</summary>
        internal static readonly ResetRegistry Resets = new ResetRegistry();

        /// <summary>由 Editor 程序集设置，进入 Play 的 SubsystemRegistration 阶段调用（重置的后备时机与失败处理）。</summary>
        internal static Action EditorEnterPlayHook;

        internal static string PendingReloadReason;

        /// <summary>开始一次 Play 的计数。Editor 在更早的时机执行重置时先调用；同一次 Play 只计一次。</summary>
        internal static void BeginPlay()
        {
            if (s_Begun) return;
            s_Begun = true;
            s_Id++;
        }

        /// <summary>允许下一次 Play 重新计数，由 Editor 在退出编辑模式前调用。</summary>
        internal static void EndPlay() => s_Begun = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnSubsystemRegistration()
        {
            BeginPlay();
            EditorEnterPlayHook?.Invoke();
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>
    /// 进入 Play 时执行的重置登记。一次性登记取出后即移除；持久登记一直保留，直到被解除或登记方对象被销毁，
    /// 某次执行成功与否不影响登记。
    /// </summary>
    internal sealed class ResetRegistry
    {
        readonly object m_Lock = new object();
        readonly List<Action> m_Once = new List<Action>();
        readonly List<Action> m_Persistent = new List<Action>();

        public int PersistentCount
        {
            get
            {
                lock (m_Lock) return m_Persistent.Count;
            }
        }

        public void Add(Action reset)
        {
            if (reset == null) throw new ArgumentNullException(nameof(reset));
            lock (m_Lock) m_Once.Add(reset);
        }

        public void AddPersistent(Action reset)
        {
            if (reset == null) throw new ArgumentNullException(nameof(reset));
            lock (m_Lock)
            {
                if (!m_Persistent.Contains(reset)) m_Persistent.Add(reset);
            }
        }

        public void RemovePersistent(Action reset)
        {
            if (reset == null) throw new ArgumentNullException(nameof(reset));
            lock (m_Lock) m_Persistent.Remove(reset);
        }

        /// <summary>取出本次进入 Play 要执行的动作：一次性登记（先进先出，随即移除）在前，持久登记在后。须在主线程调用。</summary>
        public List<Action> TakeForEnter()
        {
            lock (m_Lock)
            {
                m_Persistent.RemoveAll(IsOwnerDestroyed);
                var resets = new List<Action>(m_Once.Count + m_Persistent.Count);
                resets.AddRange(m_Once);
                resets.AddRange(m_Persistent);
                m_Once.Clear();
                return resets;
            }
        }

        // 目标是已销毁或已卸载的 UnityEngine.Object 时（== null 但引用仍在），登记方已经不存在
        static bool IsOwnerDestroyed(Action reset) => reset.Target is UnityEngine.Object owner && owner == null;
    }
#endif
}
