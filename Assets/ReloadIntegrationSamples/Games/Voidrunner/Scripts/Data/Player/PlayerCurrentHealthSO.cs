using UnityEngine;

namespace Data.Player
{
    [CreateAssetMenu(fileName = "PlayerCurrentHealth", menuName = "Player/CurrentHealth", order = 0)]
    public class PlayerCurrentHealthSO : ScriptableObject
    {
        [SerializeField] private int currentHealth;
        public int CurrentHealth => currentHealth;

        public void ResetHealth(int health)
        {
            currentHealth = Mathf.Max(1, health);
        }

        public bool TakeDamage(int damage)
        {
            currentHealth -= damage;
            if (currentHealth >= 0) return true;
            
            currentHealth = 0;
            return false;
        }

        public void Kill()
        {
            currentHealth = 0;
        }
    }
}