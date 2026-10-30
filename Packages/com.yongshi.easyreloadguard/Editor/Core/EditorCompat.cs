using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 集中所有依赖 Unity 版本语义的编辑器调用。升级 Unity 时优先复核本文件与 README 的升级清单。
    /// </summary>
    internal static class EditorCompat
    {
        public static bool DomainReloadDisabled =>
            EditorSettings.enterPlayModeOptionsEnabled &&
            (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;

        public static bool SceneReloadDisabled =>
            EditorSettings.enterPlayModeOptionsEnabled &&
            (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0;

        public static string DescribePlayModeOptions() =>
            $"Domain Reload={(DomainReloadDisabled ? "关闭" : "开启")}，Scene Reload={(SceneReloadDisabled ? "关闭" : "开启")}";

        /// <summary>在 ExitingEditMode 回调中取消本次进入 Play。</summary>
        public static void CancelEnterPlayMode() => EditorApplication.isPlaying = false;

        /// <summary>请求在下一帧重载脚本域（不编译代码）。</summary>
        public static void RequestDomainReload() => EditorUtility.RequestScriptReload();

        /// <summary>
        /// 在下一次编辑器 update 中执行一次。不用 delayCall：6000.3.1f1 实测编辑器在后台由外部工具驱动时，
        /// update 照常执行而 delayCall 不会被调用。
        /// </summary>
        public static void RunOnNextUpdate(Action action)
        {
            EditorApplication.CallbackFunction once = null;
            once = () =>
            {
                EditorApplication.update -= once;
                action();
            };
            EditorApplication.update += once;
        }

        public static Dictionary<string, string> GetRegisteredPackageVersions()
        {
            var versions = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
                versions[package.name] = package.version;
            return versions;
        }

        const BindingFlags k_Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// Unity 6 的部分编辑器事件（如 EditorApplication.playModeStateChanged、quitting、hierarchyChanged）不是委托字段，
        /// 而是 internal 结构 EventWithPerformanceTracker&lt;T&gt;。
        /// </summary>
        public static bool IsTrackedEventType(Type type) =>
            type.IsValueType && type.IsGenericType && type.Name.StartsWith("EventWithPerformanceTracker`", StringComparison.Ordinal);

        /// <summary>
        /// 读取 EventWithPerformanceTracker&lt;T&gt; 的订阅：m_Delegate（类）→ m_DelegateOrList（Entry 结构）→ Reference，
        /// 值为单个委托或 Entry[]。只读字段，不调用其枚举器（枚举会创建并启动性能追踪器）。结构与 6000.3 不符时返回 null。
        /// </summary>
        public static Delegate[] ReadTrackedEvent(object value)
        {
            if (value == null) return null;
            var holder = value.GetType().GetField("m_Delegate", k_Instance)?.GetValue(value);
            if (holder == null) return Array.Empty<Delegate>();
            var entry = holder.GetType().GetField("m_DelegateOrList", k_Instance)?.GetValue(holder);
            if (entry == null) return null;
            switch (ReferenceOf(entry))
            {
                case null:
                    return Array.Empty<Delegate>();
                case Delegate single:
                    return new[] { single };
                case Array entries:
                    var result = new List<Delegate>(entries.Length);
                    foreach (var item in entries)
                    {
                        if (!(ReferenceOf(item) is Delegate callback)) return null;
                        result.Add(callback);
                    }

                    return result.ToArray();
                default:
                    return null;
            }
        }

        static object ReferenceOf(object entry) => entry?.GetType().GetField("Reference", k_Instance)?.GetValue(entry);
    }
}
