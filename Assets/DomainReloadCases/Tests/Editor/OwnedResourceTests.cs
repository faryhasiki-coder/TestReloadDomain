using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    public sealed class OwnedResourceTests : ReloadCaseTestBase
    {
        [UnityTest, Description("RES-02；R1 跨 3 轮 Play：实际创建 RenderTexture 和材质副本，退出释放自有资源，保留共享源材质")]
        public IEnumerator OwnedGpuResources_AreReleasedWithoutDestroyingSharedMaterial()
        {
            Material shared = null;
            OwnedResourceCase owner = null;
            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.That(shader, Is.Not.Null, "本案例使用当前工程已安装 URP 的 Unlit shader；无图形设备环境应单独记录环境限制");
                shared = new Material(shader) { name = "RES-02 caller-owned source", hideFlags = HideFlags.HideAndDontSave };
                var sharedIdentity = shared.GetInstanceID();
                var sharedTexture = shared.mainTexture;
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    yield return EnterFastPlay();
                    var baseline = OwnedResourceCase.ActiveOwnerCount;
                    owner = new OwnedResourceCase(shared);
                    var ownedTexture = owner.Texture;
                    var ownedMaterial = owner.Material;
                    Assert.That(ownedTexture.IsCreated(), Is.True);
                    Assert.That(ownedMaterial, Is.Not.SameAs(shared));
                    Assert.That(ownedMaterial.mainTexture, Is.SameAs(ownedTexture));
                    Assert.That(OwnedResourceCase.ActiveOwnerCount, Is.EqualTo(baseline + 1));
                    yield return LeavePlay();
                    Assert.That(owner.IsDisposed, Is.True);
                    Assert.That(ownedTexture == null, Is.True, "退出后 Unity 原生纹理对象已销毁");
                    Assert.That(ownedMaterial == null, Is.True, "退出后 Unity 原生材质副本已销毁");
                    Assert.That(OwnedResourceCase.ActiveOwnerCount, Is.EqualTo(baseline));
                    Assert.That(shared != null, Is.True, "不得销毁调用者/其他 owner 拥有的共享源");
                    Assert.That(shared.GetInstanceID(), Is.EqualTo(sharedIdentity));
                    Assert.That(shared.mainTexture, Is.SameAs(sharedTexture), "不得修改共享源的纹理绑定");
                    owner.Dispose();
                }
            }
            finally
            {
                owner?.Dispose();
                if (shared != null) Object.DestroyImmediate(shared);
            }
        }

        [Test, Description("RES-02；EditMode-only：自有资源显式释放可以重复调用，销毁只针对副本与纹理")]
        public void ExplicitRelease_IsIdempotentAndPreservesSharedSource()
        {
            Material shared = null;
            OwnedResourceCase owner = null;
            try
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                Assert.That(shader, Is.Not.Null);
                shared = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                var baseline = OwnedResourceCase.ActiveOwnerCount;
                owner = new OwnedResourceCase(shared);
                var ownedTexture = owner.Texture;
                var ownedMaterial = owner.Material;
                owner.Dispose();
                owner.Dispose();
                Assert.That(owner.IsDisposed, Is.True);
                Assert.That(ownedTexture == null, Is.True);
                Assert.That(ownedMaterial == null, Is.True);
                Assert.That(shared != null, Is.True);
                Assert.That(OwnedResourceCase.ActiveOwnerCount, Is.EqualTo(baseline));
            }
            finally
            {
                owner?.Dispose();
                if (shared != null) Object.DestroyImmediate(shared);
            }
        }
    }
}
