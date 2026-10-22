using UnityEditor;
using UnityEngine;

namespace Faryhasiki.PlaySessions.Editor
{
    // Unity-version-specific operations live here; the core has no engine dependency.
    public static class UnitySessionBridge
    {
        public const string VerifiedUnityVersion = "6000.3.8f1";
        public static bool FastPlay => EditorSettings.enterPlayModeOptionsEnabled &&
            (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;
        public static bool SceneReloadDisabled => EditorSettings.enterPlayModeOptionsEnabled &&
            (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableSceneReload) != 0;
        public static bool Busy => EditorApplication.isCompiling || EditorApplication.isUpdating;
        public static void StopPlay() => EditorApplication.isPlaying = false;
        public static void ReloadScripts() => EditorUtility.RequestScriptReload();
    }

    [FilePath("ProjectSettings/PlaySessionsSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class PlaySessionsSettings : ScriptableSingleton<PlaySessionsSettings>
    {
        public bool auditBeforePlay = true;
        public bool warningsBlockFastPlay;
        public bool requireDependencyCoverage;
        public string verifiedUnityVersion = UnitySessionBridge.VerifiedUnityVersion;
        public string verificationEvidence = "Package baseline; see Tests and Documentation~/validation.md.";
        public void SaveSettings() => Save(true);
    }
}
