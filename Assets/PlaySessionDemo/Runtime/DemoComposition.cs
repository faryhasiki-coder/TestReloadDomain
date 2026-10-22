using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Faryhasiki.PlaySessions;
using UnityEngine.Scripting;

[assembly: PlaySessionAssembly(typeof(PlaySessionDemo.DemoComposition))]
[assembly: AlwaysLinkAssembly]

namespace PlaySessionDemo
{
    [Preserve]
    public sealed class DemoComposition : ISessionComposition
    {
        [Preserve]
        public DemoComposition() { }

        public void Configure(SessionPlan plan)
        {
            // 最小接入样例：插件在每次会话启动时调用业务代码明确提供的重置函数。
            plan.Add("sample.wallet", _ => { }, reset: CodeComparison.After.CoinWallet.ResetForPlay);

            plan.Add("demo.counter", scope =>
            {
                DemoState.Current = new DemoModel(scope);
                scope.OnDispose(() => DemoState.Current = null);
                scope.Own(new DemoLease());
                Action handler = () => DemoState.Counter++;
                DemoSignals.Pulse += handler;
                scope.OnRetire(() => DemoSignals.Pulse -= handler);
                DemoRaw.Counter++;
            }, reset: () =>
            {
                DemoState.Counter = 0;
                DemoState.Current = null;
            }, verifyStopped: issues =>
            {
                if (DemoSignals.SubscriberCount != 0 || DemoLease.LiveCount != 0 || DemoState.Current != null)
                    issues.Add(new SessionIssue("DEMO001", "demo.counter", "A demo subscription, lease or session reference survived cleanup."));
            });
        }
    }

    public static class DemoState
    {
        [SessionOwned("demo.counter")] public static int Counter;
        [SessionOwned("demo.counter")] public static DemoModel Current;
    }

    public static class DemoRaw
    {
        [SessionPersistent("故意保留的未重置计数，用于对照。真实项目不应以此理由豁免业务状态。")]
        public static int Counter;
    }

    public static class DemoSignals
    {
        [field: SessionPersistent("演示跨 Play 存活的事件发布端；每次会话必须自行退订，不能清空整个事件。")]
        public static event Action Pulse;
        public static int SubscriberCount => Pulse?.GetInvocationList().Length ?? 0;
        public static void Raise() { DemoRaw.Counter++; Pulse?.Invoke(); }
    }

    public sealed class DemoLease : IDisposable
    {
        [field: SessionPersistent("资源存量的观测值，只由真实创建/释放更新，禁止在 Reset 中归零掩盖泄漏。")]
        public static int LiveCount { get; private set; }
        private bool disposed;
        public DemoLease() => LiveCount++;
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            LiveCount--;
        }
    }

    public sealed class DemoModel
    {
        private readonly SessionScope scope;
        public int CompletedLoads { get; private set; }
        public int AcceptedRequests { get; private set; }
        public int RejectedRequests { get; private set; }
        public string LastAction { get; private set; } = "准备就绪。发送事件，或停止后再次 Play。";

        internal DemoModel(SessionScope scope) => this.scope = scope;

        public void LoadLater()
        {
            LastAction = "后台任务进行中；此时停止 Play 会取消任务。";
            scope.RunBackground(async cancel =>
            {
                await Task.Delay(2000, cancel).ConfigureAwait(false);
                await scope.CommitAsync(() =>
                {
                    CompletedLoads++;
                    LastAction = "结果已在当前会话的主线程提交。";
                }).ConfigureAwait(false);
            });
        }

        public bool SendToolRequest(Guid sessionId)
        {
            var accepted = PlaySessionRuntime.TryExecute(sessionId, () => AcceptedRequests++);
            if (!accepted) RejectedRequests++;
            LastAction = accepted ? "工具请求通过会话身份检查。" : "已拒绝过期会话请求，操作未执行。";
            return accepted;
        }
    }
}
