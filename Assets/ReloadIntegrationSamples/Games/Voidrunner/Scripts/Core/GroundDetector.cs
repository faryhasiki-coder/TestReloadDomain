using Data.Player;
using UnityEngine;

namespace Core
{
    [RequireComponent(typeof(Transform))]
    public class GroundDetector : MonoBehaviour
    {
        [Header("Ground Check Settings")]
        [SerializeField] private LayerMask groundLayer;
        [SerializeField] private Vector2 groundCheckSize = new(1f, 0.1f);
        [SerializeField] private Vector2 groundCheckOffset = new(0f, -0.5f);
        
        [Header("Events broadcasted")]
        [SerializeField] private IsPlayerGroundedSO isPlayerGrounded;

        private void Awake()
        {
            if (!isPlayerGrounded)
            {
                Debug.LogWarning("IsPlayerGroundedSO is not assigned in the inspector.");
            }
        }

        private void FixedUpdate()
        {
            if (!isPlayerGrounded) return;

            isPlayerGrounded.isGrounded = Physics2D.OverlapBox(
                transform.position + (Vector3)groundCheckOffset,
                groundCheckSize,
                0f,
                groundLayer
            );
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;

            Gizmos.DrawWireCube(
                transform.position + (Vector3)groundCheckOffset,
                groundCheckSize
            );
        }
    }
}