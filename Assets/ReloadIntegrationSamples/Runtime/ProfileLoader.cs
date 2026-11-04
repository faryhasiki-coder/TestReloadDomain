using System;
using System.Threading.Tasks;

namespace ReloadIntegrationSamples
{
    /// <summary>从服务器拉取玩家档案，完成后写入 CurrentProfile 供 UI 读取。</summary>
    public static class ProfileLoader
    {
        public static string CurrentProfile { get; private set; }

        public static async Task LoadAsync(Func<Task<string>> fetch)
        {
            CurrentProfile = null;
            CurrentProfile = await fetch();
        }
    }
}
