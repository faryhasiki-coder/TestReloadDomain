using System;
using System.Collections.Generic;

namespace ReloadIntegrationSamples
{
    /// <summary>按类型登记全局服务，每种服务只允许登记一次。</summary>
    public static class ServiceLocator
    {
        static readonly Dictionary<Type, object> s_Services = new Dictionary<Type, object>();

        public static void Register<T>(T service) where T : class => s_Services.Add(typeof(T), service);

        public static T Get<T>() where T : class => s_Services.TryGetValue(typeof(T), out var service) ? (T)service : null;
    }
}
