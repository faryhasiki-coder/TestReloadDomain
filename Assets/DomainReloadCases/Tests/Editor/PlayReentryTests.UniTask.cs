using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    /// <summary>
    /// UniTask 2.5.11 案例，连续 Play→Stop 3 轮，不改代码、不重新编译；Scene Reload 始终开启。
    /// 后台工作阻塞在闸门上模拟慢速 IO，由测试决定何时完成；资源占用用 10 分钟的 Delay，测试期间不会自然结束。
    /// </summary>
    public partial class PlayReentryTests
    {
        const string k_ControlUniTaskHandlersKey = "DomainReloadCases.ControlUniTaskHandlers";
        const double k_WaitSeconds = 5;
        const string k_HandlerReflectionHint =
            "无法读取 EditorApplication.m_PlayModeStateChangedEvent（6000.3 中为 EventWithPerformanceTracker）；内部结构变化时需复核反射路径";

        static readonly TimeSpan k_HoldDuration = TimeSpan.FromMinutes(10);
        static readonly ManualResetEventSlim s_WorkGate = new ManualResetEventSlim(false);
        static int s_BlockedWorkers;

        /// <summary>修复：任务以加载器自己的取消源为 owner，退出时 CancelAll 结束后台工作、执行 finally，会话内的加载照常完成。</summary>
        [UnityTest, Order(2)]
        public IEnumerator Fix_UniTaskWorkEndsWithSession()
        {
            s_WorkGate.Reset();
            var observed = new List<string>();

            for (var cycle = 1; cycle <= k_Cycles; cycle++)
            {
                yield return new EnterPlayMode(false);
                Assert.AreEqual(0, Volatile.Read(ref s_BlockedWorkers), "上一会话的后台工作应已随 CancelAll 结束");

                GuardedUniTaskLoader.StartLoad(GuardedWork);
                s_WorkGate.Set();
                yield return WaitFor(() => GuardedUniTaskLoader.CompletedLoads == 1);
                s_WorkGate.Reset();
                Assert.AreEqual(1, GuardedUniTaskLoader.CompletedLoads, "会话内完成的加载应正常写入，且没有旧会话的结果");

                GuardedUniTaskLoader.StartLoad(GuardedWork);
                GuardedUniTaskLoader.HoldResource(k_HoldDuration);
                yield return WaitFor(() => Volatile.Read(ref s_BlockedWorkers) == 1);
                Assert.AreEqual(1, Volatile.Read(ref s_BlockedWorkers));
                Assert.AreEqual(1, GuardedUniTaskLoader.HeldResources);
                yield return new ExitPlayMode();

                var heldAfterExit = GuardedUniTaskLoader.HeldResources;
                yield return WaitFor(() => Volatile.Read(ref s_BlockedWorkers) == 0);
                var blockedAfterExit = Volatile.Read(ref s_BlockedWorkers);
                observed.Add($"第 {cycle} 轮：completedLoads=1 " +
                             $"退出后 heldResources={heldAfterExit} blockedWorkers={blockedAfterExit}");

                Assert.AreEqual(0, heldAfterExit, "退出 Play 时 Delay 应随 CancelAll 结束并执行 finally");
                Assert.AreEqual(0, blockedAfterExit, "退出 Play 时后台工作应随 CancelAll 结束");
            }

            Report("UniTask 修复（业务自有取消源 + [OnExitPlayMode] CancelAll）", observed);
        }

        /// <summary>
        /// 复现：任务不传令牌时，后台工作跨会话完成并写入新会话；被丢弃的 Delay 不执行 finally，资源计数逐轮累积。
        /// UniTask 自身每轮多注册一组 playModeStateChanged 处理器，由项目临时适配器（Assets/ReloadGuardProjectAdapters）
        /// 在进入 Play 时移除上一组，处理器数量应保持不变。
        /// </summary>
        [UnityTest, Order(4)]
        public IEnumerator Repro_UniTaskWorkLeaksAcrossSessions()
        {
            s_WorkGate.Reset();
            var startCompleted = LeakyUniTaskLoader.CompletedLoads;
            var startHeld = LeakyUniTaskLoader.HeldResources;
            var startHandlers = CountUniTaskPlayModeHandlers();
            Assert.GreaterOrEqual(startHandlers, 0, k_HandlerReflectionHint);
            var previousLoad = 0;
            var observed = new List<string>();

            for (var cycle = 1; cycle <= k_Cycles; cycle++)
            {
                yield return new EnterPlayMode(false);
                var handlers = CountUniTaskPlayModeHandlers() - startHandlers;

                var staleWrite = "无";
                if (cycle > 1)
                {
                    Assert.AreEqual(1, Volatile.Read(ref s_BlockedWorkers), "上一会话的后台工作应仍阻塞在线程池中");
                    s_WorkGate.Set();
                    var expected = cycle - 1;
                    yield return WaitFor(() => LeakyUniTaskLoader.CompletedLoads - startCompleted == expected);
                    s_WorkGate.Reset();
                    Assert.AreEqual(expected, LeakyUniTaskLoader.CompletedLoads - startCompleted, "上一会话的结果应写入本会话");
                    Assert.AreEqual(previousLoad, LeakyUniTaskLoader.LastCompletedLoad);
                    staleWrite = $"上一轮的加载#{previousLoad}写入了第 {cycle} 轮";
                }

                Assert.AreEqual(cycle - 1, LeakyUniTaskLoader.HeldResources - startHeld, "上一会话的 Delay 被丢弃，finally 未执行");
                LeakyUniTaskLoader.StartLoad(LeakyWork);
                previousLoad = LeakyUniTaskLoader.StartedLoads;
                LeakyUniTaskLoader.HoldResource(k_HoldDuration);
                var held = LeakyUniTaskLoader.HeldResources - startHeld;
                observed.Add($"第 {cycle} 轮：heldResources={held} staleWrite={staleWrite} " +
                             $"uniTaskPlayModeHandlers=+{handlers}");

                Assert.AreEqual(cycle, held, "资源计数应逐轮累积");
                Assert.Greater(startHandlers, 0, k_HandlerReflectionHint);
                Assert.AreEqual(0, handlers, "项目临时适配器应移除上一次初始化的处理器，UniTask 处理器不应逐轮累积");
                yield return new ExitPlayMode();
            }

            Report("UniTask 复现（业务未处理；UniTask 处理器由项目临时适配器处理）", observed);
        }

        /// <summary>
        /// 对照：开启 Domain Reload 时，资源计数与 UniTask 处理器每轮都从干净状态开始。
        /// finally 同样没有执行，只是残留随旧域一起被丢弃。后台工作会跨域重载阻塞线程，不在对照组中启动。
        /// </summary>
        [UnityTest, Order(6)]
        public IEnumerator Control_UniTaskDomainReloadEnabledStartsFresh()
        {
            while (SessionState.GetInt(k_ControlCycleKey, 0) < k_Cycles)
            {
                yield return new EnterPlayMode(true);
                var cycle = SessionState.GetInt(k_ControlCycleKey, 0) + 1;
                SessionState.SetInt(k_ControlCycleKey, cycle);
                LeakyUniTaskLoader.HoldResource(k_HoldDuration);
                var handlers = CountUniTaskPlayModeHandlers();
                if (cycle == 1) SessionState.SetInt(k_ControlUniTaskHandlersKey, handlers);
                Report("UniTask 对照（未处理，Domain Reload=开启）", new List<string>
                {
                    $"第 {cycle} 轮：heldResources={LeakyUniTaskLoader.HeldResources} uniTaskPlayModeHandlers={handlers}"
                }, "开启");

                Assert.AreEqual(1, LeakyUniTaskLoader.HeldResources);
                Assert.Greater(handlers, 0, k_HandlerReflectionHint);
                Assert.AreEqual(SessionState.GetInt(k_ControlUniTaskHandlersKey, -1), handlers, "UniTask 处理器数量每轮应相同");
                yield return new ExitPlayMode();
            }
        }

        /// <summary>放开闸门并等待后台工作结束，避免阻塞的线程池工作留到后续用例或域重载。</summary>
        static void TearDownUniTask()
        {
            SessionState.EraseInt(k_ControlUniTaskHandlersKey);
            s_WorkGate.Set();
            SpinWait.SpinUntil(() => Volatile.Read(ref s_BlockedWorkers) == 0, TimeSpan.FromSeconds(k_WaitSeconds));
        }

        static void LeakyWork()
        {
            Interlocked.Increment(ref s_BlockedWorkers);
            try
            {
                s_WorkGate.Wait();
            }
            finally
            {
                Interlocked.Decrement(ref s_BlockedWorkers);
            }
        }

        static void GuardedWork(CancellationToken token)
        {
            Interlocked.Increment(ref s_BlockedWorkers);
            try
            {
                s_WorkGate.Wait(token);
            }
            finally
            {
                Interlocked.Decrement(ref s_BlockedWorkers);
            }
        }

        /// <summary>逐帧等待条件成立，最多 k_WaitSeconds 秒；调用方随后自行断言。</summary>
        static IEnumerator WaitFor(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition() && timer.Elapsed.TotalSeconds < k_WaitSeconds) yield return null;
        }

        /// <summary>
        /// 统计 EditorApplication.playModeStateChanged 上由 PlayerLoopHelper 注册的处理器；读取失败返回 -1。
        /// 依赖 Unity 6000.3 的内部结构：m_PlayModeStateChangedEvent.m_Delegate 的 length / GetDelegateAt；不限定小版本。
        /// </summary>
        static int CountUniTaskPlayModeHandlers()
        {
            const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var tracker = typeof(EditorApplication)
                .GetField("m_PlayModeStateChangedEvent", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            var handlesField = tracker?.GetType().GetField("m_Delegate", instance);
            if (handlesField == null) return -1;

            var handles = handlesField.GetValue(tracker);
            if (handles == null) return 0;
            var length = handles.GetType().GetProperty("length", instance);
            var getAt = handles.GetType().GetMethod("GetDelegateAt", instance);
            if (length == null || getAt == null) return -1;

            var count = 0;
            for (var i = 0; i < (int)length.GetValue(handles); i++)
            {
                if (!(getAt.Invoke(handles, new object[] { i }) is Delegate handler)) continue;
                foreach (var single in handler.GetInvocationList())
                {
                    if (DeclaredIn(single.Method.DeclaringType, typeof(PlayerLoopHelper))) count++;
                }
            }

            return count;
        }

        static bool DeclaredIn(Type type, Type owner)
        {
            for (; type != null; type = type.DeclaringType)
            {
                if (type == owner) return true;
            }

            return false;
        }
    }
}
