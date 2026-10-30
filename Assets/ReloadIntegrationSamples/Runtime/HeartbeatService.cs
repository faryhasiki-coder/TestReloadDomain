using System;
using System.Threading;

namespace ReloadIntegrationSamples
{
    /// <summary>联网心跳：在线程池计时器上按固定间隔发送带序号的 ping；断线时调用 Stop。</summary>
    public static class HeartbeatService
    {
        static Timer s_Timer;

        public static void Start(Action<int> sendPing, int intervalMilliseconds)
        {
            if (s_Timer != null) return;
            var sequence = 0;
            s_Timer = new Timer(_ => sendPing(Interlocked.Increment(ref sequence)), null, intervalMilliseconds, intervalMilliseconds);
        }

        public static void Stop()
        {
            s_Timer?.Dispose();
            s_Timer = null;
        }
    }
}
