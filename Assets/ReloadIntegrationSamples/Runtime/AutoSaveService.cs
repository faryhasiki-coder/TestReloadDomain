using System;
using System.Threading.Tasks;

namespace ReloadIntegrationSamples
{
    /// <summary>开局启动的定时自动存档；返回主菜单时调用 Stop。</summary>
    public static class AutoSaveService
    {
        static bool s_Running;

        public static void Start(Action save, int intervalMilliseconds)
        {
            if (s_Running) return;
            s_Running = true;
            Loop(save, intervalMilliseconds);
        }

        public static void Stop() => s_Running = false;

        static async void Loop(Action save, int intervalMilliseconds)
        {
            while (true)
            {
                await Task.Delay(intervalMilliseconds);
                if (!s_Running) return;
                save();
            }
        }
    }
}
