using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace EasyReloadGuard.Editor
{
    /// <summary>Project Settings/Easy Reload Guard：当前 Reload 配置、设置项、操作按钮与最近一次检查结果。</summary>
    internal sealed class ReloadGuardSettingsProvider : SettingsProvider
    {
        public const string SettingsPath = "Project/Easy Reload Guard";
        const int k_MaxRows = 200;

        SerializedObject m_Settings;
        Vector2 m_Scroll;
        bool m_ShowInfo;
        bool m_ShowBaselined;

        ReloadGuardSettingsProvider() : base(SettingsPath, SettingsScope.Project)
        {
            keywords = new HashSet<string> { "Domain Reload", "Enter Play Mode", "static", "Easy Reload Guard" };
        }

        [SettingsProvider]
        static SettingsProvider Create() => new ReloadGuardSettingsProvider();

        public static void Open() => SettingsService.OpenProjectSettings(SettingsPath);

        public override void OnActivate(string searchContext, VisualElement rootElement) =>
            m_Settings = new SerializedObject(ReloadGuardSettings.instance);

        public override void OnGUI(string searchContext)
        {
            DrawStatus();
            DrawSettings();
            DrawActions();
            DrawReport();
        }

        static void DrawStatus()
        {
            var disabled = EditorCompat.DomainReloadDisabled;
            var text = EditorCompat.DescribePlayModeOptions() + (disabled
                ? "。进入 Play 时执行重置计划，退出时执行释放步骤。"
                : "。Domain Reload 开启：不执行重置，退出释放仍执行；检查可照常运行，为关闭做准备。");
            EditorGUILayout.HelpBox(text, disabled ? MessageType.Info : MessageType.None);
        }

        void DrawSettings()
        {
            m_Settings.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_Settings.FindProperty(nameof(ReloadGuardSettings.prePlayCheck)), new GUIContent("进入 Play 前检查"));
            EditorGUILayout.PropertyField(m_Settings.FindProperty(nameof(ReloadGuardSettings.reloadOnResetFailure)), new GUIContent("重置失败后强制重载"));
            EditorGUILayout.PropertyField(m_Settings.FindProperty(nameof(ReloadGuardSettings.verboseLog)), new GUIContent("详细日志"));
            EditorGUILayout.PropertyField(m_Settings.FindProperty(nameof(ReloadGuardSettings.residueProbe)), new GUIContent("退出 Play 后探测残留"));
            EditorGUILayout.PropertyField(m_Settings.FindProperty(nameof(ReloadGuardSettings.residueProbeReadTypeInitializers)),
                new GUIContent("残留探测读取有静态构造的类型"));
            EditorGUILayout.PropertyField(m_Settings.FindProperty(nameof(ReloadGuardSettings.excludedPackages)), new GUIContent("排除的包"));
            EditorGUILayout.PropertyField(m_Settings.FindProperty(nameof(ReloadGuardSettings.excludedAssemblies)), new GUIContent("排除的程序集"));
            if (EditorGUI.EndChangeCheck())
            {
                m_Settings.ApplyModifiedPropertiesWithoutUndo();
                ReloadGuardSettings.instance.SaveAndInvalidate();
            }
        }

        static void DrawActions()
        {
            EditorGUILayout.Space();
            System.Action action = null;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("运行检查")) action = ReloadGuardMenu.RunAudit;
                if (GUILayout.Button("将当前遗留问题写入基线")) action = ReloadGuardMenu.WriteBaseline;
                if (GUILayout.Button("打开报告位置")) action = () => EditorUtility.RevealInFinder(ReloadAudit.DefaultReportPath);
                if (GUILayout.Button("打开残留探测报告位置")) action = () => EditorUtility.RevealInFinder(ResidueProbe.ReportPath);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("立即重载域")) action = ForceReload.ReloadNow;
                if (GUILayout.Button("重载域并进入 Play")) action = () => ForceReload.ReloadAndPlay("设置页手动请求");
            }

            if (action == null) return;
            action();
            GUIUtility.ExitGUI(); // 弹出模态对话框或切换窗口后终止本次布局，避免 GUILayout 不匹配
        }

        void DrawReport()
        {
            var report = ReloadAudit.Last;
            EditorGUILayout.Space();
            if (report == null)
            {
                EditorGUILayout.HelpBox("本域尚未运行检查。", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField($"检查结果（{report.Time:HH:mm:ss}）：{report.Summary()}；阻断 {report.BlockingCount}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"扫描程序集：{(report.ScannedAssemblies.Count == 0 ? "无" : string.Join(", ", report.ScannedAssemblies))}", EditorStyles.wordWrappedMiniLabel);
            foreach (var adapter in report.Plan.Adapters.Where(a => a.Active))
            {
                EditorGUILayout.LabelField($"适配器 {adapter.Target} {adapter.InstalledVersion}：{(adapter.Verified ? "已验证" : "未验证")}",
                    EditorStyles.miniLabel);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                m_ShowInfo = EditorGUILayout.ToggleLeft("显示提示", m_ShowInfo);
                m_ShowBaselined = EditorGUILayout.ToggleLeft("显示已基线", m_ShowBaselined);
            }

            var visible = report.Findings
                .Where(f => (m_ShowInfo || f.Severity != Severity.Info) && (m_ShowBaselined || !f.Baselined))
                .ToList();
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            foreach (var finding in visible.Take(k_MaxRows))
            {
                var text = finding.Context == null ? finding.ToString() : $"{finding}\n上下文：{finding.Context}";
                EditorGUILayout.HelpBox(text, ToMessageType(finding));
            }

            if (visible.Count > k_MaxRows)
                EditorGUILayout.HelpBox($"仅显示前 {k_MaxRows} 条，完整内容见 {ReloadAudit.DefaultReportPath}。", MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        static MessageType ToMessageType(Finding finding)
        {
            if (finding.Baselined) return MessageType.None;
            switch (finding.Severity)
            {
                case Severity.Error: return MessageType.Error;
                case Severity.Warning: return MessageType.Warning;
                default: return MessageType.Info;
            }
        }
    }
}
