using System.Collections.Generic;

namespace ReloadIntegrationSamples
{
    /// <summary>玩家背包：开局金币 100，购买后扣款并放入背包。</summary>
    public sealed class Inventory : Singleton<Inventory>
    {
        public const int StartingGold = 100;

        readonly List<string> m_Items = new List<string>();

        public int Gold { get; private set; } = StartingGold;
        public IReadOnlyList<string> Items => m_Items;

        public bool TryBuy(string item, int price)
        {
            if (Gold < price) return false;
            Gold -= price;
            m_Items.Add(item);
            return true;
        }
    }
}
