using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ReloadIntegrationSamples.Tests
{
    /// <summary>INT-02 单例与对象池：泛型基类单例的每局状态、池中持有的场景对象。</summary>
    [Explicit(IntegrationAcceptance.ExplicitReason), Category(IntegrationAcceptance.Category)]
    public sealed class SingletonAndPoolAcceptanceTests : IntegrationAcceptanceTestBase
    {
        [UnityTest]
        public IEnumerator InventoryStartsEmptyEverySession()
        {
            for (var cycle = 1; cycle <= Cycles; cycle++)
            {
                yield return EnterFastPlay();
                AssertSameDomain(cycle);

                var inventory = Inventory.Instance;
                Assert.AreEqual(Inventory.StartingGold, inventory.Gold, $"第 {cycle} 局开局金币");
                Assert.IsEmpty(inventory.Items, $"第 {cycle} 局开局背包应为空");

                Assert.IsTrue(inventory.TryBuy("Sword", 60));
                Assert.AreEqual(Inventory.StartingGold - 60, inventory.Gold);
                CollectionAssert.AreEqual(new[] { "Sword" }, inventory.Items);

                Record("背包", cycle, $"gold={inventory.Gold} items={inventory.Items.Count}");
                yield return LeavePlay();
            }
        }

        [UnityTest]
        public IEnumerator BulletPoolOnlyHandsOutLiveBullets()
        {
            for (var cycle = 1; cycle <= Cycles; cycle++)
            {
                yield return EnterFastPlay();
                AssertSameDomain(cycle);

                var fired = new List<Bullet>();
                for (var i = 0; i < 3; i++)
                {
                    var bullet = BulletPool.Get();
                    Assert.IsTrue(bullet != null, $"第 {cycle} 局：取出的子弹应是存活对象");
                    bullet.Fire(Vector3.forward * i);
                    fired.Add(bullet);
                }

                foreach (var bullet in fired) BulletPool.Release(bullet);
                var reused = BulletPool.Get();
                Assert.IsTrue(reused != null && reused.gameObject.activeSelf);
                CollectionAssert.Contains(fired, reused, $"第 {cycle} 局：同一局内回收的子弹应被复用");

                Record("对象池", cycle, "取出 3 发、回收 3 发、复用 1 发");
                yield return LeavePlay();
            }
        }
    }
}
