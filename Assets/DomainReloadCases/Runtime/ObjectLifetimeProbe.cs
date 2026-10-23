using System;
using System.Collections.Generic;
using UnityEngine;

namespace DomainReloadCases
{
    /// <summary>
    /// OBJ-02/03：记录场景序列化与真实 MonoBehaviour 生命周期；不标 ExecuteAlways，
    /// 不把热重载时的私有字段保存规则当作 Prefab/场景序列化规则。
    /// </summary>
    public sealed class ObjectLifetimeProbe : MonoBehaviour
    {
        public int PublicSerialized = 10;
        [SerializeField] int m_PrivateSerialized = 20;
        [NonSerialized] public int NonSerializedValue = 30;
        public int AutoProperty { get; set; } = 40;
        public List<int> SerializedNumbers = new List<int> { 50 };
        public int? NullableValue = 60;

        [NonSerialized] public int AwakeCalls;
        [NonSerialized] public int EnableCalls;
        [NonSerialized] public int StartCalls;
        [NonSerialized] public int DisableCalls;
        [NonSerialized] public int DestroyCalls;
        [NonSerialized] public List<string> Lifecycle = new List<string>();

        public int PrivateSerialized
        {
            get => m_PrivateSerialized;
            set => m_PrivateSerialized = value;
        }

        void Awake() { AwakeCalls++; Record(nameof(Awake)); }
        void OnEnable() { EnableCalls++; Record(nameof(OnEnable)); }
        void Start() { StartCalls++; Record(nameof(Start)); }
        void OnDisable() { DisableCalls++; Record(nameof(OnDisable)); }
        void OnDestroy() { DestroyCalls++; Record(nameof(OnDestroy)); }

        void Record(string callback)
        {
            if (Lifecycle == null) Lifecycle = new List<string>();
            Lifecycle.Add(callback);
        }

        public void MutateAllFields(int value)
        {
            PublicSerialized = value;
            m_PrivateSerialized = value;
            NonSerializedValue = value;
            AutoProperty = value;
            SerializedNumbers.Add(value);
            NullableValue = value;
        }
    }
}
