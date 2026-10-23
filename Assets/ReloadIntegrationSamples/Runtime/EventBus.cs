using System;

namespace ReloadIntegrationSamples
{
    /// <summary>按消息类型分发的全局事件总线，业务代码直接使用闭合类型，如 EventBus&lt;EnemyKilled&gt;。</summary>
    public static class EventBus<T>
    {
        static Action<T> s_Handlers;

        public static void Subscribe(Action<T> handler) => s_Handlers += handler;
        public static void Unsubscribe(Action<T> handler) => s_Handlers -= handler;
        public static void Publish(T message) => s_Handlers?.Invoke(message);
    }

    public readonly struct EnemyKilled
    {
        public readonly int Points;

        public EnemyKilled(int points) => Points = points;
    }
}
