using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace Faryhasiki.PlaySessions.Editor
{
    [InitializeOnLoad]
    public static class PlaySessionEditorBridge
    {
        private const string InterruptedCleanupKey = "Faryhasiki.PlaySessions.InterruptedCleanup";
        private static List<SessionIssue> lastAudit = new List<SessionIssue>();
        public static IReadOnlyList<SessionIssue> LastAudit => lastAudit.AsReadOnly();

        static PlaySessionEditorBridge()
        {
            PlaySessionRuntime.SetEnvironmentValidation(ValidateEnvironment);
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += PlaySessionRuntime.Pump;
            EditorApplication.quitting += PlaySessionRuntime.Shutdown;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
        }

        public static bool UsesFastPlay => EditorSettings.enterPlayModeOptionsEnabled &&
            (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;

        public static List<SessionIssue> ValidateEnvironment(SessionCatalog catalog)
        {
            var result = SessionAudit.Inspect(catalog);
            if (catalog.ManagedAssemblies.Count == 0) return result;
            if (EditorSettings.enterPlayModeOptionsEnabled &&
                (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0)
                result.Add(new SessionIssue("PLAY114", "Editor settings", "This release requires Scene Reload enabled. DisableSceneReload is not validated."));
            if (SessionState.GetBool(InterruptedCleanupKey, false))
                result.Add(new SessionIssue("PLAY306", "assembly reload", "Cleanup was interrupted by assembly reload. Restart the Editor before another managed session."));
            var previous = PlaySessionRuntime.Current;
            if (!EditorApplication.isPlaying && previous != null && !previous.CanStartNext)
                result.Add(new SessionIssue("PLAY305", "previous session", "Cleanup is pending or failed. Inspect the session diagnostics before retrying."));
            return result;
        }

        [MenuItem("Tools/Play Sessions/Run Preflight")]
        public static void RunPreflight()
        {
            lastAudit = ValidateEnvironment(CompositionDiscovery.Discover());
            SessionMonitorWindow.Open();
        }

        [MenuItem("Tools/Play Sessions/Enable Fast Play (Domain Reload Off)")]
        public static void EnableFastPlay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            AssetDatabase.SaveAssets();
            RunPreflight();
        }

        [MenuItem("Tools/Play Sessions/Use Standard Play (Reload Both)")]
        public static void UseStandardPlay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;
            AssetDatabase.SaveAssets();
            RunPreflight();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode)
            {
                lastAudit = ValidateEnvironment(CompositionDiscovery.Discover());
                if (lastAudit.Any(issue => issue.Severity == IssueSeverity.Error))
                {
                    EditorApplication.isPlaying = false;
                    foreach (var issue in lastAudit) Debug.LogError(issue.ToString());
                    SessionMonitorWindow.Open();
                }
            }
            else if (change == PlayModeStateChange.ExitingPlayMode) PlaySessionRuntime.Shutdown();
        }

        private static void OnCompilationStarted(object context)
        {
            if (PlaySessionRuntime.Current == null) return;
            PlaySessionRuntime.Shutdown();
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
        }

        private static void BeforeReload()
        {
            PlaySessionRuntime.Shutdown();
            PlaySessionRuntime.Pump();
            var previous = PlaySessionRuntime.Current;
            if (previous != null && !previous.CanStartNext)
                SessionState.SetBool(InterruptedCleanupKey, true);
        }
    }
}
