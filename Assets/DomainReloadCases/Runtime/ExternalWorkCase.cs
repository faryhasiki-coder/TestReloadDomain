using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EasyReloadGuard;

namespace DomainReloadCases
{
    /// <summary>
    /// EV/ASYNC 实战案例。所有工作必须由测试显式启动；静态初始化只创建资源登记表。
    /// 适配方案：公开的 OnExitPlayMode + PlaySession.Token/Current，不修改供应商或包内部状态。
    /// </summary>
    public static class ExternalWorkCase
    {
        [ReloadSafe("跨会话保留登记表以便退出时逐个释放；不得直接清空尚未关闭的工作")]
        static readonly List<OwnedWork> s_Owners = new List<OwnedWork>();

        [field: ResetStatic]
        public static event Action OwnedEvent;

        public static int ActiveOwnerCount => s_Owners.Count;
        public static int OwnedSubscriberCount => OwnedEvent?.GetInvocationList().Length ?? 0;
        public static void RaiseOwnedEvent() => OwnedEvent?.Invoke();

        public static Subscription SubscribeOwned(Action callback) =>
            Subscribe(() => OwnedEvent += callback, () => OwnedEvent -= callback);

        /// <summary>只撤销本 owner 注册的委托，不清空外部事件源。</summary>
        public static Subscription Subscribe(Action subscribe, Action unsubscribe)
        {
            subscribe();
            return Track(new Subscription(unsubscribe));
        }

        public static CancellableTask StartTask() => Track(new CancellableTask(PlaySession.Token));
        public static ThreadWorker StartThread() => Track(new ThreadWorker());
        public static TimerWorker StartTimer() => Track(new TimerWorker());

        static T Track<T>(T owner) where T : OwnedWork
        {
            s_Owners.Add(owner);
            return owner;
        }

        [OnExitPlayMode]
        static void ReleaseSessionOwners()
        {
            var failures = new List<Exception>();
            foreach (var owner in s_Owners.ToArray())
            {
                try { owner.Dispose(); }
                catch (Exception exception) { failures.Add(exception); }
            }
            if (failures.Count != 0) throw new AggregateException("案例工作未能全部停止", failures);
        }

        public abstract class OwnedWork : IDisposable
        {
            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                if (IsDisposed) return;
                Close();
                IsDisposed = true;
                s_Owners.Remove(this);
            }

            protected abstract void Close();
        }

        public sealed class Subscription : OwnedWork
        {
            readonly Action _unsubscribe;
            internal Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;
            protected override void Close() => _unsubscribe();
        }

        /// <summary>模拟无法从外部清空的第三方事件源；同一源还允许其他 owner 长期订阅。</summary>
        public sealed class ExternalEventSource
        {
            public event Action Changed;
            public void Raise() => Changed?.Invoke();
            public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;
        }

        public sealed class FormatterSlot
        {
            public Func<int, string> Format;
        }

        /// <summary>
        /// 条件恢复协议：仅当前槽仍等于自己注册的委托时才能写回。
        /// A 已释放后 B 释放时跳过 A，恢复到最早仍有效的前任或初始 formatter。
        /// </summary>
        public sealed class FormatterLease : IDisposable
        {
            readonly FormatterSlot _slot;
            readonly Func<int, string> _previous;
            readonly Func<int, string> _formatter;
            readonly Func<int, string> _installed;
            bool _disposed;

            public FormatterLease(FormatterSlot slot, Func<int, string> formatter)
            {
                _slot = slot;
                _previous = slot.Format;
                _formatter = formatter;
                _installed = Format;
                slot.Format = _installed;
            }

            string Format(int value) => _formatter(value);

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                if (!ReferenceEquals(_slot.Format, _installed)) return;
                var previous = _previous;
                while (previous?.Target is FormatterLease predecessor && predecessor._disposed)
                    previous = predecessor._previous;
                _slot.Format = previous;
            }
        }

        public sealed class CancellableTask : OwnedWork
        {
            readonly CancellationTokenSource _cancellation;
            readonly TaskCompletionSource<bool> _started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int _finallyCount;
            public Task Completion { get; }
            public Task Started => _started.Task;
            public bool CancellationRequested { get; private set; }
            public int FinallyCount => Volatile.Read(ref _finallyCount);

            internal CancellableTask(CancellationToken sessionToken)
            {
                _cancellation = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
                var token = _cancellation.Token;
                Completion = Task.Run(async () =>
                {
                    _started.TrySetResult(true);
                    try { await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false); }
                    finally { Interlocked.Increment(ref _finallyCount); }
                });
            }

            protected override void Close()
            {
                _cancellation.Cancel();
                CancellationRequested = _cancellation.IsCancellationRequested;
                try
                {
                    if (!Completion.Wait(TimeSpan.FromSeconds(2)))
                        throw new TimeoutException("Task 收到取消请求后没有在两秒内结束");
                }
                catch (AggregateException exception) when (Completion.IsCanceled &&
                    exception.InnerExceptions.Count == 1 && exception.InnerException is OperationCanceledException)
                {
                    // 仅接受当前任务已经确认为 Canceled 的取消；普通异常必须向上报告。
                }
                _cancellation.Dispose();
            }
        }

        public sealed class ThreadWorker : OwnedWork
        {
            readonly ManualResetEventSlim _stop = new ManualResetEventSlim();
            readonly Thread _thread;
            int _callbacks;
            public int CallbackCount => Volatile.Read(ref _callbacks);
            public bool IsAlive => _thread.IsAlive;

            internal ThreadWorker()
            {
                _thread = new Thread(() =>
                {
                    while (!_stop.Wait(5)) Interlocked.Increment(ref _callbacks);
                }) { IsBackground = true, Name = "DomainReloadCases owned worker" };
                _thread.Start();
            }

            protected override void Close()
            {
                _stop.Set();
                if (!_thread.Join(TimeSpan.FromSeconds(2)))
                    throw new TimeoutException("Thread 在两秒内未停止；仍保留 owner 供诊断");
                _stop.Dispose();
            }
        }

        public sealed class TimerWorker : OwnedWork
        {
            readonly object _gate = new object();
            readonly Timer _timer;
            readonly ManualResetEvent _drained = new ManualResetEvent(false);
            bool _stopping;
            int _callbacks;
            public int CallbackCount => Volatile.Read(ref _callbacks);
            public bool CallbacksDrained { get; private set; }

            internal TimerWorker()
            {
                _timer = new Timer(_ =>
                {
                    lock (_gate)
                    {
                        if (!_stopping) Interlocked.Increment(ref _callbacks);
                    }
                }, null, 5, 5);
            }

            protected override void Close()
            {
                lock (_gate)
                {
                    if (!_stopping)
                    {
                        _stopping = true;
                        _timer.Dispose(_drained);
                    }
                }
                if (!_drained.WaitOne(TimeSpan.FromSeconds(2)))
                    throw new TimeoutException("Timer 的在途回调在两秒内未结束");
                CallbacksDrained = true;
                _drained.Dispose();
            }
        }

        /// <summary>外部工作本身不可取消时仍检查 token + stamp；不把真实故障伪装为取消。</summary>
        public sealed class SessionResultGate
        {
            readonly CancellationToken _token = PlaySession.Token;
            readonly PlaySessionStamp _stamp = PlaySession.Current;
            public int AcceptedCount { get; private set; }
            public int AcceptedValue { get; private set; }
            public int RejectedCount { get; private set; }

            public async Task<int> ObserveAsync(Task<int> externalCompletion)
            {
                // 外部等待不依赖旧 UnitySynchronizationContext；不捕获或改写业务异常。
                return await externalCompletion.ConfigureAwait(false);
            }

            /// <summary>测试/业务在主线程收到完成状态后调用，门禁检查与提交在主线程连续执行。</summary>
            public bool TryCommit(int result)
            {
                if (_token.IsCancellationRequested || !_stamp.IsAlive)
                {
                    RejectedCount++;
                    return false;
                }
                AcceptedValue = result;
                AcceptedCount++;
                return true;
            }
        }
    }
}
