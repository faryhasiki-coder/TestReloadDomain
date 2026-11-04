using System;
using UnityEngine;

namespace EventSO
{
    [CreateAssetMenu(menuName = "SOEvents/VoidEventSO", fileName = "New Void Event SO")]
    public class VoidEventSO : ScriptableObject
    {
        private Action _action;

        public event Action OnEvent
        {
            add => _action += value;
            remove => _action -= value;
        }

        public void Invoke()
        {
            _action?.Invoke();
        }
    }
}