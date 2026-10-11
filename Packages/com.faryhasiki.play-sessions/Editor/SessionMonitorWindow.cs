using UnityEditor;
using UnityEngine;

namespace Faryhasiki.PlaySessions.Editor
{
    public sealed class SessionMonitorWindow : EditorWindow
    {
        private Vector2 scroll;

        [MenuItem("Tools/Play Sessions/Monitor")]
        public static void Open() => GetWindow<SessionMonitorWindow>("Play Sessions");
        private void OnInspectorUpdate() => Repaint();

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Play Sessions  /  0.1.0", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Domain Reload", PlaySessionEditorBridge.UsesFastPlay ? "OFF — session managed" : "ON — baseline");
            EditorGUILayout.HelpBox("检查覆盖主动声明 PlaySessionAssembly 的程序集。外部 package 适配与完整开源工程验收尚需单独完成。", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("检查接入")) PlaySessionEditorBridge.RunPreflight();
                    if (GUILayout.Button("关闭 Domain Reload")) PlaySessionEditorBridge.EnableFastPlay();
                    if (GUILayout.Button("恢复标准模式")) PlaySessionEditorBridge.UseStandardPlay();
                }
            }
            var host = PlaySessionRuntime.Current;
            if (host != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Session", host.Id.ToString());
                EditorGUILayout.LabelField("State", host.Status.ToString());
                EditorGUILayout.LabelField("Resources / Tasks / Rejected", $"{host.ResourceCount} / {host.PendingTasks} / {host.RejectedCommits}");
                if (!EditorApplication.isPlaying && !host.CanStartNext)
                    EditorGUILayout.HelpBox("上一会话尚未安全结束。请查看下方问题；任务不会被强制遗弃。", MessageType.Error);
                foreach (var scope in host.Scopes)
                    EditorGUILayout.LabelField(scope.ModuleId, $"resources {scope.ResourceCount}   tasks {scope.PendingTasks}   stopped {scope.IsStopped}");
            }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var issue in PlaySessionEditorBridge.LastAudit) DrawIssue(issue);
            foreach (var issue in PlaySessionRuntime.BootIssues) DrawIssue(issue);
            if (host != null) foreach (var issue in host.Issues) DrawIssue(issue);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawIssue(SessionIssue issue)
        {
            EditorGUILayout.HelpBox(issue.ToString(), issue.Severity == IssueSeverity.Error ? MessageType.Error : MessageType.Warning);
            var dot = issue.Owner.LastIndexOf('.');
            var typeName = dot > 0 ? issue.Owner.Substring(0, dot) : issue.Owner;
            var lastDot = typeName.LastIndexOf('.');
            var shortName = lastDot >= 0 ? typeName.Substring(lastDot + 1) : typeName;
            var guids = AssetDatabase.FindAssets($"{shortName} t:MonoScript");
            if (guids.Length == 1 && GUILayout.Button("打开相关脚本"))
                AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guids[0])));
        }
    }
}
