using System;
using System.Collections.Generic;
using EasyReloadGuard;
using UnityEngine;

namespace DomainReloadCases
{
    /// <summary>RES-02：只释放案例自建 RenderTexture 和克隆材质，调用者拥有的源材质保持有效。</summary>
    public sealed class OwnedResourceCase : IDisposable
    {
        [ReloadSafe("仅由显式创建和退出清理维护；不能通过清空集合替代释放 GPU 资源")]
        static readonly List<OwnedResourceCase> s_Owners = new List<OwnedResourceCase>();

        public RenderTexture Texture { get; private set; }
        public Material Material { get; private set; }
        public bool IsDisposed { get; private set; }
        public static int ActiveOwnerCount => s_Owners.Count;

        public OwnedResourceCase(Material sharedSource)
        {
            if (sharedSource == null) throw new ArgumentNullException(nameof(sharedSource));
            try
            {
                Texture = new RenderTexture(16, 16, 0) { name = "RES-02 owned target", hideFlags = HideFlags.HideAndDontSave };
                if (!Texture.Create()) throw new InvalidOperationException("RenderTexture.Create 未成功");
                Material = new Material(sharedSource) { name = "RES-02 owned material", hideFlags = HideFlags.HideAndDontSave };
                Material.mainTexture = Texture;
                s_Owners.Add(this);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            if (Material != null)
            {
                Material.mainTexture = null;
                DestroyOwned(Material);
            }
            if (Texture != null)
            {
                Texture.Release();
                DestroyOwned(Texture);
            }
            s_Owners.Remove(this);
        }

        static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        [OnExitPlayMode]
        static void ReleaseSessionResources()
        {
            foreach (var owner in s_Owners.ToArray()) owner.Dispose();
        }
    }
}
