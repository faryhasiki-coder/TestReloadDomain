using Faryhasiki.PlaySessions;

namespace PlaySessionDemo.CodeComparison.After
{
    public static class CoinWallet
    {
        // 新增：声明归属。属性本身不会自动重置，仍需注册下面的 ResetForPlay。
        [field: SessionOwned("sample.wallet")]
        public static int Coins { get; private set; }

        public static void CollectCoin() => Coins++;

        // 新增：明确指定这个业务状态在每次 Play 开始时的初值。
        internal static void ResetForPlay() => Coins = 0;
    }
}
