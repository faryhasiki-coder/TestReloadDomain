using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ReloadIntegrationSamples.Tests
{
    /// <summary>INT-01 计分：静态事件、静态自动属性、非默认初值、初始化标志、直接使用的闭合泛型。</summary>
    [Explicit(IntegrationAcceptance.ExplicitReason), Category(IntegrationAcceptance.Category)]
    public sealed class ScoringAcceptanceTests : IntegrationAcceptanceTestBase
    {
        [UnityTest]
        public IEnumerator EverySessionScoresFromZeroAndOncePerKill()
        {
            for (var cycle = 1; cycle <= Cycles; cycle++)
            {
                yield return EnterFastPlay();
                AssertSameDomain(cycle);

                var label = new GameObject("ScoreLabel").AddComponent<ScoreLabel>();
                ScoreSystem.Initialize();
                Assert.AreEqual(0, ScoreSystem.Score, $"第 {cycle} 局开局分数应为 0");

                EventBus<EnemyKilled>.Publish(new EnemyKilled(10));
                Assert.AreEqual(10, ScoreSystem.Score, $"第 {cycle} 局：一次击杀只计分一次，且倍率从 1 开始");
                Assert.AreEqual("10", label.Text, $"第 {cycle} 局：本局的分数显示应刷新");

                ScoreSystem.ActivateDoubleScore();
                EventBus<EnemyKilled>.Publish(new EnemyKilled(10));
                Assert.AreEqual(30, ScoreSystem.Score, $"第 {cycle} 局：双倍道具只影响之后的击杀");
                Assert.AreEqual("30", label.Text);

                Record("计分", cycle, $"score={ScoreSystem.Score} label={label.Text}");
                yield return LeavePlay();
            }
        }
    }
}
