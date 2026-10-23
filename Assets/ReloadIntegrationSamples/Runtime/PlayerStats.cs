using System;
using UnityEngine;

namespace ReloadIntegrationSamples
{
    /// <summary>玩家数值资产：MaxHealth 由策划配置，CurrentHealth 是运行时血量，资产加载时回满。</summary>
    [CreateAssetMenu(menuName = "Domain Reload Samples/Player Stats")]
    public sealed class PlayerStats : ScriptableObject
    {
        public int MaxHealth = 100;
        [NonSerialized] public int CurrentHealth;

        void OnEnable() => CurrentHealth = MaxHealth;

        public void TakeDamage(int amount) => CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
    }
}
