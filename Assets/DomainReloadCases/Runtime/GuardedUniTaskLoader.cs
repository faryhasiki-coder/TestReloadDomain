using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyReloadGuard;

namespace DomainReloadCases
{
    /// <summary>
    /// 修复案例：与 LeakyUniTaskLoader 同样的两类异步任务，任务的 owner 是 Play 会话。
    /// 令牌取自 PlaySession.Token，退出 Play 时取消：后台工作据此提前结束，Delay 立即以取消结束并执行 finally；
    /// 回写前再用 PlaySessionStamp 判断，旧会话的结果不会写入新会话。
    /// </summary>
    public static class GuardedUniTaskLoader
    {
        [ResetStatic] public static int CompletedLoads;

        [ReloadSafe("资源计数由 finally 成对维护，退出 Play 时随令牌取消归零；重置会掩盖未释放的资源")]
        public static int HeldResources;

        /// <summary>在线程池执行 work，work 应响应传入的令牌；完成后仅在原会话仍活跃时回写结果。</summary>
        public static void StartLoad(Action<CancellationToken> work) => LoadAsync(work).Forget();

        /// <summary>占用一份资源，等待 duration 或会话结束后在 finally 中释放。</summary>
        public static void HoldResource(TimeSpan duration) => HoldAsync(duration).Forget();

        static async UniTaskVoid LoadAsync(Action<CancellationToken> work)
        {
            var session = PlaySession.Current;
            var token = PlaySession.Token;
            var canceled = await UniTask.RunOnThreadPool(() => work(token), cancellationToken: token).SuppressCancellationThrow();
            if (canceled || !session.IsAlive) return;
            CompletedLoads++;
        }

        static async UniTaskVoid HoldAsync(TimeSpan duration)
        {
            HeldResources++;
            try
            {
                // cancelImmediately：令牌取消时同步结束等待，不依赖退出后 PlayerLoop 是否还会再运行一次
                await UniTask.Delay(duration, cancellationToken: PlaySession.Token, cancelImmediately: true).SuppressCancellationThrow();
            }
            finally
            {
                HeldResources--;
            }
        }
    }
}
