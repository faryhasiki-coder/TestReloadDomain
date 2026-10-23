using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    [Category("Case.Initialization")]
    public sealed class InitializationOrderTests : ReloadCaseTestBase
    {
        [TearDown]
        public void DisarmInitializationProbe()
        {
            InitializationOrderCase.Disarm();
        }

        [UnityTest]
        [Description("INIT-01 / R1：连续三轮真实 Play，静态构造保持同域一次；每轮原生初始化阶段在业务 Awake 前重建业务状态。")]
        public IEnumerator RuntimePhasesReinitializeWhileStaticConstructorDoesNotRerun()
        {
            InitializationOrderCase.Arm();
            var domain = InitializationOrderCase.DomainIdentity;
            var constructorSeed = InitializationOrderCase.ConstructorSeed;
            Assert.AreEqual(1, InitializationOrderCase.StaticConstructorCount);

            for (var cycle = 1; cycle <= 3; cycle++)
            {
                yield return EnterFastPlay();
                Assert.AreEqual(domain, InitializationOrderCase.DomainIdentity, "R1 应保持同一托管域。");
                Assert.AreEqual(1, InitializationOrderCase.StaticConstructorCount, "静态构造函数不能被当作每局入口。");
                Assert.AreEqual(constructorSeed, InitializationOrderCase.ConstructorSeed,
                    "未重置的构造状态应跨 Fast Play 保留；第二轮不能重新得到字段初始值。");
                Assert.AreEqual(cycle, InitializationOrderCase.SessionInitializationCount);
                Assert.AreEqual(42, InitializationOrderCase.BusinessValueObservedByAwake);
                CollectionAssert.AreEqual(new[]
                {
                    "SubsystemRegistration", "AfterAssembliesLoaded", "BeforeSplashScreen",
                    "BeforeSceneLoad", "Awake", "AfterSceneLoad"
                }, InitializationOrderCase.Phases,
                    "Awake 对象由 BeforeSceneLoad 阶段创建；这里只验证跨阶段顺序，不规定同阶段方法顺序。");

                constructorSeed = 100 + cycle;
                InitializationOrderCase.ConstructorSeed = constructorSeed;
                InitializationOrderCase.MutateBusinessState();
                yield return LeavePlay();
            }
        }

        [UnityTest]
        [Description("INIT-02 / R1：EasyReloadGuard ResetMethod 的显式 Order 令 B 先于 A，连续 Play 后业务 Awake 仍读取已就绪的依赖。")]
        public IEnumerator OrderedResetInitializesDependencyBeforeConsumerEverySession()
        {
            InitializationOrderCase.Arm();
            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return EnterFastPlay();
                CollectionAssert.AreEqual(new[] { "B", "A" }, InitializationOrderCase.DependencyOrder);
                Assert.IsTrue(InitializationOrderCase.AObservedReadyB, "A 不能依赖无顺序保证的同阶段 RuntimeInitialize 方法。");
                Assert.AreEqual(43, InitializationOrderCase.AValue);
                Assert.IsTrue(InitializationOrderCase.AwakeObservedReadyDependencies,
                    "显式依赖初始化必须在真实业务 Awake 前完成。");
                InitializationOrderCase.MutateBusinessState();
                yield return LeavePlay();
            }
        }
    }
}
