namespace PlaySessionDemo.CodeComparison.Before
{
    // 原始业务代码：没有引用或接入 Play Sessions。
    public static class CoinWallet
    {
        public static int Coins { get; private set; }

        public static void CollectCoin() => Coins++;
    }
}
