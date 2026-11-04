using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ReloadIntegrationSamples.Tests
{
    /// <summary>
    /// INT-05 ExecuteAlways：编辑态预览写入的静态值不应带进 Play。
    /// 每轮退出 Play 后，场景恢复出的编辑态对象会再次预览，因此每局进入前都有一次真实的编辑态写入。
    /// </summary>
    [Explicit(IntegrationAcceptance.ExplicitReason), Category(IntegrationAcceptance.Category)]
    public sealed class ExecuteAlwaysAcceptanceTests : IntegrationAcceptanceTestBase
    {
        const string k_ObjectName = "IntegrationSamples.ScorePreview";

        /// <summary>先退出 Play，再删除测试放进编辑场景的对象，不在用户场景中留下残留。</summary>
        [UnityTearDown]
        public IEnumerator RemovePreviewObject()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            for (var item = GameObject.Find(k_ObjectName); item != null; item = GameObject.Find(k_ObjectName))
                Object.DestroyImmediate(item);
        }

        [UnityTest]
        public IEnumerator EditPreviewDoesNotLeakIntoGameplayScore()
        {
            new GameObject(k_ObjectName).AddComponent<ExecuteAlwaysCaseCounter>();

            for (var cycle = 1; cycle <= Cycles; cycle++)
            {
                var preview = FindPreview();
                yield return WaitUntil(() => ExecuteAlwaysCaseCounter.GameplayScore == preview.PreviewScore, 10);
                Assert.AreEqual(preview.PreviewScore, ExecuteAlwaysCaseCounter.GameplayScore,
                    $"第 {cycle} 局前：编辑态预览应已运行（前置条件，不是被测结果）");

                yield return EnterFastPlay();
                AssertSameDomain(cycle);

                Assert.AreEqual(ExecuteAlwaysCaseCounter.DefaultScore, ExecuteAlwaysCaseCounter.GameplayScore,
                    $"第 {cycle} 局：运行时得分应从默认分开始，不能带入编辑态预览值");
                var playing = FindPreview();
                Assert.IsTrue(Application.IsPlaying(playing.gameObject));
                playing.AddPoint();
                Assert.AreEqual(ExecuteAlwaysCaseCounter.DefaultScore + 1, ExecuteAlwaysCaseCounter.GameplayScore);

                Record("ExecuteAlways", cycle, $"score={ExecuteAlwaysCaseCounter.GameplayScore}");
                yield return LeavePlay();
            }
        }

        static ExecuteAlwaysCaseCounter FindPreview()
        {
            var item = GameObject.Find(k_ObjectName);
            Assert.IsNotNull(item, "未找到测试放置的 ExecuteAlways 对象");
            return item.GetComponent<ExecuteAlwaysCaseCounter>();
        }
    }
}
