using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using EasyReloadGuard;

namespace DomainReloadCases
{
    /// <summary>
    /// 修复案例：与 LeakyUniTaskLoader 同样的两类异步任务，任务的 owner 是本加载器自己的取消源。
    /// CancelAll 取消全部在途任务：后台工作据此提前结束，Delay 立即以取消结束并执行 finally；
    /// 回写前再判断任务的令牌，被取消的旧任务不会写入结果。取消时机由业务决定，Editor 中借 [OnExitPlayMode] 在退出 Play 时调用，
    /// Player 中工具包不参与这些任务的取消与回写。
    /// </summary>
    public static class GuardedUniTaskLoader
    {
        [ResetStatic] public static int CompletedLoads;

        [ReloadSafe("资源计数由 finally 成对维护，CancelAll 取消后归零；重置会掩盖未释放的资源")]
        public static int HeldResources;

        [ReloadSafe("取消源由 CancelAll 换新，退出 Play 时已取消旧任务；重置会丢失尚未取消的在途任务")]
        static CancellationTokenSource s_Cts = new CancellationTokenSource();

        /// <summary>在线程池执行 work，work 应响应传入的令牌；完成后仅在任务未被取消时回写结果。</summary>
        public static void StartLoad(Action<CancellationToken> work) => LoadAsync(work, s_Cts.Token).Forget();

        /// <summary>占用一份资源，等待 duration 或任务被取消后在 finally 中释放。</summary>
        public static void HoldResource(TimeSpan duration) => HoldAsync(duration, s_Cts.Token).Forget();

        /// <summary>取消全部在途加载与资源占用；之后发起的任务使用新的令牌。</summary>
        [OnExitPlayMode]
        public static void CancelAll()
        {
            var cancelled = s_Cts;
            s_Cts = new CancellationTokenSource();
            // 不 Dispose：在途任务回写前仍需读取旧令牌
            cancelled.Cancel();
        }

        static async UniTaskVoid LoadAsync(Action<CancellationToken> work, CancellationToken token)
        {
            var canceled = await UniTask.RunOnThreadPool(() => work(token), cancellationToken: token).SuppressCancellationThrow();
            if (canceled || token.IsCancellationRequested) return;
            CompletedLoads++;
        }

        static async UniTaskVoid HoldAsync(TimeSpan duration, CancellationToken token)
        {
            HeldResources++;
            try
            {
                // cancelImmediately：令牌取消时同步结束等待，不依赖退出后 PlayerLoop 是否还会再运行一次
                await UniTask.Delay(duration, cancellationToken: token, cancelImmediately: true).SuppressCancellationThrow();
            }
            finally
            {
                HeldResources--;
            }
        }
    }
}
