using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ReloadIntegrationSamples.Tests
{
    /// <summary>
    /// 接入验收测试基类：Domain Reload 关闭、Scene Reload 开启，同一脚本域内连续 Play→Stop。
    /// 只通过样例的公开业务接口观察结果，不调用、不引用 Easy Reload Guard；
    /// 测试结束只恢复 Enter Play Mode 设置与后台运行选项，不重置样例状态。
    /// </summary>
    public abstract class IntegrationAcceptanceTestBase
    {
        protected const int Cycles = 3;

        static readonly string s_DomainId = Guid.NewGuid().ToString("N");

        string StateKey => "ReloadIntegrationSamples." + GetType().FullName + ".";

        [SetUp]
        public void CaptureEnvironment()
        {
            if (SessionState.GetBool(StateKey + "owned", false)) return;
            Assert.IsFalse(EditorApplication.isPlaying, "请在 Edit Mode 下运行接入验收测试。");
            SessionState.SetInt(StateKey + "options", (int)(EditorSettings.enterPlayModeOptionsEnabled
                ? EditorSettings.enterPlayModeOptions : EnterPlayModeOptions.None));
            SessionState.SetBool(StateKey + "background", Application.runInBackground);
            SessionState.SetBool(StateKey + "dirty", EditorUtility.IsDirty(SettingsAsset()));
            SessionState.SetString(StateKey + "domain", s_DomainId);
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

        /// <summary>直接 yield 返回值；UTF 只执行测试方法直接产出的 EnterPlayMode/ExitPlayMode。</summary>
        protected static EnterPlayMode EnterFastPlay()
        {
            Assert.IsFalse(EditorApplication.isPlaying);
            ApplyOptions(EnterPlayModeOptions.DisableDomainReload);
            Application.runInBackground = true;
            return new EnterPlayMode(false);
        }

        protected static ExitPlayMode LeavePlay()
        {
            Assert.IsTrue(EditorApplication.isPlaying);
            return new ExitPlayMode();
        }

        /// <summary>连续性前提：同一测试内不得换域（例如被强制重载），否则后续轮次不算连续 Fast Play。</summary>
        protected void AssertSameDomain(int cycle)
        {
            Assert.AreEqual(SessionState.GetString(StateKey + "domain", ""), s_DomainId,
                $"第 {cycle} 局进入 Play 时发生了域重载，连续 Fast Play 前提不成立；请在刚编译或刚重载的域中单独运行本组。");
        }

        /// <summary>逐帧等待条件成立，超时不断言，由调用方断言。Edit Mode 下请求一次 PlayerLoop 更新，驱动 ExecuteAlways。</summary>
        protected static IEnumerator WaitUntil(Func<bool> condition, double seconds = 3)
        {
            var deadline = EditorApplication.timeSinceStartup + seconds;
            while (!condition() && EditorApplication.timeSinceStartup < deadline)
            {
                if (!EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
                yield return null;
            }
        }

        protected static IEnumerator WaitSeconds(double seconds)
        {
            var deadline = EditorApplication.timeSinceStartup + seconds;
            while (EditorApplication.timeSinceStartup < deadline) yield return null;
        }

        protected static void Record(string sample, int cycle, string observation) =>
            TestContext.WriteLine($"[{sample}] Unity {Application.unityVersion}，Domain Reload=关闭，Scene Reload=开启，第 {cycle} 局：{observation}");

        /// <summary>Unity 6.3 中 enabled 由 options 推导：先写 options，再写与之一致的 enabled。</summary>
        static void ApplyOptions(EnterPlayModeOptions options)
        {
            EditorSettings.enterPlayModeOptions = options;
            EditorSettings.enterPlayModeOptionsEnabled = options != EnterPlayModeOptions.None;
        }

        static UnityEngine.Object SettingsAsset() =>
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/EditorSettings.asset")[0];
    }
}
