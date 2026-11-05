using UnityEditor;
using UnityEditor.SceneManagement;

namespace EasyReloadGuard.Editor
{
    [InitializeOnLoad]
    internal static class ReloadGuardBootstrap
    {
        // 静态构造每次域加载执行一次；EditorApplication、EditorSceneManager 事件随域重载清空，不会重复订阅
        static ReloadGuardBootstrap()
        {
            PlaySession.EditorEnterPlayHook = EnterTiming.OnSubsystemRegistration;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneClosed += EnterTiming.OnSceneClosed;
            ForceReload.ResumeAfterReload();
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    EnterTiming.Disarm();
                    ResetPipeline.BeginEnteringPlay();
                    if (!PrePlayGate.OnExitingEditMode()) return;
                    ResetPipeline.ObserveInitialValues();
                    EnterTiming.Arm();
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    EnterTiming.Disarm();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    ResetPipeline.RunExit();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    EnterTiming.Disarm();
                    ResidueProbe.OnEnteredEditMode();
                    ForceReload.OnEnteredEditMode();
                    break;
            }
        }
    }

    /// <summary>本域是否进入过 Play。静态字段随域重载清零，正好表达“域是新的”。</summary>
    internal static class DomainState
    {
        static bool s_Played;

        public static bool IsFresh => !s_Played;

        public static void MarkPlayed() => s_Played = true;
    }
}
