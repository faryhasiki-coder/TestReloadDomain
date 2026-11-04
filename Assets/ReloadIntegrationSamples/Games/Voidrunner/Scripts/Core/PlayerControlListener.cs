using EventSO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Core
{
    public class PlayerControlListener : MonoBehaviour, InputSystem.InputSystem.IPlayerActions
    {
        [Header("Events broadcasted")]
        [SerializeField] private Vector2EventSO moveInput;
        [SerializeField] private Vector2EventSO lookInput;
        [SerializeField] private VoidEventSO attackInput;
        [SerializeField] private VoidEventSO interactInput;
        [SerializeField] private VoidEventSO crouchInput;
        [SerializeField] private VoidEventSO jumpInput;
        [SerializeField] private VoidEventSO previousInput;
        [SerializeField] private VoidEventSO nextInput;
        [SerializeField] private VoidEventSO sprintInput;

        private InputSystem.InputSystem _playerInputSystem;
        private InputSystem.InputSystem.PlayerActions _playerActions;
        
        private void Awake()
        {
            _playerInputSystem = new InputSystem.InputSystem();
            _playerActions = _playerInputSystem.Player;
            _playerActions.SetCallbacks(this);
        }

        private void OnDestroy()
        {
            _playerInputSystem.Dispose();
        }

        private void OnEnable()
        {
            _playerActions.Enable();
        }
        
        private void OnDisable()
        {
            _playerActions.Disable();
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            moveInput?.Invoke(context.ReadValue<Vector2>());
        }

        public void OnLook(InputAction.CallbackContext context)
        {
            lookInput?.Invoke(context.ReadValue<Vector2>());
        }

        public void OnAttack(InputAction.CallbackContext context)
        {
            if (context.performed)
                attackInput?.Invoke();
        }

        public void OnInteract(InputAction.CallbackContext context)
        {
            if (context.performed)
                interactInput?.Invoke();
        }

        public void OnCrouch(InputAction.CallbackContext context)
        {
            if (context.performed)
                crouchInput?.Invoke();
        }

        public void OnJump(InputAction.CallbackContext context)
        {
            if (context.performed)
                jumpInput?.Invoke();
        }

        public void OnPrevious(InputAction.CallbackContext context)
        {
            if (context.performed)
                previousInput?.Invoke();
        }

        public void OnNext(InputAction.CallbackContext context)
        {
            if (context.performed)
                nextInput?.Invoke();
        }

        public void OnSprint(InputAction.CallbackContext context)
        {
            if (context.performed)
                sprintInput?.Invoke();
        }
    }
}