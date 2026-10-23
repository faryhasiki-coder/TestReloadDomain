using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DomainReloadCases.Tests
{
    /// <summary>
    /// R1 三轮真实 Play 回调测试。不是四种重载组合的完整验收，未在本次补例中执行。
    /// 项目中其他 InitialValue 声明仍要求首次 Play 在 fresh domain 下捕获基线，不能绕过关口。
    /// </summary>
    public sealed class StateOwnershipTests : ReloadCaseTestBase
    {
        [TearDown]
        public void ReleaseOwnedState()
        {
            StateOwnershipCase.ReleaseTestResources();
            RegisteredGenericState<int>.ReleaseTestValue();
            RegisteredGenericState<string>.ReleaseTestValue();
        }

        [UnityTest, Description("ST-01：每局计数与初始化标志，R1 三轮；重复 InitializeOnce 不重复初始化")]
        public IEnumerator ST01_InitializationFlagAndCounterRestartForThreeSessions()
        {
            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return EnterFastPlay();
                Assert.AreEqual(0, StateOwnershipCase.SessionCount);
                Assert.IsFalse(StateOwnershipCase.IsInitialized);
                Assert.AreEqual(0, StateOwnershipCase.InitializationCount);
                StateOwnershipCase.InitializeOnce();
                StateOwnershipCase.InitializeOnce();
                Assert.IsTrue(StateOwnershipCase.IsInitialized);
                Assert.AreEqual(1, StateOwnershipCase.InitializationCount);
                StateOwnershipCase.SessionCount = 100 + cycle;
                yield return LeavePlay();
            }
        }

        [UnityTest, Description("ST-02：已销毁 Unity 对象托管壳不会阻止单例重建，R1 三轮")]
        public IEnumerator ST02_DestroyedUnityReferenceDoesNotPreventRecreation()
        {
            GameObject previous = null;
            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return EnterFastPlay();
                if (cycle > 0)
                {
                    Assert.IsFalse(ReferenceEquals(previous, null), "保留的是旧对象托管引用");
                    Assert.IsTrue(previous == null, "上一局场景对象已销毁");
                }
                Assert.IsTrue(StateOwnershipCase.Singleton == null);
                var first = StateOwnershipCase.EnsureSingleton();
                Assert.AreSame(first, StateOwnershipCase.EnsureSingleton(), "同局只有一个 owner");
                UnityEngine.Object.Destroy(first);
                yield return null;
                Assert.IsTrue(first == null);
                Assert.IsFalse(ReferenceEquals(first, null));
                Assert.AreSame(first, StateOwnershipCase.Singleton, "故意保留已销毁的壳来考验判空");
                var replacement = StateOwnershipCase.EnsureSingleton();
                Assert.IsTrue(replacement != null);
                Assert.AreNotSame(first, replacement);
                Assert.AreSame(replacement, StateOwnershipCase.EnsureSingleton());
                previous = replacement;
                yield return LeavePlay();
            }
        }

        [UnityTest, Description("ST-03：Default/null、readonly Clear、重建及外部别名，R1 三轮")]
        public IEnumerator ST03_CollectionStrategiesHaveDifferentAliasContracts()
        {
            var clearAlias = StateOwnershipCase.ClearOnEntry;
            List<int> replaceAlias = null;
            List<int> nullAlias = null;
            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return EnterFastPlay();
                Assert.IsNull(StateOwnershipCase.NullOnEntry);
                Assert.AreSame(clearAlias, StateOwnershipCase.ClearOnEntry);
                Assert.IsEmpty(clearAlias);
                Assert.IsEmpty(StateOwnershipCase.ReplaceOnEntry);
                if (cycle > 0)
                {
                    Assert.AreNotSame(replaceAlias, StateOwnershipCase.ReplaceOnEntry);
                    Assert.IsEmpty(replaceAlias, "owner 重建同时让旧别名丢弃上一局数据");
                    Assert.AreEqual(1, nullAlias.Count, "写 null 不会清空外部持有的旧集合");
                }
                StateOwnershipCase.NullOnEntry = new List<int> { cycle + 1 };
                nullAlias = StateOwnershipCase.NullOnEntry;
                StateOwnershipCase.ClearOnEntry.Add(cycle + 1);
                StateOwnershipCase.ReplaceOnEntry.Add(cycle + 1);
                replaceAlias = StateOwnershipCase.ReplaceOnEntry;
                Assert.AreEqual(1, clearAlias.Count, "Clear 后同一实例仍能继续使用");
                yield return LeavePlay();
            }
        }

        [UnityTest, Description("ST-04：field-target 静态自动属性与两种直接闭合泛型的显式登记，R1 三轮")]
        public IEnumerator ST04_PropertyAndRegisteredClosedGenericsResetEverySession()
        {
            // 首轮也登记一份污染，证明不是恰好从 CLR 默认值起步。
            RegisteredGenericState<int>.Set(81);
            RegisteredGenericState<string>.Set(82);
            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return EnterFastPlay();
                Assert.AreEqual(0, StateOwnershipCase.SessionProperty);
                Assert.AreEqual(0, RegisteredGenericState<int>.Value);
                Assert.AreEqual(0, RegisteredGenericState<string>.Value);
                StateOwnershipCase.SessionProperty = 90 + cycle;
                RegisteredGenericState<int>.Set(100 + cycle);
                RegisteredGenericState<int>.Set(200 + cycle);
                RegisteredGenericState<string>.Set(300 + cycle);
                Assert.AreEqual(200 + cycle, RegisteredGenericState<int>.Value);
                Assert.AreEqual(300 + cycle, RegisteredGenericState<string>.Value);
                yield return LeavePlay();
            }
        }

        [UnityTest, Description("ST-05：纯计算缓存/偏好保留，每局状态重置，R1 三轮")]
        public IEnumerator ST05_RetainedPureCacheAndPreferenceHaveSeparateOwnership()
        {
            const int key = 191;
            var hadValue = StateOwnershipCase.CalculationCache.TryGetValue(key, out var oldValue);
            var oldPreference = StateOwnershipCase.Preference;
            try
            {
                StateOwnershipCase.CalculationCache.Remove(key);
                Assert.AreEqual(key * key, StateOwnershipCase.CalculateSquare(key));
                StateOwnershipCase.Preference = 29;
                var cacheAlias = StateOwnershipCase.CalculationCache;
                for (var cycle = 0; cycle < 3; cycle++)
                {
                    yield return EnterFastPlay();
                    Assert.AreSame(cacheAlias, StateOwnershipCase.CalculationCache);
                    Assert.AreEqual(key * key, StateOwnershipCase.CalculationCache[key]);
                    Assert.AreEqual(29, StateOwnershipCase.Preference);
                    Assert.AreEqual(0, StateOwnershipCase.SessionCount);
                    StateOwnershipCase.SessionCount = cycle + 10;
                    yield return LeavePlay();
                }
            }
            finally
            {
                StateOwnershipCase.Preference = oldPreference;
                if (hadValue) StateOwnershipCase.CalculationCache[key] = oldValue;
                else StateOwnershipCase.CalculationCache.Remove(key);
            }
        }

        [UnityTest, Description("OBJ-01：每局 new 的普通状态树和由 static 持有的状态根，R1 三轮")]
        public IEnumerator OBJ01_NewTreesNeedNoFieldAnnotationsAndOldRootsRejectWrites()
        {
            SessionStateTree previousRoot = null;
            for (var cycle = 0; cycle < 3; cycle++)
            {
                yield return EnterFastPlay();
                using (var localRoot = new SessionStateTree())
                {
                    Assert.AreEqual(0, localRoot.Count);
                    Assert.AreEqual(0, localRoot.Child.Score);
                    Assert.IsTrue(localRoot.TryAdd(10));
                    Assert.AreEqual(10, localRoot.Child.Score);
                }
                var current = StateOwnershipCase.RetainedRoot;
                Assert.AreEqual(0, current.Count);
                Assert.AreEqual(0, current.Child.Score);
                Assert.IsFalse(current.IsDisposed);
                if (previousRoot != null)
                {
                    Assert.AreNotSame(previousRoot, current);
                    Assert.IsTrue(previousRoot.IsDisposed);
                    Assert.IsFalse(previousRoot.TryAdd(100), "上一局的写入口已关闭");
                    Assert.AreEqual(0, previousRoot.SubscriberCount);
                    Assert.AreEqual(0, current.Count, "旧 owner 不能污染新树");
                }
                var calls = 0;
                current.Changed += () => calls++;
                Assert.IsTrue(current.TryAdd(cycle + 1));
                Assert.AreEqual(1, calls);
                previousRoot = current;
                yield return LeavePlay();
                Assert.IsTrue(current.IsDisposed);
                Assert.AreEqual(0, current.SubscriberCount);
                Assert.IsFalse(current.TryAdd(20));
                Assert.AreEqual(1, calls, "退出后不再向旧订阅发布事件");
            }
        }
    }
}
