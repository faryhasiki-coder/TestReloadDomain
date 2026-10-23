using System;
using EasyReloadGuard;
using UnityEngine;

namespace DomainReloadCases
{
    public sealed class ReloadModeProbe : MonoBehaviour
    {
        [ReloadSafe("只读域身份，用于区分真正域重载与连续 Play")]
        public static readonly string DomainIdentity = Guid.NewGuid().ToString("N");
        [ResetStatic] public static int SessionValue;
        public int SerializedValue = 11;
        [NonSerialized] public int RuntimeValue = 22;
        [NonSerialized] public string ManagedIdentity = Guid.NewGuid().ToString("N");
        public int PropertyValue { get; set; } = 33;
        public int AwakeCalls { get; private set; }
        public int EnableCalls { get; private set; }
        public int StartCalls { get; private set; }
        public int DisableCalls { get; private set; }

        void Awake() => AwakeCalls++;
        void OnEnable() => EnableCalls++;
        void Start() => StartCalls++;
        void OnDisable() => DisableCalls++;
    }
}
