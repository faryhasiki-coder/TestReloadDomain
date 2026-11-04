using UnityEngine;

namespace ReloadIntegrationSamples
{
    /// <summary>场景入口：创建跨场景常驻的服务对象并登记到 ServiceLocator。</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        void Awake()
        {
            var services = new GameObject("[Services]");
            DontDestroyOnLoad(services);
            ServiceLocator.Register(services.AddComponent<AudioService>());
        }
    }
}
