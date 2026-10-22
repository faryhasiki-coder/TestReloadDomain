using Faryhasiki.PlaySessions;

[assembly: PlaySessionAssembly]
[assembly: PlaySessionAdapter(typeof(PlaySessions.Sample.LegacyAdapter))]

namespace PlaySessions.Sample
{
    public static class NewWallet
    {
        [ResetOnPlay] public static int Coins;
        public static void CollectCoin() => Coins++;
    }

    public static class OldWallet
    {
        public static int Coins = 10;
        public static void CollectCoin() => Coins++;
        public static void Reset() => Coins = 10;
    }

    public sealed class LegacyAdapter : IPlaySessionAdapter
    {
        public void Configure(SessionPlan plan)
        {
            plan.CoverStatics(typeof(OldWallet), "sample.wallet", "Reset restores initial balance; no resources owned.");
            plan.Add("sample.wallet", _ => OldWallet.Reset());
        }
    }
}
