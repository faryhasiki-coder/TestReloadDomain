using UnityEngine;
using Core;

namespace EventSO
{
    [CreateAssetMenu(fileName = "GameStateEvent", menuName = "Event/GameState", order = 0)]
    public class GameStateEventSO : BaseEventSO<GameState>
    {
    }
}