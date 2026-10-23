using Core;
using EventSO;
using UnityEngine;

namespace Test.Core
{
    public class GameStateTester : MonoBehaviour
    {
        [Header("Broadcast Events")]
        [SerializeField] private VoidEventSO gameOverEvent;
        [SerializeField] private VoidEventSO gameWinEvent;
        
        [Header("Received Events")]
        [SerializeField] private GameStateEventSO gameStateEvent;
        
        private void OnEnable()
        {
            gameStateEvent.OnEvent += OnGameStateChanged;
        }
        
        private void OnDisable()
        {
            gameStateEvent.OnEvent -= OnGameStateChanged;
        }
        
        
        [ContextMenu("Test Winning")]
        private void TestWinning()
        {
            gameWinEvent.Invoke();
        }

        [ContextMenu("Test Losing")]
        private void TestLosing()
        {
            gameOverEvent.Invoke();
        }
        
        private void OnGameStateChanged(GameState state)
        {
            Debug.Log($"Game State Changed: {state}");
        }
    }
}