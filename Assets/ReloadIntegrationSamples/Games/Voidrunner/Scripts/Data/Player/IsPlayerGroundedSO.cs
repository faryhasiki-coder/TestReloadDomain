using UnityEngine;

namespace Data.Player
{
    [CreateAssetMenu(fileName = "Is Player Grounded", menuName = "Player/Is Player Grounded", order = 0)]
    public class IsPlayerGroundedSO : ScriptableObject
    {
        public bool isGrounded;
    }
}