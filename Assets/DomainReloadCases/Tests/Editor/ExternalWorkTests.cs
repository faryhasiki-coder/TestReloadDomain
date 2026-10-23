using System;
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    /// <summary>只声明测试。资源仅在测试显式运行时启动；失败路径也必须释放。</summary>
    public sealed class ExternalWorkTests : ReloadCaseTestBase
    {
        [UnityTest, Description("EV-01；R1 跨 3 轮 Play：自有静态事件每轮只有当前 owner，退出后解除")]
        public IEnumerator OwnedEvent_HasOneCurrentSubscriberAcrossThreeSessions()
        {
            ExternalWorkCase.Subscription owner = null;
            try
            {
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    yield return EnterFastPlay();
                    Assert.That(ExternalWorkCase.OwnedSubscriberCount, Is.Zero);
                    var calls = 0;
                    owner = ExternalWorkCase.SubscribeOwned(() => calls++);
                    ExternalWorkCase.RaiseOwnedEvent();
                    Assert.That(calls, Is.EqualTo(1));
                    Assert.That(ExternalWorkCase.OwnedSubscriberCount, Is.EqualTo(1));
                    yield return LeavePlay();
                    Assert.That(owner.IsDisposed, Is.True, "退出入口应实际释放 owner");
                    Assert.That(ExternalWorkCase.OwnedSubscriberCount, Is.Zero);
                    ExternalWorkCase.RaiseOwnedEvent();
                    Assert.That(calls, Is.EqualTo(1), "旧 owner 不得继续接收事件");
                }
            }
            finally { owner?.Dispose(); }
        }

        [Test, Description("EV-02；EditMode-only：第三方样式事件源只退订自己的 handler，重复释放安全，其他 owner 不受影响")]
        public void ExternalEvent_UnsubscribesOnlyItsOwner()
        {
            var source = new ExternalWorkCase.ExternalEventSource();
            var permanentCalls = 0;
            var sessionCalls = 0;
            Action permanent = () => permanentCalls++;
            Action temporary = () => sessionCalls++;
            source.Changed += permanent;
            var baseline = ExternalWorkCase.ActiveOwnerCount;
            ExternalWorkCase.Subscription owner = null;
            try
            {
                owner = ExternalWorkCase.Subscribe(() => source.Changed += temporary, () => source.Changed -= temporary);
                source.Raise();
                Assert.That(permanentCalls, Is.EqualTo(1));
                Assert.That(sessionCalls, Is.EqualTo(1));
                owner.Dispose();
                owner.Dispose();
                source.Raise();
                Assert.That(permanentCalls, Is.EqualTo(2));
                Assert.That(sessionCalls, Is.EqualTo(1));
                Assert.That(source.SubscriberCount, Is.EqualTo(1));
                Assert.That(ExternalWorkCase.ActiveOwnerCount, Is.EqualTo(baseline));
            }
            finally
            {
                owner?.Dispose();
                source.Changed -= permanent;
            }
        }

        [UnityTest, Description("EV-02；R1 跨 3 轮 Play：真实 EditorApplication 事件的 Play 临时订阅退出时释放，编辑器长期 owner 保留")]
        public IEnumerator EditorEvent_PreservesLongLivedOwnerAcrossThreeSessions()
        {
            var editorCalls = 0;
            var staleCalls = 0;
            Action<PlayModeStateChange> permanent = state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) editorCalls++;
            };
            Action<PlayModeStateChange> temporary = state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode) staleCalls++;
            };
            ExternalWorkCase.Subscription owner = null;
            EditorApplication.playModeStateChanged += permanent;
            try
            {
                for (var cycle = 1; cycle <= 3; cycle++)
                {
                    yield return EnterFastPlay();
                    var baseline = ExternalWorkCase.ActiveOwnerCount;
                    owner = ExternalWorkCase.Subscribe(
                        () => EditorApplication.playModeStateChanged += temporary,
                        () => EditorApplication.playModeStateChanged -= temporary);
                    yield return LeavePlay();
                    Assert.That(owner.IsDisposed, Is.True);
                    Assert.That(staleCalls, Is.Zero, "临时 owner 必须在 EnteredEditMode 前解除");
                    Assert.That(editorCalls, Is.EqualTo(cycle), "不允许清空其他 owner 的外部订阅");
                    Assert.That(ExternalWorkCase.ActiveOwnerCount, Is.EqualTo(baseline));
                    owner.Dispose();
                }
            }
            finally
            {
                owner?.Dispose();
                EditorApplication.playModeStateChanged -= temporary;
                EditorApplication.playModeStateChanged -= permanent;
            }
        }

        [Test, Description("EV-03；EditMode-only：A 注册、B 接管、A 先释放不能覆盖 B；B 随后释放跳过已失效的 A")]
        public void FormatterSlot_EarlierOwnerCannotOverwriteReplacement()
        {
            Func<int, string> baseline = value => "base:" + value;
            var slot = new ExternalWorkCase.FormatterSlot { Format = baseline };
            using (var ownerA = new ExternalWorkCase.FormatterLease(slot, value => "A:" + value))
            using (var ownerB = new ExternalWorkCase.FormatterLease(slot, value => "B:" + value))
            {
                ownerA.Dispose();
                Assert.That(slot.Format(7), Is.EqualTo("B:7"));
                ownerB.Dispose();
                Assert.That(slot.Format, Is.SameAs(baseline));
                Assert.That(slot.Format(7), Is.EqualTo("base:7"));
            }
        }

        [Test, Description("EV-03；EditMode-only：B 先释放时恢复仍存活的 A，A 再释放恢复原始 formatter")]
        public void FormatterSlot_ReverseReleaseRestoresLivePredecessor()
        {
            Func<int, string> baseline = value => "base:" + value;
            var slot = new ExternalWorkCase.FormatterSlot { Format = baseline };
            using (var ownerA = new ExternalWorkCase.FormatterLease(slot, value => "A:" + value))
            using (var ownerB = new ExternalWorkCase.FormatterLease(slot, value => "B:" + value))
            {
                ownerB.Dispose();
                Assert.That(slot.Format(7), Is.EqualTo("A:7"));
                ownerA.Dispose();
                Assert.That(slot.Format, Is.SameAs(baseline));
            }
        }

        [UnityTest, Description("ASYNC-01；R1 跨 3 轮 Play：真实 Task 收到会话取消后进入 Canceled，finally 已执行，不能仅验证取消标志")]
        public IEnumerator Task_CancellationActuallyTerminatesWorkAcrossThreeSessions()
        {
            ExternalWorkCase.CancellableTask worker = null;
            try
            {
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    yield return EnterFastPlay();
                    var baseline = ExternalWorkCase.ActiveOwnerCount;
                    worker = ExternalWorkCase.StartTask();
                    yield return WaitUntil(() => worker.Started.IsCompleted);
                    Assert.That(worker.Started.IsCompleted, Is.True);
                    Assert.That(worker.Completion.IsCompleted, Is.False);
                    yield return LeavePlay();
                    Assert.That(worker.CancellationRequested, Is.True);
                    Assert.That(worker.Completion.Status, Is.EqualTo(TaskStatus.Canceled));
                    Assert.That(worker.FinallyCount, Is.EqualTo(1));
                    Assert.That(worker.IsDisposed, Is.True);
                    Assert.That(ExternalWorkCase.ActiveOwnerCount, Is.EqualTo(baseline));
                }
            }
            finally { worker?.Dispose(); }
        }

        [UnityTest, Description("ASYNC-02；R1 跨 3 轮 Play：真实 Thread 启动并工作，退出时有界 Join，之后不再产生回调")]
        public IEnumerator Thread_StopsAndRemainsStoppedAcrossThreeSessions()
        {
            ExternalWorkCase.ThreadWorker worker = null;
            try
            {
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    yield return EnterFastPlay();
                    var baseline = ExternalWorkCase.ActiveOwnerCount;
                    worker = ExternalWorkCase.StartThread();
                    yield return WaitUntil(() => worker.CallbackCount > 0);
                    Assert.That(worker.CallbackCount, Is.GreaterThan(0));
                    yield return LeavePlay();
                    Assert.That(worker.IsAlive, Is.False, "字段置空不能替代线程停止证据");
                    Assert.That(worker.IsDisposed, Is.True);
                    var stoppedAt = worker.CallbackCount;
                    var deadline = EditorApplication.timeSinceStartup + 0.05;
                    yield return WaitUntil(() => EditorApplication.timeSinceStartup >= deadline);
                    Assert.That(worker.CallbackCount, Is.EqualTo(stoppedAt));
                    Assert.That(ExternalWorkCase.ActiveOwnerCount, Is.EqualTo(baseline));
                }
            }
            finally { worker?.Dispose(); }
        }

        [UnityTest, Description("ASYNC-02；R1 跨 3 轮 Play：真实 Timer 工作，退出时停表并有界等待在途回调，之后计数保持不变")]
        public IEnumerator Timer_DrainsCallbacksAcrossThreeSessions()
        {
            ExternalWorkCase.TimerWorker worker = null;
            try
            {
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    yield return EnterFastPlay();
                    var baseline = ExternalWorkCase.ActiveOwnerCount;
                    worker = ExternalWorkCase.StartTimer();
                    yield return WaitUntil(() => worker.CallbackCount > 0);
                    Assert.That(worker.CallbackCount, Is.GreaterThan(0));
                    yield return LeavePlay();
                    Assert.That(worker.CallbacksDrained, Is.True);
                    Assert.That(worker.IsDisposed, Is.True);
                    var stoppedAt = worker.CallbackCount;
                    var deadline = EditorApplication.timeSinceStartup + 0.05;
                    yield return WaitUntil(() => EditorApplication.timeSinceStartup >= deadline);
                    Assert.That(worker.CallbackCount, Is.EqualTo(stoppedAt));
                    Assert.That(ExternalWorkCase.ActiveOwnerCount, Is.EqualTo(baseline));
                }
            }
            finally { worker?.Dispose(); }
        }

        [UnityTest, Description("ASYNC-03；R1 跨 3 轮 Play：不可取消的旧外部结果在下一会话/退出后才完成，token + stamp 拒绝旧提交，当前提交成功")]
        public IEnumerator LateCompletion_CannotCommitIntoNextSession()
        {
            TaskCompletionSource<int> previousSource = null;
            Task<int> previousTask = null;
            ExternalWorkCase.SessionResultGate previousGate = null;
            try
            {
                for (var cycle = 1; cycle <= 3; cycle++)
                {
                    yield return EnterFastPlay();
                    if (previousSource != null)
                    {
                        previousSource.SetResult(-cycle);
                        yield return WaitUntil(() => previousTask.IsCompleted);
                        Assert.That(previousGate.TryCommit(previousTask.GetAwaiter().GetResult()), Is.False);
                        Assert.That(previousGate.RejectedCount, Is.EqualTo(1));
                        Assert.That(previousGate.AcceptedValue, Is.EqualTo(cycle - 1));
                    }

                    var current = new ExternalWorkCase.SessionResultGate();
                    var immediate = current.ObserveAsync(Task.FromResult(cycle));
                    yield return WaitUntil(() => immediate.IsCompleted);
                    Assert.That(current.TryCommit(immediate.GetAwaiter().GetResult()), Is.True);
                    Assert.That(current.AcceptedCount, Is.EqualTo(1));
                    Assert.That(current.AcceptedValue, Is.EqualTo(cycle));
                    previousSource = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                    previousGate = current;
                    previousTask = current.ObserveAsync(previousSource.Task);
                    Assert.That(previousTask.IsCompleted, Is.False);
                    yield return LeavePlay();
                }
                previousSource.SetResult(-99);
                yield return WaitUntil(() => previousTask.IsCompleted);
                Assert.That(previousGate.TryCommit(previousTask.GetAwaiter().GetResult()), Is.False);
                Assert.That(previousGate.AcceptedValue, Is.EqualTo(3));
                Assert.That(previousGate.RejectedCount, Is.EqualTo(1));
            }
            finally { previousSource?.TrySetCanceled(); }
        }

        [Test, Description("ASYNC-03；EditMode-only：真实外部业务异常保留 Faulted 与原异常身份，不能吞成取消或成功")]
        public void ExternalFailure_RemainsFaultedWithOriginalException()
        {
            var failure = new InvalidOperationException("case business failure");
            var gate = new ExternalWorkCase.SessionResultGate();
            var observed = gate.ObserveAsync(Task.FromException<int>(failure));
            Assert.That(observed.IsFaulted, Is.True);
            Assert.That(observed.IsCanceled, Is.False);
            var thrown = Assert.Throws<InvalidOperationException>(() => observed.GetAwaiter().GetResult());
            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(gate.AcceptedCount, Is.Zero);
        }
    }
}
