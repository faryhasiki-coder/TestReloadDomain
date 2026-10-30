using System;
using System.Collections.Generic;
using System.Reflection;
using Cysharp.Threading.Tasks;
using EasyReloadGuard.Editor;
using UnityEditor;

namespace ReloadGuardProjectAdapters.Editor
{
    /// <summary>
    /// 本测试工程的临时适配。UniTask 2.5.11 的 PlayerLoopHelper.InsertRunner 每次初始化都向
    /// EditorApplication.playModeStateChanged 追加匿名处理器且从不移除，关闭 Domain Reload 时每次进入 Play 累积一组。
    /// 本适配器在进入 Play 的重置阶段（SubsystemRegistration，早于 UniTask 在 AfterAssembliesLoaded 的重新初始化）
    /// 用反射移除上一次初始化留下的处理器：它们已在此前的 ExitingEditMode 中清空旧队列，随后 Init 会登记本次会话的处理器。
    /// 正式落地改为在 UniTask 源码中修复。这里依赖 Unity 6000.3 的事件存储与 UniTask 的闭包结构，不保证跨版本；
    /// 结构不符时本步骤失败，按“重置失败后强制重载”设置兜底。
    /// </summary>
    internal sealed class UniTaskEditorCallbackAdapter : ReloadAdapter
    {
        public override string Target => "com.cysharp.unitask";

        public override string[] VerifiedVersions => new[] { "2.5.11" };

        public override void Configure(ReloadAdapterBuilder builder)
        {
            builder.SelfHandled("2.5.11 包内静态字段经逐条复核不影响 Play 运行逻辑：缓存委托只赋值一次、Promise 对象池只回收已重置的对象、" +
                                "Init 在关闭 Domain Reload 时重建 runner 与队列、UniTaskScheduler 为配置开关；" +
                                "唯一的残留（InsertRunner 每次初始化追加的 Editor 回调）由本适配器移除。" +
                                "业务订阅 UnobservedTaskException 及跨会话的线程池工作仍由业务 owner 解除或取消。");
            builder.OnReset("移除 UniTask 上一次初始化留下的 playModeStateChanged 处理器", RemovePreviousHandlers);
        }

        static void RemovePreviousHandlers()
        {
            foreach (var handler in FindPlayerLoopHelperHandlers())
                EditorApplication.playModeStateChanged -= handler;
        }

        /// <summary>
        /// 读取 EditorApplication.m_PlayModeStateChangedEvent.m_Delegate（length / GetDelegateAt），返回 PlayerLoopHelper 登记的处理器。
        /// </summary>
        static List<Action<PlayModeStateChange>> FindPlayerLoopHelperHandlers()
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags staticField = BindingFlags.Static | BindingFlags.NonPublic;
            var trackerField = Required(typeof(EditorApplication).GetField("m_PlayModeStateChangedEvent", staticField),
                "EditorApplication.m_PlayModeStateChangedEvent");
            var tracker = trackerField.GetValue(null);
            var handles = Required(tracker.GetType().GetField("m_Delegate", instance), "m_Delegate").GetValue(tracker);

            var result = new List<Action<PlayModeStateChange>>();
            if (handles == null) return result;
            var length = Required(handles.GetType().GetProperty("length", instance), "length");
            var getAt = Required(handles.GetType().GetMethod("GetDelegateAt", instance), "GetDelegateAt");
            for (var i = 0; i < (int)length.GetValue(handles); i++)
            {
                if (!(getAt.Invoke(handles, new object[] { i }) is Delegate entry)) continue;
                foreach (var single in entry.GetInvocationList())
                {
                    if (single is Action<PlayModeStateChange> handler && DeclaredIn(handler.Method.DeclaringType, typeof(PlayerLoopHelper)))
                        result.Add(handler);
                }
            }

            return result;
        }

        static bool DeclaredIn(Type type, Type owner)
        {
            for (; type != null; type = type.DeclaringType)
            {
                if (type == owner) return true;
            }

            return false;
        }

        static T Required<T>(T member, string name) where T : MemberInfo =>
            member ?? throw new InvalidOperationException($"Unity 事件存储结构已变化，找不到 {name}，需复核本临时适配器");
    }
}
