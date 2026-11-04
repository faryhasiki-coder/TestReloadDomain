using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ReloadIntegrationSamples.Tests
{
    /// <summary>
    /// INT-04 后台工作：async 循环、线程池计时器、晚到的异步结果。
    /// 退出后停止属于退出清理要求；关闭 Domain Reload 后，残留工作会进一步串到下一局。
    /// </summary>
    [Explicit(IntegrationAcceptance.ExplicitReason), Category(IntegrationAcceptance.Category)]
    public sealed class BackgroundWorkAcceptanceTests : IntegrationAcceptanceTestBase
    {
        const double k_QuietSeconds = 0.3;

        TaskCompletionSource<string> m_PendingProfile;

        /// <summary>测试收尾（在断言之后执行）：停止仍在编辑器中运行的后台工作，不影响本用例结果。</summary>
        [TearDown]
        public void StopLeftoverWork()
        {
            AutoSaveService.Stop();
            HeartbeatService.Stop();
            m_PendingProfile?.TrySetCanceled();
        }

        [UnityTest]
        public IEnumerator AutoSaveRunsOnlyForCurrentSession()
        {
            Counter previous = null;
            for (var cycle = 1; cycle <= Cycles; cycle++)
            {
                yield return EnterFastPlay();
                AssertSameDomain(cycle);

                var saves = new Counter();
                AutoSaveService.Start(saves.Increment, 50);
                yield return WaitUntil(() => saves.Value >= 2);
                Assert.GreaterOrEqual(saves.Value, 2, $"第 {cycle} 局：本局的自动存档应运行");
                if (previous != null)
                    Assert.AreEqual(previous.StoppedAt, previous.Value, $"第 {cycle} 局：上一局的存档回调不应再执行");

                yield return LeavePlay();
                yield return WaitUntilQuiet(saves);
                Record("自动存档", cycle, $"saves={saves.Value}");
                previous = saves;
            }
        }

        [UnityTest]
        public IEnumerator HeartbeatRunsOnlyForCurrentSession()
        {
            Counter previous = null;
            for (var cycle = 1; cycle <= Cycles; cycle++)
            {
                yield return EnterFastPlay();
                AssertSameDomain(cycle);

                var pings = new Counter();
                HeartbeatService.Start(_ => pings.Increment(), 20);
                yield return WaitUntil(() => pings.Value >= 2);
                Assert.GreaterOrEqual(pings.Value, 2, $"第 {cycle} 局：本局的心跳应运行");
                if (previous != null)
                    Assert.AreEqual(previous.StoppedAt, previous.Value, $"第 {cycle} 局：上一局的心跳回调不应再执行");

                yield return LeavePlay();
                yield return WaitUntilQuiet(pings);
                Record("心跳", cycle, $"pings={pings.Value}");
                previous = pings;
            }
        }

        [UnityTest]
        public IEnumerator LateProfileResultDoesNotOverwriteNewSession()
        {
            Task previousLoad = null;
            for (var cycle = 1; cycle <= Cycles; cycle++)
            {
                yield return EnterFastPlay();
                AssertSameDomain(cycle);
                Assert.IsNull(ProfileLoader.CurrentProfile, $"第 {cycle} 局开局不应带有档案");

                var current = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var load = ProfileLoader.LoadAsync(() => current.Task);
                current.SetResult("player-" + cycle);
                yield return WaitUntil(() => load.IsCompleted);
                Assert.AreEqual("player-" + cycle, ProfileLoader.CurrentProfile, $"第 {cycle} 局：本局请求应正常写入");

                if (previousLoad != null)
                {
                    m_PendingProfile.SetResult("stale-from-" + (cycle - 1));
                    yield return WaitUntil(() => previousLoad.IsCompleted);
                    Assert.IsTrue(previousLoad.IsCompleted, $"第 {cycle} 局：上一局的请求应结束（完成或取消均可），不能一直挂起");
                    Assert.AreEqual("player-" + cycle, ProfileLoader.CurrentProfile, $"第 {cycle} 局：上一局晚到的结果不应写入本局");
                }

                Record("档案加载", cycle, $"profile={ProfileLoader.CurrentProfile}");

                // 本局再发起一个慢请求，退出时仍未完成
                m_PendingProfile = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var pending = m_PendingProfile;
                previousLoad = ProfileLoader.LoadAsync(() => pending.Task);
                yield return LeavePlay();
            }
        }

        /// <summary>退出后先给在途回调 0.1 秒，记下停止时的计数；之后的静默期内计数不得再增加。</summary>
        static IEnumerator WaitUntilQuiet(Counter counter)
        {
            yield return WaitSeconds(0.1);
            counter.MarkStopped();
            yield return WaitSeconds(k_QuietSeconds);
            Assert.AreEqual(counter.StoppedAt, counter.Value, "退出 Play 后后台工作应停止");
        }

        sealed class Counter
        {
            int m_Value;
            public int Value => Volatile.Read(ref m_Value);
            public int StoppedAt { get; private set; }
            public void Increment() => Interlocked.Increment(ref m_Value);
            public void MarkStopped() => StoppedAt = Value;
        }
    }
}
