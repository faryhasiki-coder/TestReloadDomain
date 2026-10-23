using System;
using System.Collections.Generic;
using EasyReloadGuard;
using UnityEngine;

namespace DomainReloadCases
{
    /// <summary>SO-01/02：作者配置与会话状态分离。测试只创建自己的临时 .asset，不改项目资产。</summary>
    public sealed class ScriptableStateCase : ScriptableObject
    {
        public int AuthorValue = 7;
        [NonSerialized] public int RuntimeCount;
        public event Action Changed;
        public int SubscriberCount => Changed?.GetInvocationList().Length ?? 0;

        public void Raise() => Changed?.Invoke();

        public void ResetRuntimeState()
        {
            RuntimeCount = 0;
            Changed = null;
        }
    }

    /// <summary>保留源资产，显式管理它的每局状态和临时副本，不依赖 Scene Reload 重建资产。</summary>
    public static class ScriptableStateOwner
    {
        sealed class OwnedCopy : IDisposable
        {
            readonly ScriptableStateCase m_Source;
            readonly Action m_Handler;
            public readonly ScriptableStateCase Value;

            public OwnedCopy(ScriptableStateCase source, ScriptableStateCase value)
            {
                m_Source = source;
                Value = value;
                m_Handler = () => { if (Value != null) Value.RuntimeCount++; };
                m_Source.Changed += m_Handler;
            }

            public void Dispose()
            {
                if (m_Source != null) m_Source.Changed -= m_Handler;
                if (Value == null) return;
                Value.ResetRuntimeState();
                // 仅销毁这里 CreateInstance/Instantiate 的副本。退出回调要求同步收尾，
                // 不把 Destroy 的帧末延迟当成已销毁，也不可能把源 .asset 放进此列表。
                UnityEngine.Object.DestroyImmediate(Value);
            }
        }

        public static ScriptableStateCase RetainedAsset { get; private set; }
        static readonly List<OwnedCopy> s_Copies = new List<OwnedCopy>();
        public static int OwnedCopyCount => s_Copies.Count;

        public static void BindTestAsset(ScriptableStateCase asset)
        {
            ReleaseOwnedCopies();
            RetainedAsset = asset;
        }

        public static ScriptableStateCase CreateRuntimeCopy(bool cloneAuthorConfiguration)
        {
            if (RetainedAsset == null) throw new InvalidOperationException("先绑定此案例拥有的源资产。");
            var copy = cloneAuthorConfiguration
                ? UnityEngine.Object.Instantiate(RetainedAsset)
                : ScriptableObject.CreateInstance<ScriptableStateCase>();
            copy.name = cloneAuthorConfiguration ? "ScriptableStateCase.Clone" : "ScriptableStateCase.Transient";
            copy.ResetRuntimeState();
            s_Copies.Add(new OwnedCopy(RetainedAsset, copy));
            return copy;
        }

        [ResetMethod]
        static void BeginSession()
        {
            ReleaseOwnedCopies();
            if (RetainedAsset != null) RetainedAsset.ResetRuntimeState();
        }

        [OnExitPlayMode]
        static void EndSession()
        {
            ReleaseOwnedCopies();
            if (RetainedAsset != null) RetainedAsset.ResetRuntimeState();
        }

        static void ReleaseOwnedCopies()
        {
            foreach (var copy in s_Copies) copy.Dispose();
            s_Copies.Clear();
        }

        /// <summary>测试收尾用，不是跨 Play 断言所依赖的 reset 入口。</summary>
        public static void ReleaseTestResources()
        {
            EndSession();
            RetainedAsset = null;
        }
    }
}
