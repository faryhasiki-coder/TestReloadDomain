using System;
using UnityEngine;

namespace EventSO
{
    public abstract class BaseEventSO<T> : ScriptableObject
    {
        private Action<T> _action;

        public event Action<T> OnEvent
        {
            add => _action += value;
            remove => _action -= value;
        }

        public void Invoke(T arg)
        {
            _action?.Invoke(arg);
        }
    }
}