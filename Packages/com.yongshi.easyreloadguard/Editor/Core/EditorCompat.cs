using System;
using System.Collections.Generic;
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
    }
}
