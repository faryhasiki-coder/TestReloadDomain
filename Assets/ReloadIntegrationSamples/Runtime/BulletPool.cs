using System.Collections.Generic;
using UnityEngine;

namespace ReloadIntegrationSamples
{
    /// <summary>子弹对象池：回收的子弹停用后入栈，下次发射优先复用。</summary>
    public static class BulletPool
    {
        static readonly Stack<Bullet> s_Free = new Stack<Bullet>();

        public static Bullet Get()
        {
            var bullet = s_Free.Count > 0 ? s_Free.Pop() : new GameObject("Bullet").AddComponent<Bullet>();
            bullet.gameObject.SetActive(true);
            return bullet;
        }

        public static void Release(Bullet bullet)
        {
            bullet.gameObject.SetActive(false);
            s_Free.Push(bullet);
        }
    }
}
