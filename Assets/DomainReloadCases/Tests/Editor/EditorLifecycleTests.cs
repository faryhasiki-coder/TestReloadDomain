using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    /// <summary>
    /// Editor 专属入口；不在测试程序集声明包的 ResetMethod（包有意跳过测试程序集）。
    /// 普通测试只订阅/观察事件，绝不请求编译或域重载。
    /// </summary>
    [InitializeOnLoad]
    static class EditorLifecycleCaseProbe
    {
        const string ManualPrefix = "DomainReloadCases.IN04.Manual.";
        const string MenuRoot = "Tools/Domain Reload Cases/IN-04 Assembly Reload/";
        static bool s_Armed;
        static string s_SavedPreference;

        static EditorLifecycleCaseProbe()
        {
            InitializeOnLoadCount++;
            if (SessionState.GetBool(ManualPrefix + "armed", false)) AttachManualAssemblyObservers();
        }

        internal static int InitializeOnLoadCount { get; private set; }
        internal static int EnterPlayCount { get; private set; }
        internal static int ExitingPlayCount { get; private set; }
        internal static int BeforeAssemblyReloadCount { get; private set; }
        internal static int AfterAssemblyReloadCount { get; private set; }
        internal static int UpdateCount { get; private set; }
        internal static int TransientCache { get; set; }
        internal static string StickyPreference { get; set; } = "keep-editor-preference";
        internal static bool UpdateSubscribed { get; private set; }

        internal static void Arm()
        {
            Disarm();
            s_SavedPreference = StickyPreference;
            EnterPlayCount = ExitingPlayCount = BeforeAssemblyReloadCount = AfterAssemblyReloadCount = UpdateCount = 0;
            s_Armed = true;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
        }

        internal static void Disarm()
        {
            DetachUpdate();
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
            if (s_Armed) StickyPreference = s_SavedPreference;
            s_Armed = false;
            TransientCache = 0;
        }

        [InitializeOnEnterPlayMode]
        static void InitializeSession(EnterPlayModeOptions options)
        {
            if (!s_Armed) return;
            EnterPlayCount++;
            TransientCache = 0;
            // 防御重复登记；本入口由 Unity 调用，不由测试手动模拟。
            DetachUpdate();
            EditorApplication.update += OnUpdate;
            UpdateSubscribed = true;
        }

        static void OnUpdate()
        {
            UpdateCount++;
        }

        static void DetachUpdate()
        {
            EditorApplication.update -= OnUpdate;
            UpdateSubscribed = false;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode) return;
            ExitingPlayCount++;
            DetachUpdate();
        }

        static void OnBeforeAssemblyReload()
        {
            BeforeAssemblyReloadCount++;
            DetachUpdate();
        }

        static void OnAfterAssemblyReload()
        {
            AfterAssemblyReloadCount++;
        }

        // 以下观察器只在用户点菜单后启用；SessionState 跨域保存计数，不修改项目设置/脚本。
        [MenuItem(MenuRoot + "1. 开始被动观察")]
        static void ArmManualAssemblyObservation()
        {
            SessionState.SetInt(ManualPrefix + "before", 0);
            SessionState.SetInt(ManualPrefix + "after", 0);
            SessionState.SetBool(ManualPrefix + "armed", true);
            AttachManualAssemblyObservers();
            Debug.Log("[IN-04] 已开始被动观察。先 R1 Play→Stop，应仍为 0/0；再手动重编译脚本，随后查看结果。此菜单不触发编译或重载。");
        }

        static void AttachManualAssemblyObservers()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= RecordManualBefore;
            AssemblyReloadEvents.afterAssemblyReload -= RecordManualAfter;
            AssemblyReloadEvents.beforeAssemblyReload += RecordManualBefore;
            AssemblyReloadEvents.afterAssemblyReload += RecordManualAfter;
        }

        static void RecordManualBefore()
        {
            SessionState.SetInt(ManualPrefix + "before", SessionState.GetInt(ManualPrefix + "before", 0) + 1);
        }

        static void RecordManualAfter()
        {
            SessionState.SetInt(ManualPrefix + "after", SessionState.GetInt(ManualPrefix + "after", 0) + 1);
        }

        [MenuItem(MenuRoot + "2. 查看观察计数")]
        static void ReportManualAssemblyObservation()
        {
            Debug.Log($"[IN-04] armed={SessionState.GetBool(ManualPrefix + "armed", false)}, " +
                      $"beforeAssemblyReload={SessionState.GetInt(ManualPrefix + "before", 0)}, " +
                      $"afterAssemblyReload={SessionState.GetInt(ManualPrefix + "after", 0)}。" +
                      "此处仅显示观察值；完成 Play 对照与真实重编译后再人工判定。");
        }

        [MenuItem(MenuRoot + "3. 结束观察并清理计数")]
        static void StopManualAssemblyObservation()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= RecordManualBefore;
            AssemblyReloadEvents.afterAssemblyReload -= RecordManualAfter;
            SessionState.EraseBool(ManualPrefix + "armed");
            SessionState.EraseInt(ManualPrefix + "before");
            SessionState.EraseInt(ManualPrefix + "after");
        }
    }

    [Category("Case.EditorLifecycle")]
    public sealed class EditorLifecycleTests : ReloadCaseTestBase
    {
        [TearDown]
        public void DetachEditorObservers()
        {
            EditorLifecycleCaseProbe.Disarm();
        }

        [UnityTest]
        [Description("IN-03 / R1：InitializeOnEnterPlayMode 每轮清理临时缓存，保留编辑器偏好；真实 EditorApplication.update 订阅在退出 Play 释放。")]
        public IEnumerator EditorSessionEntryResetsCacheAndExitReleasesSubscription()
        {
            EditorLifecycleCaseProbe.Arm();
            var initializeOnLoad = EditorLifecycleCaseProbe.InitializeOnLoadCount;
            EditorLifecycleCaseProbe.StickyPreference = "user-tool-preference";

            for (var cycle = 1; cycle <= 3; cycle++)
            {
                EditorLifecycleCaseProbe.TransientCache = 100 + cycle;
                yield return EnterFastPlay();
                Assert.AreEqual(cycle, EditorLifecycleCaseProbe.EnterPlayCount);
                Assert.AreEqual(0, EditorLifecycleCaseProbe.TransientCache);
                Assert.AreEqual(initializeOnLoad, EditorLifecycleCaseProbe.InitializeOnLoadCount,
                    "InitializeOnLoad 是域加载入口，R1 普通 Play 不能依赖它每轮执行。");
                Assert.AreEqual("user-tool-preference", EditorLifecycleCaseProbe.StickyPreference);
                var beforeUpdate = EditorLifecycleCaseProbe.UpdateCount;
                yield return WaitUntil(() => EditorLifecycleCaseProbe.UpdateCount > beforeUpdate);
                Assert.IsTrue(EditorLifecycleCaseProbe.UpdateSubscribed);

                yield return LeavePlay();
                Assert.AreEqual(cycle, EditorLifecycleCaseProbe.ExitingPlayCount);
                Assert.IsFalse(EditorLifecycleCaseProbe.UpdateSubscribed);
                var afterExit = EditorLifecycleCaseProbe.UpdateCount;
                for (var tick = 0; tick < 3; tick++) yield return null;
                Assert.AreEqual(afterExit, EditorLifecycleCaseProbe.UpdateCount, "退出后案例 Update 不应继续被编辑器调用。");
            }
        }

        [UnityTest]
        [Description("IN-04 / R1：普通 Play→Stop 发生 Play 状态事件，但不会触发真实 AssemblyReloadEvents；不请求重编译。")]
        public IEnumerator FastPlayTransitionDoesNotRaiseAssemblyReloadEvents()
        {
            EditorLifecycleCaseProbe.Arm();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return EnterFastPlay();
                yield return LeavePlay();
            }
            Assert.AreEqual(3, EditorLifecycleCaseProbe.EnterPlayCount);
            Assert.AreEqual(3, EditorLifecycleCaseProbe.ExitingPlayCount);
            Assert.AreEqual(0, EditorLifecycleCaseProbe.BeforeAssemblyReloadCount);
            Assert.AreEqual(0, EditorLifecycleCaseProbe.AfterAssemblyReloadCount);
        }

    }
}
