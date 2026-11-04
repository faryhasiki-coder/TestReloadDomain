using EventSO;
using UnityEngine;

namespace Test.Core
{
    public class GameLevelTester : MonoBehaviour
    {
        [Header("Received Events")]
        [SerializeField] private VoidEventSO gameSavingEvent;
        [SerializeField] private VoidEventSO gameLoadingEvent;
        
        [Header("Broadcasted Events")]
        [SerializeField] private VoidEventSO gameTransitionEvent;
        
        
        private string _currentSceneName;
        
        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            _currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        }

        private void Update()
        {
            var currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentSceneName == _currentSceneName) return;
            
            _currentSceneName = currentSceneName;
            Debug.Log($"Scene changed to: {_currentSceneName}");
        }

        [ContextMenu("Transition To Next Level")]
        private void TransitionToNextLevel()
        {
            gameTransitionEvent.Invoke();
        }
        
        private void OnEnable()
        {
            gameSavingEvent.OnEvent += OnGameSaving;
            gameLoadingEvent.OnEvent += OnGameLoading;
        }
        
        private void OnDisable()
        {
            gameSavingEvent.OnEvent -= OnGameSaving;
            gameLoadingEvent.OnEvent -= OnGameLoading;
        }
        
        private void OnGameSaving()
        {
            Debug.Log($"Game is saving at scene: {_currentSceneName}");
        }
        
        private void OnGameLoading()
        {
            Debug.Log($"Game is loading at scene: {_currentSceneName}");
        }
    }
}