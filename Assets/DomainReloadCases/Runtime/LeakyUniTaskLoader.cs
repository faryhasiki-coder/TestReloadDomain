using System;
using Cysharp.Threading.Tasks;

namespace DomainReloadCases
{
    /// <summary>
    /// 复现案例：UniTask 异步任务不传取消令牌、回写前不判断会话。关闭 Domain Reload 后：
    /// 1. 线程池工作跨会话完成时，UniTask.Yield 回到的是新会话的 PlayerLoop 队列，旧结果写入新会话；
    /// 2. UniTask.Delay 等待在 EnteredEditMode 时被 PlayerLoopHelper 丢弃，状态机不再继续，finally 永不执行。
    /// 该类故意暴露问题，不要为它加入重置、取消或清理逻辑。
    /// </summary>
    public static class LeakyUniTaskLoader
    {
        public static int StartedLoads;
        public static int CompletedLoads;
        public static int LastCompletedLoad;
        public static int HeldResources;

        /// <summary>在线程池执行 work（模拟慢速 IO，由调用方决定何时完成），完成后回到主线程写入结果。</summary>
        public static void StartLoad(Action work) => LoadAsync(++StartedLoads, work).Forget();

        /// <summary>占用一份资源，等待 duration 后在 finally 中释放。</summary>
        public static void HoldResource(TimeSpan duration) => HoldAsync(duration).Forget();

        static async UniTaskVoid LoadAsync(int load, Action work)
        {
            await UniTask.RunOnThreadPool(work);
            LastCompletedLoad = load;
            CompletedLoads++;
        }

        static async UniTaskVoid HoldAsync(TimeSpan duration)
        {
            HeldResources++;
            try
            {
                await UniTask.Delay(duration);
            }
            finally
            {
                HeldResources--;
            }
        }
    }
}
