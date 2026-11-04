using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    /// <summary>
    /// 连续 Play→Stop 3 轮，不改代码、不重新编译；Scene Reload 始终开启。
    /// 修复、复现两组关闭 Domain Reload，对照组开启 Domain Reload。
    /// 修复组需在本域尚未进入过 Play 时最先运行（批处理流水线满足），且所有关闭 Domain Reload 的用例都要排在
    /// 开启 Domain Reload 的对照之前，因此用 Order 固定顺序。UniTask 用例在 PlayReentryTests.UniTask.cs，
    /// 与本文件同属一个类：NUnit 3.5 的 Order 只能用于方法，无法跨类排序。
    /// </summary>
    public partial class PlayReentryTests
    {
        const int k_Cycles = 3;
        const string k_SavedOptionsKey = "DomainReloadCases.SavedPlayModeOptions";
        const string k_SavedDirtyKey = "DomainReloadCases.SavedEditorSettingsDirty";
        const string k_ControlCycleKey = "DomainReloadCases.ControlCycle";
        const string k_EditorSettingsPath = "ProjectSettings/EditorSettings.asset";

        // 开启 Domain Reload 时，Test Framework 在域重载后会重新执行 SetUp，且只恢复枚举器位置、不恢复局部变量：
        // 原设置与轮次都存入 SessionState，设置项按测试名决定（Control_ 前缀的用例开启 Domain Reload）
        [SetUp]
        public void SetUp()
        {
            if (SessionState.GetInt(k_SavedOptionsKey, -1) < 0)
            {
                SessionState.SetInt(k_SavedOptionsKey, (int)EffectiveOptions());
                SessionState.SetBool(k_SavedDirtyKey, EditorUtility.IsDirty(EditorSettingsAsset()));
            }

            var control = TestContext.CurrentContext.Test.MethodName.StartsWith("Control_", StringComparison.Ordinal);
            ApplyOptions(control ? EnterPlayModeOptions.None : EnterPlayModeOptions.DisableDomainReload);
        }

        [TearDown]
        public void TearDown()
        {
            SessionState.EraseInt(k_ControlCycleKey);
            TearDownUniTask();
            var saved = SessionState.GetInt(k_SavedOptionsKey, -1);
            if (saved < 0) return;
            ApplyOptions((EnterPlayModeOptions)saved);
            // 生效值已恢复；原先未修改过的设置资源不留脏标记，避免保存时改写磁盘上的旧版字段
            if (!SessionState.GetBool(k_SavedDirtyKey, false)) EditorUtility.ClearDirty(EditorSettingsAsset());
            SessionState.EraseInt(k_SavedOptionsKey);
            SessionState.EraseBool(k_SavedDirtyKey);
        }

        /// <summary>未启用 Enter Play Mode Options 时等同于两项重载都开启。</summary>
        static EnterPlayModeOptions EffectiveOptions() =>
            EditorSettings.enterPlayModeOptionsEnabled ? EditorSettings.enterPlayModeOptions : EnterPlayModeOptions.None;

        /// <summary>
        /// Unity 6.3 中 enabled 由 options 推导：先写 enabled 再写 None 会把 enabled 改回 false，
        /// 对 None 写 enabled=true 会把 options 改成两项都关闭。因此先写 options，再写与之一致的 enabled。
        /// </summary>
        static void ApplyOptions(EnterPlayModeOptions options)
        {
            EditorSettings.enterPlayModeOptions = options;
            EditorSettings.enterPlayModeOptionsEnabled = options != EnterPlayModeOptions.None;
        }

        static UnityEngine.Object EditorSettingsAsset() => AssetDatabase.LoadAllAssetsAtPath(k_EditorSettingsPath)[0];

        /// <summary>对照：开启 Domain Reload 时，同一个未处理的案例每轮都从干净状态开始。</summary>
        [UnityTest, Order(5)]
        public IEnumerator Control_DomainReloadEnabledStartsFresh()
        {
            while (SessionState.GetInt(k_ControlCycleKey, 0) < k_Cycles)
            {
                yield return new EnterPlayMode(true);
                var cycle = SessionState.GetInt(k_ControlCycleKey, 0) + 1;
                SessionState.SetInt(k_ControlCycleKey, cycle);
                new GameObject("LeakyCase").AddComponent<LeakyCaseCounter>();
                var handlers = CountLogHandlers(() => LeakyCaseCounter.LogCallbackCount);
                Report("对照（未处理，Domain Reload=开启）", new List<string>
                {
                    $"第 {cycle} 轮：created={LeakyCaseCounter.CreatedCount} instances={LeakyCaseCounter.Instances.Count} " +
                    $"tickedSubscribers={LeakyCaseCounter.TickedSubscriberCount} logHandlers={handlers}"
                }, "开启");

                Assert.AreEqual(1, LeakyCaseCounter.CreatedCount);
                Assert.AreEqual(1, LeakyCaseCounter.Instances.Count);
                Assert.AreEqual(1, LeakyCaseCounter.TickedSubscriberCount);
                Assert.AreEqual(1, handlers);
                yield return new ExitPlayMode();
            }
        }

        /// <summary>复现：未处理的静态状态逐轮累积。</summary>
        [UnityTest, Order(3)]
        public IEnumerator Repro_LeakyStateAccumulatesAcrossSessions()
        {
            var startCreated = LeakyCaseCounter.CreatedCount;
            var startInstances = LeakyCaseCounter.Instances.Count;
            var startTicked = LeakyCaseCounter.TickedSubscriberCount;
            var startHandlers = CountLogHandlers(() => LeakyCaseCounter.LogCallbackCount);
            var observed = new List<string>();

            for (var cycle = 1; cycle <= k_Cycles; cycle++)
            {
                yield return new EnterPlayMode(false);
                new GameObject("LeakyCase").AddComponent<LeakyCaseCounter>();

                var created = LeakyCaseCounter.CreatedCount - startCreated;
                var instances = LeakyCaseCounter.Instances.Count - startInstances;
                var ticked = LeakyCaseCounter.TickedSubscriberCount - startTicked;
                var handlers = CountLogHandlers(() => LeakyCaseCounter.LogCallbackCount) - startHandlers;
                observed.Add($"第 {cycle} 轮：created={created} instances={instances} tickedSubscribers={ticked} logHandlers={handlers}");

                Assert.AreEqual(cycle, created, "静态计数应逐轮累积");
                Assert.AreEqual(cycle, instances, "静态集合应保留上一会话已销毁的对象");
                Assert.AreEqual(cycle, ticked, "静态事件订阅应逐轮累积");
                Assert.AreEqual(cycle, handlers, "外部静态事件处理器应逐轮累积");
                yield return new ExitPlayMode();
            }

            Report("复现（未处理）", observed);
        }

        /// <summary>修复：声明处理方式后每轮都回到干净状态。</summary>
        [UnityTest, Order(1)]
        public IEnumerator Fix_GuardedStateIsResetEachSession()
        {
            var observed = new List<string>();

            for (var cycle = 1; cycle <= k_Cycles; cycle++)
            {
                yield return new EnterPlayMode(false);
                Assert.AreEqual("initial", GuardedCaseCounter.Label, "InitialValue 应恢复初值");
                new GameObject("GuardedCase").AddComponent<GuardedCaseCounter>();
                var handlers = CountLogHandlers(() => GuardedCaseCounter.LogCallbackCount);
                observed.Add($"第 {cycle} 轮：created={GuardedCaseCounter.CreatedCount} " +
                             $"instances={GuardedCaseCounter.Instances.Count} tickedSubscribers={GuardedCaseCounter.TickedSubscriberCount} " +
                             $"logHandlers={handlers} label={GuardedCaseCounter.Label}");

                Assert.AreEqual(1, GuardedCaseCounter.CreatedCount);
                Assert.AreEqual(1, GuardedCaseCounter.Instances.Count);
                Assert.AreEqual(1, GuardedCaseCounter.TickedSubscriberCount);
                Assert.AreEqual(1, handlers);

                GuardedCaseCounter.Label = "changed in session " + cycle;
                yield return new ExitPlayMode();
            }

            Assert.AreEqual(0, CountLogHandlers(() => GuardedCaseCounter.LogCallbackCount), "退出 Play 后不应残留外部事件处理器");
            Report("修复（Easy Reload Guard）", observed);
        }

        /// <summary>输出一次探针日志，返回当前挂在 Application.logMessageReceived 上的案例处理器数。</summary>
        static int CountLogHandlers(Func<int> counter)
        {
            var before = counter();
            Debug.Log(CaseProbe.Message);
            return counter() - before;
        }

        static void Report(string title, List<string> observed, string domainReload = "关闭")
        {
            var text = $"{title}，Unity {Application.unityVersion}，Domain Reload={domainReload}，Scene Reload=开启\n" +
                       string.Join("\n", observed);
            TestContext.Out.WriteLine(text);
            Debug.Log("[DomainReloadCases] " + text);
        }
    }
}
