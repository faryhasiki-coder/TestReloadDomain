using System;
using System.Collections.Generic;
using Faryhasiki.PlaySessions;

[assembly: PlaySessionAssembly]
[assembly: PlaySessionAdapter(typeof(PlaySessionDemo.LegacyAdapter))]

namespace PlaySessionDemo
{
    // New scalar state: one annotation; reset to default(int), not the field initializer.
    public static class Wallet
    {
        [ResetOnPlay] public static int Coins;
        public static void CollectCoin() => Coins++;
    }

    [SessionOwned("demo.inventory")]
    public static class Inventory
    {
        public static readonly List<string> Items = new List<string>();
        public static int EventCount;

        [SessionReset("demo.inventory", "demo.legacy")]
        static void Reset(SessionScope scope)
        {
            Items.Clear(); EventCount = 0;
            LegacyBus.Changed += OnChanged;
            scope.OnStop(() => LegacyBus.Changed -= OnChanged);
        }

        static void OnChanged() => EventCount++;
    }

    // Represents old/third-party source. It has no reference to Play Sessions.
    public static class LegacyBus
    {
        public static int Count;
        public static event Action Changed;
        public static void Publish() { Count++; Changed?.Invoke(); }
        public static void Reset() { Count = 0; Changed = null; }
        public static int Subscribers => Changed?.GetInvocationList().Length ?? 0;
    }

    public sealed class LegacyAdapter : IPlaySessionAdapter
    {
        public void Configure(SessionPlan plan)
        {
            plan.CoverStatics(typeof(LegacyBus), "demo.legacy", "Demo Reset clears count and delegates; repeated Play test covers both.");
            plan.Add("demo.legacy", scope =>
            {
                LegacyBus.Reset();
                scope.OnStop(LegacyBus.Reset);
            });
        }
    }
}
