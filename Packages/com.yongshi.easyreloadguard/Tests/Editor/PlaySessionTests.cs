using System;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EasyReloadGuard.Editor.Tests
{
    public class PlaySessionTests
    {
        /// <summary>
        /// 不影响出包逻辑：运行时程序集对业务公开的成员只能是 Conditional("UNITY_EDITOR") 的 void 命令，
        /// 不提供可读取的会话状态（令牌、会话标记等），业务无法据此改变取消、回写或释放流程。
        /// </summary>
        [Test]
        public void PublicSurface_IsOnlyEditorConditionalCommands()
        {
            const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var type = typeof(PlaySession);
            CollectionAssert.IsEmpty(type.GetProperties(publicStatic), "PlaySession 不应公开属性");
            CollectionAssert.IsEmpty(type.GetFields(publicStatic), "PlaySession 不应公开字段");
            CollectionAssert.IsEmpty(type.GetEvents(publicStatic), "PlaySession 不应公开事件");

            var methods = type.GetMethods(publicStatic);
            CollectionAssert.IsNotEmpty(methods);
            foreach (var method in methods)
            {
                Assert.AreEqual(typeof(void), method.ReturnType, $"{method.Name} 应返回 void");
                var conditional = method.GetCustomAttribute<ConditionalAttribute>();
                Assert.IsNotNull(conditional, $"{method.Name} 应带 Conditional");
                Assert.AreEqual("UNITY_EDITOR", conditional.ConditionString, method.Name);
            }

            Assert.IsNull(type.Assembly.GetType("EasyReloadGuard.PlaySessionStamp"), "运行时程序集不应再提供会话标记类型");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RunExit_FailureIsSummarizedAndFollowsReloadSetting(bool reloadOnResetFailure)
        {
            var settings = ReloadGuardSettings.instance;
            var previous = settings.reloadOnResetFailure;
            LogAssert.ignoreFailingMessages = true; // 失败汇总按设计输出错误日志
            ForceReload.TakePendingReason(new ResetPlan());
            try
            {
                settings.reloadOnResetFailure = reloadOnResetFailure;
                var released = false;
                var plan = new ResetPlan();
                plan.AddAction("测试", "释放失败", () => throw new InvalidOperationException("释放失败"), 0, false);
                plan.AddAction("测试", "释放", () => released = true, 1, false);
                plan.Seal();

                ResetPipeline.RunExit(plan);

                Assert.IsTrue(released, "一个释放步骤失败不能阻断后续释放步骤");
                var reason = ForceReload.TakePendingReason(new ResetPlan());
                if (reloadOnResetFailure) StringAssert.Contains("退出 Play 释放失败", reason);
                else Assert.IsNull(reason);
            }
            finally
            {
                settings.reloadOnResetFailure = previous;
            }
        }

        [Test]
        public void RegisterReset_IsTakenOnce()
        {
            var registry = new ResetRegistry();
            var calls = 0;
            registry.Add(() => calls++);

            foreach (var reset in registry.TakeForEnter()) reset();
            Assert.AreEqual(1, calls);
            Assert.IsEmpty(registry.TakeForEnter());
        }

        [Test]
        public void PersistentReset_IsTakenEveryEnterEvenAfterFailure()
        {
            var registry = new ResetRegistry();
            var calls = 0;
            registry.AddPersistent(() =>
            {
                calls++;
                if (calls == 1) throw new InvalidOperationException("第一次执行失败");
            });

            for (var cycle = 1; cycle <= 3; cycle++)
            {
                var resets = registry.TakeForEnter();
                Assert.AreEqual(1, resets.Count, $"第 {cycle} 次进入 Play 仍应执行持久登记");
                try
                {
                    resets[0]();
                }
                catch (InvalidOperationException)
                {
                }
            }

            Assert.AreEqual(3, calls);
        }

        [Test]
        public void PersistentReset_DuplicateRegistrationIsKeptOnceAndCanBeUnregistered()
        {
            var registry = new ResetRegistry();
            var owner = new ResetCounter();
            registry.AddPersistent(owner.Reset);
            registry.AddPersistent(owner.Reset);
            Assert.AreEqual(1, registry.PersistentCount, "同一目标与方法只保留一份");

            registry.RemovePersistent(owner.Reset);
            Assert.AreEqual(0, registry.PersistentCount);
            Assert.IsEmpty(registry.TakeForEnter());
        }

        [Test]
        public void PersistentReset_IsRemovedWhenOwnerIsDestroyed()
        {
            var registry = new ResetRegistry();
            var owner = new GameObject("PersistentResetOwner").transform;
            registry.AddPersistent(owner.DetachChildren);
            Assert.AreEqual(1, registry.TakeForEnter().Count);

            Object.DestroyImmediate(owner.gameObject);

            Assert.IsEmpty(registry.TakeForEnter(), "owner 销毁后不应再执行");
            Assert.AreEqual(0, registry.PersistentCount, "owner 销毁后登记表不应继续持有");
        }

        [Test]
        public void RegisterPersistentReset_ThrowsOnNull()
        {
            Assert.Throws<ArgumentNullException>(() => PlaySession.RegisterPersistentReset(null));
            Assert.Throws<ArgumentNullException>(() => PlaySession.UnregisterPersistentReset(null));
        }

        sealed class ResetCounter
        {
            public int Calls;

            public void Reset() => Calls++;
        }
    }
}
