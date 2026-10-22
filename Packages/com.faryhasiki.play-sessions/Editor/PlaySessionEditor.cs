using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Faryhasiki.PlaySessions.Editor
{
    [InitializeOnLoad]
    public static class PlaySessionEditor
    {
        const string ReloadTicket = "PlaySessions.ReloadTicket";
        const string ReloadCompleted = "PlaySessions.ReloadCompleted";
        const string PendingRestartKey = "PlaySessions.PendingRestartRequirement";
        public const string RestartRequiredKey = "PlaySessions.EditorRestartRequired";
        static bool reloadRequested;
        static bool reportedFault;
        public static string LastReloadCompleted => UnityEditor.SessionState.GetString(ReloadCompleted, "Never");

        static PlaySessionEditor()
        {
            var ticket = UnityEditor.SessionState.GetString(ReloadTicket, "");
            if (!string.IsNullOrEmpty(ticket))
            {
                UnityEditor.SessionState.SetString(ReloadCompleted, ticket);
                UnityEditor.SessionState.EraseString(ReloadTicket);
            }
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += PlaySession.Stop;
            EditorApplication.quitting += PlaySession.Stop;
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                if (UnityEditor.SessionState.GetBool(PendingRestartKey, false))
                    UnityEditor.SessionState.SetBool(RestartRequiredKey, true);
                UnityEditor.SessionState.EraseBool(PendingRestartKey);
            }
            if (change == PlayModeStateChange.ExitingPlayMode) PlaySession.Stop();
            if (change != PlayModeStateChange.ExitingEditMode) return;
            UnityEditor.SessionState.EraseBool(PendingRestartKey);
            try
            {
                if (reloadRequested || UnitySessionBridge.FastPlay && (PlaySession.BridgeError != null || PlaySession.Host.State != SessionState.Idle))
                    throw new InvalidOperationException("Previous session has not retired cleanly: " + PlaySession.Host.State + ". Use Force Script Reload; unresolved native work requires restarting the Editor.");
                var settings = PlaySessionsSettings.instance;
                var report = PlaySessionAudit.Run(settings.auditBeforePlay && UnitySessionBridge.FastPlay);
                var blockers = report.diagnostics.Where(d => d.severity == "Error" || UnitySessionBridge.FastPlay && settings.warningsBlockFastPlay && d.severity == "Warning").ToArray();
                if (blockers.Length > 0) throw new InvalidOperationException(string.Join("\n", blockers.Select(d => d.ToString())));
                UnityEditor.SessionState.SetBool(PendingRestartKey, report.requiresEditorRestartAfterPlay);
                var warnings = report.diagnostics.Count(d => d.severity == "Warning");
                if (warnings > 0) Debug.LogWarning("[Play Sessions] " + warnings + " audit warning(s). Open Tools/Play Sessions/Monitor.");
                if (PlaySession.Host.State == SessionState.Idle && PlaySession.BridgeError == null) reportedFault = false;
            }
            catch (Exception ex)
            {
                UnitySessionBridge.StopPlay();
                Debug.LogError("[Play Sessions] Play blocked.\n" + ex.Message);
            }
        }

        static void Update()
        {
            PlaySession.Host.Poll();
            if ((PlaySession.Host.State == SessionState.Faulted || PlaySession.BridgeError != null) && !reportedFault)
            {
                reportedFault = true;
                if (EditorApplication.isPlaying) UnitySessionBridge.StopPlay();
                Debug.LogError("[Play Sessions] Session failed; further Fast Play is blocked.\n" + PlaySession.BridgeError + "\n" + string.Join("\n", PlaySession.Host.Errors));
            }
            if (!reloadRequested || EditorApplication.isPlayingOrWillChangePlaymode || UnitySessionBridge.Busy) return;
            reloadRequested = false;
            UnityEditor.SessionState.SetString(ReloadTicket, DateTime.UtcNow.ToString("O"));
            UnitySessionBridge.ReloadScripts();
        }

        [MenuItem("Tools/Play Sessions/Force Script Reload")]
        public static void ForceReload()
        {
            reloadRequested = true;
            PlaySession.Stop();
            UnitySessionBridge.StopPlay();
            Debug.Log("[Play Sessions] Script reload requested. Settings are preserved. This does not reset native DLL/process state.");
        }
    }

    public sealed class PlaySessionMonitor : EditorWindow
    {
        AuditReport report;
        Vector2 scroll;
        string evidence = "";
        [MenuItem("Tools/Play Sessions/Monitor")]
        public static void Open() => GetWindow<PlaySessionMonitor>("Play Sessions");

        void OnGUI()
        {
            EditorGUILayout.LabelField("Play Sessions 0.1.0", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Unity", Application.unityVersion);
            EditorGUILayout.LabelField("Session", PlaySession.Host.State + " / " + PlaySession.Host.SessionId);
            EditorGUILayout.LabelField("Last forced reload (UTC)", PlaySessionEditor.LastReloadCompleted);
            EditorGUILayout.HelpBox("支持 Domain Reload 关闭、Scene Reload 开启。未评审的依赖不代表安全。原生 DLL 可能需要重启编辑器。", MessageType.Info);
            var settings = PlaySessionsSettings.instance;
            EditorGUI.BeginChangeCheck();
            settings.auditBeforePlay = EditorGUILayout.Toggle("Play 前静态检查", settings.auditBeforePlay);
            settings.warningsBlockFastPlay = EditorGUILayout.Toggle("警告阻止 Fast Play", settings.warningsBlockFastPlay);
            settings.requireDependencyCoverage = EditorGUILayout.Toggle("要求全部依赖已评审", settings.requireDependencyCoverage);
            if (EditorGUI.EndChangeCheck()) settings.SaveSettings();
            EditorGUILayout.LabelField("已验证 Unity", settings.verifiedUnityVersion);
            evidence = EditorGUILayout.TextField("升级验证记录", evidence);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(evidence)))
                if (GUILayout.Button("记录当前 Unity 版本的人工验证"))
                { settings.verifiedUnityVersion = Application.unityVersion; settings.verificationEvidence = evidence; settings.SaveSettings(); }
            if (GUILayout.Button("运行检查 / 刷新报告")) report = PlaySessionAudit.Run();
            if (GUILayout.Button("导出 JSON 到 outputs/play-sessions")) Debug.Log(PlaySessionAudit.Export(report ?? (report = PlaySessionAudit.Run())));
            if (GUILayout.Button("停止运行并强制重载脚本")) PlaySessionEditor.ForceReload();
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (report != null)
            {
                EditorGUILayout.LabelField("扫描程序集", string.Join(", ", report.scannedAssemblies));
                foreach (var diagnostic in report.diagnostics)
                    EditorGUILayout.HelpBox(diagnostic.ToString(), diagnostic.severity == "Error" ? MessageType.Error : diagnostic.severity == "Warning" ? MessageType.Warning : MessageType.Info);
            }
            foreach (var error in PlaySession.Host.Errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.EndScrollView();
        }
        void OnInspectorUpdate() => Repaint();
    }
}
