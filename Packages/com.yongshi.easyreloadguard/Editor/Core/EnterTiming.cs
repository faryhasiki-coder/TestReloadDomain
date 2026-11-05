using UnityEditor;
using UnityEngine.SceneManagement;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 进入 Play 时重置的时机（关闭 Domain Reload 时）。6000.3.1f1 实测进入 Play 的顺序：
    /// ExitingEditMode → [InitializeOnEnterPlayMode] → 编辑模式场景对象 OnDisable/OnDestroy → EditorSceneManager.sceneClosed（每个已加载场景一次）
    /// → 各程序集的 SubsystemRegistration（程序集之间的先后不固定，本包的钩子会排在部分业务之后）→ … → 首帧。
    /// 重置必须晚于编辑模式对象的退出回调（开启 Domain Reload 时它们写入的值会被重新加载清掉），并早于所有业务 SubsystemRegistration，
    /// 所以在 ExitingEditMode 登记已加载的场景数，最后一个场景关闭时执行重置；PlaySession 的 SubsystemRegistration 钩子只作后备。
    /// 关闭 Scene Reload、场景数对不上等情况会退回后备，并给出警告。
    /// </summary>
    internal static class EnterTiming
    {
        static bool s_Waiting;
        static int s_Remaining;
        static bool s_SceneReloadWarned;

        /// <summary>ExitingEditMode、确认本次进入 Play 不会被取消之后调用。</summary>
        internal static void Arm()
        {
            s_Waiting = false;
            if (!EditorCompat.DomainReloadDisabled || EditorCompat.SceneReloadDisabled) return;
            s_Remaining = EditorCompat.LoadedSceneCount;
            s_Waiting = s_Remaining > 0;
        }

        internal static void Disarm() => s_Waiting = false;

        /// <summary>只在登记期间、进入 Play 方向（isPlaying 已为 true）响应：停止方向的 sceneClosed 与 Play 中业务卸载场景都不会触发重置。</summary>
        internal static void OnSceneClosed(Scene scene)
        {
            if (!s_Waiting || !EditorApplication.isPlaying) return;
            if (--s_Remaining > 0) return;
            s_Waiting = false;
            ResetPipeline.RunEnter();
        }

        /// <summary>PlaySession 的 SubsystemRegistration 钩子：本次 Play 的重置还没执行时在这里执行（后备），然后处理重置失败。</summary>
        internal static void OnSubsystemRegistration()
        {
            if (!ResetPipeline.EnterDone && EditorCompat.DomainReloadDisabled)
            {
                if (s_Waiting)
                {
                    Log.Warning($"重置时机退回到 SubsystemRegistration：等待的编辑场景还有 {s_Remaining} 个没有收到关闭通知（如未保存的场景）。" +
                                "部分程序集的 SubsystemRegistration 可能早于重置执行，其中写入的已声明字段会被重置覆盖。");
                }
                else if (EditorCompat.SceneReloadDisabled && !s_SceneReloadWarned)
                {
                    s_SceneReloadWarned = true;
                    Log.Warning("Scene Reload 已关闭，重置在 SubsystemRegistration 中执行（该配置的顺序未实测）：" +
                                "部分程序集的 SubsystemRegistration 可能早于重置执行，其中写入的已声明字段会被重置覆盖。");
                }
                else if (!EditorCompat.SceneReloadDisabled)
                {
                    Log.Warning("重置时机退回到 SubsystemRegistration：本次进入 Play 没有完成编辑场景关闭阶段的重置，部分业务初始化可能早于重置。");
                }
            }

            s_Waiting = false;
            ResetPipeline.RunEnter();
            ResetPipeline.HandleEnterFailure();
        }
    }
}
