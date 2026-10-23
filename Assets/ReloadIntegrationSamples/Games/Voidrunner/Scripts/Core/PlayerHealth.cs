using Data.Player;
using EventSO;
using UnityEngine;

namespace Core
{
    public class PlayerHealth : MonoBehaviour
    {
        [Header("Player Health Settings")]
        [SerializeField] private PlayerMaxHealthSO playerMaxHealthSO;
        [Header("Player Health Data")]
        [SerializeField] private PlayerCurrentHealthSO playerCurrentHealthSO;
        [Header("Events broadcasted")]
        [SerializeField] private VoidEventSO gameOverEventSO;
        [Header("Events received")]
        [SerializeField] private VoidEventSO playerDeathEventSO;
        [SerializeField] private IntEventSO playerDamageEventSO;
        
        private void Awake()
        {
            if (playerMaxHealthSO == null || playerCurrentHealthSO == null)
            {
                Debug.LogError("PlayerHealthSO is not assigned in PlayerHealth.");
                return;
            }
            playerCurrentHealthSO.ResetHealth(playerMaxHealthSO.maxHealth);

            if (gameOverEventSO != null) return;
            
            Debug.LogError("GameOverEventSO is not assigned in PlayerHealth.");
        }

        private void OnEnable()
        {
            if (playerDamageEventSO == null)
            {
                Debug.LogError("PlayerDamageEventSO is not assigned in PlayerHealth.");
                return;
            }
            playerDamageEventSO.OnEvent += (OnDamage);
            
            if (playerDeathEventSO == null)
            {
                Debug.LogError("PlayerDeathEventSO is not assigned in PlayerHealth.");
                return;
            }
            playerDeathEventSO.OnEvent += (OnDeath);
        }

        private void OnDisable()
        {
            if (playerDamageEventSO == null)
            {
                Debug.LogError("PlayerDamageEventSO is not assigned in PlayerHealth.");
                return;
            }
            playerDamageEventSO.OnEvent -= (OnDamage);

            if (playerDeathEventSO == null)
            {
                Debug.LogError("PlayerDeathEventSO is not assigned in PlayerHealth.");
                return;
            }
            playerDeathEventSO.OnEvent -= (OnDeath);
        }

        private void OnDamage(int damage)
        {
            if (playerCurrentHealthSO == null) return;
            
            if (playerCurrentHealthSO.TakeDamage(damage)) return;
            
            gameOverEventSO?.Invoke();
        }
        
        private void OnDeath()
        {
            if (playerCurrentHealthSO == null) return;
            
            if (playerCurrentHealthSO.CurrentHealth <= 0) return;

            playerCurrentHealthSO.Kill();
            
            gameOverEventSO?.Invoke();
        }
    }
}