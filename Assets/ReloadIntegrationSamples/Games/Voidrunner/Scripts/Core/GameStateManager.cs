using EventSO;
using UnityEngine;

namespace Core
{
    public class GameStateManager : MonoBehaviour
    {
        [Header("Received Events")] [SerializeField]
        private VoidEventSO gameOverEvent;

        [SerializeField] private VoidEventSO gameWinEvent;

        [Header("Broadcasted Events")] [SerializeField]
        private GameStateEventSO gameStateEvent;

        private GameState _gameState;

        private void Awake()
        {
            _gameState = GameState.Playing;
        }

        private void OnEnable()
        {
            gameOverEvent.OnEvent += OnGameOver;
            gameWinEvent.OnEvent += OnGameWin;
        }

        private void OnDisable()
        {
            gameOverEvent.OnEvent -= OnGameOver;
            gameWinEvent.OnEvent -= OnGameWin;
        }

        private void OnGameWin()
        {
            if (_gameState != GameState.Playing) return;

            _gameState = GameState.Victory;
            gameStateEvent.Invoke(GameState.Victory);
        }

        private void OnGameOver()
        {
            if (_gameState != GameState.Playing) return;

            _gameState = GameState.Defeat;
            gameStateEvent.Invoke(GameState.Defeat);
        }
    }
}