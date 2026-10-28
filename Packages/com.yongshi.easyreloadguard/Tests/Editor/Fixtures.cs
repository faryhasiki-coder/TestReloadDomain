using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// 夹具位于测试程序集（引用 nunit），项目的重置计划与检查会排除它们，只由测试显式使用。
namespace EasyReloadGuard.Editor.Tests
{
    static class ResetFixture
    {
        public static int Counter;
        public static int Seeded = 5;
        public static string Label = "init";
        public static readonly List<int> Items = new List<int>();
        public static readonly int Constant = 1;
        public static List<int> Mutable = new List<int>();
        public static readonly List<string> Calls = new List<string>();

        public static event Action Changed;

        public static int Property { get; set; }

        [ResetStatic] public static int AutoProp { get; set; }

        static int s_Manual;
        public static int ManualProp { get => s_Manual; set => s_Manual = value; }

#pragma warning disable CS0067
        [ResetStatic] public static event Action AutoEvent;
#pragma warning restore CS0067

        static Action s_CustomEventBacking;
        public static event Action CustomEvent { add => s_CustomEventBacking += value; remove => s_CustomEventBacking -= value; }
        public static bool HasAutoEventSubscribers => AutoEvent != null;
        public static void ClearAutoEvent() => AutoEvent = null;

        public static bool HasChangedSubscribers => Changed != null;

        public static void ClearChanged() => Changed = null;

        public static void ResetLate() => Calls.Add("late");

        public static void ResetEarly() => Calls.Add("early");

        public static async void AsyncReset() => await Task.Yield();

        public static void WithArgument(int value) => Calls.Add(value.ToString());

        public static int ReturnsValue() => 0;
    }

    sealed class InstanceFixture
    {
        public int Value;

        public void Reset() => Value = 0;
    }

    /// <summary>[ResetStatic] 误用在实例属性/事件上，生产发现入口应报告 RG010。</summary>
    sealed class InstanceAttributeFixture
    {
        [ResetStatic] public int Count { get; set; }

#pragma warning disable CS0067
        [ResetStatic] public event Action Changed;
#pragma warning restore CS0067
    }

    class GenericHolder<T>
    {
        public static T Value;
    }

    sealed class StringHolder : GenericHolder<string>
    {
    }

    static class AuditFixture
    {
#pragma warning disable CS0067
        public static event Action Changed;
#pragma warning restore CS0067
        public static GameObject Instance;
        public static int Counter;
        public static readonly List<int> Items = new List<int>();
        public static readonly int[] Table = { 1, 2 };
        public static readonly int Constant = 1;
        public const int Literal = 1;

        [ReloadSafe("测试：有意跨会话保留")]
        public static int Intentional;

        [ReloadSafe(" ")]
        public static int MissingReason;

        public static int AutoProperty { get; set; }

        public static CancellationTokenSource Cancellation;
        public static Task Pending;
        public static readonly Timer Ticker = null;

        public static async void FireAndForget() => await Task.Yield();

        public static async Task ReturnsTask() => await Task.Yield();
    }

    [ReloadSafe("测试：整体安全")]
    static class SafeFixture
    {
        public static int Value;
    }

    static class OwnerFixture
    {
        public static int Value;

        public static void Reset() => Value = 0;
    }

    static class LegacyFixture
    {
        public static int Value;

        [InitializeOnEnterPlayMode]
        static void Reset() => Value = 0;
    }

    abstract class TestAdapterBase : ReloadAdapter
    {
        public override string Target => typeof(TestAdapterBase).Assembly.GetName().Name;

        public override AdapterTargetKind TargetKind => AdapterTargetKind.Assembly;

        public override string[] VerifiedVersions => new[] { typeof(TestAdapterBase).Assembly.GetName().Version.ToString() };

        protected const string FixtureType = "EasyReloadGuard.Editor.Tests.ResetFixture, EasyReloadGuard.Editor.Tests";
    }

    sealed class WorkingAdapter : TestAdapterBase
    {
        public override void Configure(ReloadAdapterBuilder builder)
        {
            builder.ResetField(FixtureType, nameof(ResetFixture.Counter));
            builder.OnReset("记录调用", () => ResetFixture.Calls.Add("adapter"));
            builder.RequireDomainReload("测试兜底");
        }
    }

    sealed class BrokenAdapter : TestAdapterBase
    {
        public override void Configure(ReloadAdapterBuilder builder)
        {
            builder.ResetField("EasyReloadGuard.Editor.Tests.DoesNotExist, EasyReloadGuard.Editor.Tests", "Value");
            builder.ResetField(FixtureType, "Missing");
            builder.SelfHandled(" ");
        }
    }

    sealed class KnownIssueAdapter : TestAdapterBase
    {
        public override void Configure(ReloadAdapterBuilder builder)
        {
            builder.SelfHandled("测试依据");
            builder.KnownIssue(FixtureType, nameof(ResetFixture.Counter), "测试：已知会影响运行逻辑的静态字段");
            builder.KnownIssue(FixtureType, "Missing", "测试：成员不存在");
        }
    }

    sealed class UnverifiedAdapter : TestAdapterBase
    {
        public override string[] VerifiedVersions => new[] { "never" };

        public override void Configure(ReloadAdapterBuilder builder) => builder.SelfHandled("测试依据");
    }

    sealed class DormantAdapter : ReloadAdapter
    {
        public override string Target => "com.example.not-installed";

        public override string[] VerifiedVersions => new[] { "1.0.0" };

        public override void Configure(ReloadAdapterBuilder builder) => throw new InvalidOperationException("目标不存在时不应调用");
    }
}
