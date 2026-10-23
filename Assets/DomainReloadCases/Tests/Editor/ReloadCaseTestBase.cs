using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    /// <summary>
    /// 仅在用户明确执行测试后运行。保留原设置，不启动编译、不修改包配置。
    /// 已有 InitialValue 用例要求从新脚本域开始；本基类不绕过框架的进入关口。
    /// </summary>
    public abstract class ReloadCaseTestBase
    {
        string StateKey => "DomainReloadCases.Environment." + GetType().FullName + ".";

        [SetUp]
        public void CaptureEnvironment()
        {
            if (SessionState.GetBool(StateKey + "owned", false)) return;
            Assert.IsFalse(EditorApplication.isPlaying, "从 EditMode 启动这组用例。");
            SessionState.SetInt(StateKey + "options", (int)(EditorSettings.enterPlayModeOptionsEnabled
                ? EditorSettings.enterPlayModeOptions : EnterPlayModeOptions.None));
            SessionState.SetBool(StateKey + "background", Application.runInBackground);
            SessionState.SetBool(StateKey + "dirty", EditorUtility.IsDirty(SettingsAsset()));
            SessionState.SetBool(StateKey + "owned", true);
        }

        [UnityTearDown]
        public IEnumerator RestoreEnvironment()
        {
            if (!SessionState.GetBool(StateKey + "owned", false)) yield break;
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            ApplyOptions((EnterPlayModeOptions)SessionState.GetInt(StateKey + "options", 0));
            Application.runInBackground = SessionState.GetBool(StateKey + "background", false);
            if (!SessionState.GetBool(StateKey + "dirty", false)) EditorUtility.ClearDirty(SettingsAsset());
            SessionState.EraseBool(StateKey + "owned");
        }

        // UTF 必须直接收到 IEditModeTestYieldInstruction 才会执行 Perform。
        // 不把 Enter/Exit 包在嵌套 IEnumerator 中；真正域重载也只恢复顶层测试枚举器的 PC。
        protected EnterPlayMode EnterFastPlay() => EnterWithOptions(EnterPlayModeOptions.DisableDomainReload);

        protected EnterPlayMode EnterWithOptions(EnterPlayModeOptions options)
        {
            Assert.IsFalse(EditorApplication.isPlaying);
            ApplyOptions(options);
            Application.runInBackground = true;
            return new EnterPlayMode((options & EnterPlayModeOptions.DisableDomainReload) == 0);
        }

        protected ExitPlayMode LeavePlay()
        {
            Assert.IsTrue(EditorApplication.isPlaying);
            return new ExitPlayMode();
        }

        protected static IEnumerator WaitUntil(Func<bool> predicate, double seconds = 5)
        {
            double deadline = EditorApplication.timeSinceStartup + seconds;
            while (!predicate() && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.IsTrue(predicate(), "在 " + seconds + " 秒内未观察到完成；不能将取消请求当作实际停止。");
        }

        static void ApplyOptions(EnterPlayModeOptions options)
        {
            EditorSettings.enterPlayModeOptions = options;
            EditorSettings.enterPlayModeOptionsEnabled = options != EnterPlayModeOptions.None;
        }

        static UnityEngine.Object SettingsAsset() =>
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/EditorSettings.asset")[0];
    }
}
