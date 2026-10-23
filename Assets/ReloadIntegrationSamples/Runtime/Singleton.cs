namespace ReloadIntegrationSamples
{
    /// <summary>纯 C# 单例基类，首次访问时创建。</summary>
    public abstract class Singleton<T> where T : class, new()
    {
        static T s_Instance;

        public static T Instance => s_Instance ??= new T();
    }
}
