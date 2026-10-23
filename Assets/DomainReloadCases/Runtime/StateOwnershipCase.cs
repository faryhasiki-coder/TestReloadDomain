using System;
using System.Collections.Generic;
using EasyReloadGuard;
using UnityEngine;

namespace DomainReloadCases
{
    /// <summary>
    /// ST-01~05、OBJ-01 的业务状态。字段重置与 owner 重建分开声明；测试只经过实际 Play 入口触发。
    /// 本案例不用 InitialValue：该模式记录本域首次 Play 的值，首次观测必须来自 fresh domain，
    /// 不能为了测试通过跳过项目现有 InitialValue 的干净域关口。
    /// </summary>
    public static class StateOwnershipCase
    {
        [ResetStatic] public static int SessionCount;
        [ResetStatic] public static bool IsInitialized;
        [ResetStatic] public static int InitializationCount;
        [ResetStatic] public static List<int> NullOnEntry;
        [ResetStatic(ResetMode.Clear)] public static readonly List<int> ClearOnEntry = new List<int>();
        [field: ResetStatic] public static int SessionProperty { get; set; }

        public static List<int> ReplaceOnEntry { get; private set; } = new List<int>();
        public static SessionStateTree RetainedRoot { get; private set; } = new SessionStateTree();
        public static GameObject Singleton { get; private set; }

        [ReloadSafe("只保存 int→int 纯计算结果，不保存场景对象，跨 Play 保留")]
        public static readonly Dictionary<int, int> CalculationCache = new Dictionary<int, int>();
        [ReloadSafe("案例中的用户偏好，有意跨 Play 保留，测试负责恢复原值")]
        public static int Preference = 7;

        public static void InitializeOnce()
        {
            if (IsInitialized) return;
            IsInitialized = true;
            InitializationCount++;
        }

        public static GameObject EnsureSingleton()
        {
            // 使用 Unity 的存活判空；ReferenceEquals 仍可能拿到已销毁对象的托管壳。
            if (Singleton == null) Singleton = new GameObject("StateOwnershipCase.Singleton");
            return Singleton;
        }

        public static int CalculateSquare(int value)
        {
            if (!CalculationCache.TryGetValue(value, out var result))
            {
                result = value * value;
                CalculationCache.Add(value, result);
            }
            return result;
        }

        [ResetMethod]
        static void RebuildOwnedState()
        {
            // 旧列表也清空：旧别名不可继续拿到上一局数据；新 owner 接收新列表。
            ReplaceOnEntry.Clear();
            ReplaceOnEntry = new List<int>();
            RetainedRoot?.Dispose();
            RetainedRoot = new SessionStateTree();
            Singleton = null;
        }

        [OnExitPlayMode]
        static void ReleaseSessionTree()
        {
            // 退出就关闭旧入口，不等下一局才让旧事件/调用者失效。
            RetainedRoot?.Dispose();
        }

        /// <summary>测试收尾入口，只释放案例拥有的资源；不是被测进入 Play 的 reset 路径。</summary>
        public static void ReleaseTestResources()
        {
            RetainedRoot?.Dispose();
            NullOnEntry = null;
            ClearOnEntry.Clear();
            ReplaceOnEntry.Clear();
            if (Singleton != null) UnityEngine.Object.DestroyImmediate(Singleton);
            Singleton = null;
        }
    }

    public sealed class SessionStateTree : IDisposable
    {
        public sealed class ChildState { public int Score; }

        public readonly ChildState Child = new ChildState();
        public int Count { get; private set; }
        public bool IsDisposed { get; private set; }
        public event Action Changed;
        public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;

        public bool TryAdd(int value)
        {
            if (IsDisposed) return false;
            Count += value;
            Child.Score += value;
            Changed?.Invoke();
            return true;
        }

        public void Dispose()
        {
            IsDisposed = true;
            Changed = null;
        }
    }

    /// <summary>
    /// 直接使用的闭合泛型不会都被静态扫描发现；首次写入登记下一次 Play 的重置。
    /// RegisterReset 是一次性队列，每次真正执行后解除登记标记，下一局写入必须重新登记。
    /// </summary>
    [ReloadSafe("闭合泛型由首次写入时的 PlaySession.RegisterReset 显式登记，不依赖扫描发现")]
    public static class RegisteredGenericState<T>
    {
        static bool s_Registered;
        public static int Value { get; private set; }

        public static void Set(int value)
        {
            if (!s_Registered)
            {
                PlaySession.RegisterReset(ResetNextSession);
                s_Registered = true;
            }
            Value = value;
        }

        static void ResetNextSession()
        {
            Value = 0;
            s_Registered = false;
        }

        public static void ReleaseTestValue() => Value = 0;
    }
}
