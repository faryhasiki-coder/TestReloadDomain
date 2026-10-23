using System;
using System.Collections.Generic;
using EasyReloadGuard;
using UnityEngine;

namespace DomainReloadCases
{
    /// <summary>
    /// INIT-01/02：仅在测试显式 Arm 后记录 Unity 实际初始化回调。
    /// 此探针针对 R1；普通编辑、其他案例和 Player 启动不会创建业务对象。
    /// </summary>
    [ReloadSafe("初始化案例的跨域/跨会话观测值；测试通过 Arm/Disarm 管理，运行时阶段自行重置业务状态")]
    public sealed class InitializationOrderCase : MonoBehaviour
    {
        static readonly List<string> s_Phases = new List<string>();
        static readonly List<string> s_DependencyOrder = new List<string>();
        static bool s_Armed;
        static int s_SavedConstructorSeed;

        static InitializationOrderCase()
        {
            StaticConstructorCount++;
            DomainIdentity = Guid.NewGuid().ToString("N");
            ConstructorSeed = 17;
        }

        public static int StaticConstructorCount { get; private set; }
        public static string DomainIdentity { get; private set; }
        public static int ConstructorSeed { get; set; }
        public static int SessionInitializationCount { get; private set; }
        public static int BusinessValue { get; private set; }
        public static int BusinessValueObservedByAwake { get; private set; }
        public static bool BReady { get; private set; }
        public static int BValue { get; private set; }
        public static int AValue { get; private set; }
        public static bool AObservedReadyB { get; private set; }
        public static bool AwakeObservedReadyDependencies { get; private set; }
        public static IReadOnlyList<string> Phases => s_Phases;
        public static IReadOnlyList<string> DependencyOrder => s_DependencyOrder;

        public static void Arm()
        {
            if (s_Armed) return;
            s_SavedConstructorSeed = ConstructorSeed;
            SessionInitializationCount = 0;
            s_Phases.Clear();
            s_DependencyOrder.Clear();
            s_Armed = true;
        }

        public static void Disarm()
        {
            if (!s_Armed) return;
            s_Armed = false;
            ConstructorSeed = s_SavedConstructorSeed;
            s_Phases.Clear();
            s_DependencyOrder.Clear();
            SessionInitializationCount = 0;
            BusinessValue = BusinessValueObservedByAwake = 0;
            BReady = AObservedReadyB = AwakeObservedReadyDependencies = false;
            BValue = AValue = 0;
        }

        public static void MutateBusinessState()
        {
            BusinessValue = -100;
            BReady = false;
            BValue = AValue = -100;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InitializeSession()
        {
            if (!s_Armed) return;
            s_Phases.Clear();
            s_Phases.Add("SubsystemRegistration");
            SessionInitializationCount++;
            BusinessValue = 42;
            BusinessValueObservedByAwake = 0;
            AwakeObservedReadyDependencies = false;
            // 不清理 DependencyOrder：本方法和包入口处于同一阶段，不能假设二者的先后。
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void AfterAssembliesLoaded()
        {
            if (s_Armed) s_Phases.Add("AfterAssembliesLoaded");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void BeforeSplashScreen()
        {
            if (s_Armed) s_Phases.Add("BeforeSplashScreen");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void BeforeSceneLoad()
        {
            if (!s_Armed) return;
            s_Phases.Add("BeforeSceneLoad");
            var business = new GameObject("InitializationOrderCase.Business") { hideFlags = HideFlags.DontSave };
            business.AddComponent<InitializationOrderCase>(); // Awake 由 Unity 调度，测试不直接调用。
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterSceneLoad()
        {
            if (s_Armed) s_Phases.Add("AfterSceneLoad");
        }

        void Awake()
        {
            if (!s_Armed) return;
            s_Phases.Add("Awake");
            BusinessValueObservedByAwake = BusinessValue;
            AwakeObservedReadyDependencies = BReady && AObservedReadyB && AValue == 43;
        }

        [ResetMethod(-200)]
        static void InitializeDependencyB()
        {
            if (!s_Armed) return;
            s_DependencyOrder.Clear();
            BReady = true;
            BValue = 42;
            s_DependencyOrder.Add("B");
        }

        [ResetMethod(-100)]
        static void InitializeDependentA()
        {
            if (!s_Armed) return;
            AObservedReadyB = BReady;
            AValue = BReady ? BValue + 1 : -1;
            s_DependencyOrder.Add("A");
        }
    }
}
