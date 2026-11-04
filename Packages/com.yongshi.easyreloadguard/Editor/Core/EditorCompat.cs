using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 集中所有依赖 Unity 版本语义的编辑器调用。升级 Unity 时优先复核本文件与 README 的升级清单。
    /// </summary>
    internal static class EditorCompat
    {
        // 6000.3.1f1 实测：EditorSettings.enterPlayModeOptionsEnabled 的读取值只由 options 推导，写入也不改序列化字段；
        // 进入 Play 时 Unity 实际按序列化字段 m_EnterPlayModeOptionsEnabled 决定。该字段为 false 时接口显示已关闭 Domain Reload，
        // 脚本却照样重新加载。因此“是否启用”以序列化字段为准，读不到时退回接口值。
        const string k_EditorSettingsPath = "ProjectSettings/EditorSettings.asset";
        const string k_OptionsEnabledProperty = "m_EnterPlayModeOptionsEnabled";
        static SerializedObject s_EditorSettings;

        /// <summary>Unity 进入 Play 时实际使用的“启用 Enter Play Mode 选项”开关。</summary>
        static bool OptionsEnabled
        {
            get
            {
                if (s_EditorSettings == null || s_EditorSettings.targetObject == null)
                {
                    var assets = AssetDatabase.LoadAllAssetsAtPath(k_EditorSettingsPath);
                    s_EditorSettings = assets.Length > 0 && assets[0] != null ? new SerializedObject(assets[0]) : null;
                }

                if (s_EditorSettings == null) return EditorSettings.enterPlayModeOptionsEnabled;
                // 实测 UpdateIfRequiredOrScript 看不到其他 SerializedObject 对该字段的修改，必须完整读取
                s_EditorSettings.Update();
                var property = s_EditorSettings.FindProperty(k_OptionsEnabledProperty);
                return property != null ? property.boolValue : EditorSettings.enterPlayModeOptionsEnabled;
            }
        }

        public static bool DomainReloadDisabled =>
            OptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;

        public static bool SceneReloadDisabled =>
            OptionsEnabled && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0;

        /// <summary>
        /// 接口显示已关闭 Domain Reload，但项目设置中的开关实际是关的，Unity 进入 Play 时仍会重新加载脚本。
        /// 通常由脚本只写了 EditorSettings 接口造成；脚本中应同时用 SerializedObject 写入 m_EnterPlayModeOptionsEnabled。
        /// </summary>
        public static bool PlayModeOptionsMismatch =>
            EditorSettings.enterPlayModeOptionsEnabled &&
            (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0 &&
            !DomainReloadDisabled;

        public const string MismatchHint =
            "设置不一致：EditorSettings 接口显示已关闭 Domain Reload，但 EditorSettings.asset 中的 m_EnterPlayModeOptionsEnabled 为 false，" +
            "进入 Play 时 Unity 仍会重新加载脚本。请在 Project Settings/Editor 中检查 Enter Play Mode 设置；用脚本切换时要同时写入这个字段";

        public static string DescribePlayModeOptions() =>
            $"Domain Reload={(DomainReloadDisabled ? "关闭" : "开启")}，Scene Reload={(SceneReloadDisabled ? "关闭" : "开启")}" +
            (PlayModeOptionsMismatch ? $"（{MismatchHint}）" : string.Empty);

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

        /// <summary>
        /// 显示主工具栏中的自定义元素（Overlay id 即 MainToolbarElement 的 path）。6000.3 中只有 Unity 自带元素默认显示，
        /// 第三方元素默认隐藏，且没有公开的默认显示选项。工具栏窗口尚未创建时返回 false。
        /// </summary>
        public static bool TryShowMainToolbarElement(string path)
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window.GetType().Name != "MainToolbarWindow" || !window.TryGetOverlay(path, out var overlay)) continue;
                overlay.displayed = true;
                return true;
            }

            return false;
        }

        /// <summary>已登记的包：package.json 版本，以及 Git 来源时 manifest 中的 tag（或分支、提交号）与实际解析的提交号。</summary>
        public static Dictionary<string, InstalledPackage> GetRegisteredPackages()
        {
            var packages = new Dictionary<string, InstalledPackage>(StringComparer.Ordinal);
            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages())
            {
                var git = package.source == UnityEditor.PackageManager.PackageSource.Git ? package.git : null;
                packages[package.name] = new InstalledPackage(package.version, package.source.ToString(), git?.revision, git?.hash);
            }

            return packages;
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
