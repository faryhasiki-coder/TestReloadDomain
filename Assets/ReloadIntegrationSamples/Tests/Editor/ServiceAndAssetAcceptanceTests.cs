using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ReloadIntegrationSamples.Tests
{
    /// <summary>INT-03 服务注册：场景入口每局登记常驻服务；ScriptableObject 的运行时字段。</summary>
    [Explicit(IntegrationAcceptance.ExplicitReason), Category(IntegrationAcceptance.Category)]
    public sealed class ServiceAndAssetAcceptanceTests : IntegrationAcceptanceTestBase
    {
        PlayerStats m_Stats;

        [TearDown]
        public void DestroyStats()
        {
            if (m_Stats != null) Object.DestroyImmediate(m_Stats);
        }

        [UnityTest]
        public IEnumerator BootstrapRegistersLiveServicesEverySession()
        {
            for (var cycle = 1; cycle <= Cycles; cycle++)
            {
                yield return EnterFastPlay();
                AssertSameDomain(cycle);

                new GameObject("Bootstrap").AddComponent<GameBootstrap>();
                var audio = ServiceLocator.Get<AudioService>();
                Assert.IsTrue(audio != null, $"第 {cycle} 局：ServiceLocator 应返回本局创建的存活服务");
                audio.Play("start");
                Assert.AreEqual(1, audio.PlayedClips);

                Record("服务注册", cycle, $"audio 存活，played={audio.PlayedClips}");
                yield return LeavePlay();
            }
        }

        /// <summary>
        /// 用一份编辑器中常驻的实例代替已加载的 .asset：两者都跨 Play 存活，且只在加载或域重载时执行 OnEnable。
        /// </summary>
        [UnityTest]
        public IEnumerator PlayerStatsStartAtFullHealthEverySession()
        {
            m_Stats = ScriptableObject.CreateInstance<PlayerStats>();
            m_Stats.hideFlags = HideFlags.DontSave;

            for (var cycle = 1; cycle <= Cycles; cycle++)
            {
                yield return EnterFastPlay();
                AssertSameDomain(cycle);

                Assert.AreEqual(m_Stats.MaxHealth, m_Stats.CurrentHealth, $"第 {cycle} 局开局应满血");
                m_Stats.TakeDamage(30);
                Assert.AreEqual(m_Stats.MaxHealth - 30, m_Stats.CurrentHealth);

                Record("ScriptableObject", cycle, $"health={m_Stats.CurrentHealth}/{m_Stats.MaxHealth}");
                yield return LeavePlay();
            }
        }
    }
}
