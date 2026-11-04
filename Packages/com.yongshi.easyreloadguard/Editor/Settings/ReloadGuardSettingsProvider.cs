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
        const string k_EditorSettingsPath = "Project/Editor";
        const int k_MaxRows = 200;

        static string s_Version;

        SerializedObject m_Settings;
        bool m_ShowInfo;
        bool m_ShowBaselined;
        bool m_ShowEnter = true;
        bool m_ShowExit = true;
        bool m_ShowScope;
        bool m_ShowLog;
        bool m_ShowSkill = ReloadGuardSkillInstaller.Targets.Any(t =>
            t.ToolPresent && ReloadGuardSkillInstaller.GetStatus(t) != ReloadGuardSkillInstaller.SkillStatus.UpToDate);
        bool m_ShowScanned;
        bool m_ShowAdapters;
        readonly Dictionary<string, bool> m_GroupExpanded = new Dictionary<string, bool>();

        ReloadGuardSettingsProvider() : base(SettingsPath, SettingsScope.Project)
        {
            keywords = new HashSet<string>
            {
                "Domain Reload", "Enter Play Mode", "static", "Easy Reload Guard",
                "重新加载脚本", "检查", "遗留", "残留", "排除",
            };
        }

        [SettingsProvider]
        static SettingsProvider Create() => new ReloadGuardSettingsProvider();

        public static void Open() => SettingsService.OpenProjectSettings(SettingsPath);

        static string Version => s_Version ?? (s_Version =
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ReloadGuardSettingsProvider).Assembly)?.version ?? "");

        public override void OnActivate(string searchContext, VisualElement rootElement) =>
            m_Settings = new SerializedObject(ReloadGuardSettings.instance);

        public override void OnGUI(string searchContext)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(900)))
            {
                DrawStatus();
                DrawSettings();
                DrawActions();
                DrawReport();
            }
        }

        static void DrawStatus()
        {
            var disabled = EditorCompat.DomainReloadDisabled;
            using (new EditorGUILayout.VerticalScope(ReloadGuardStyles.Card))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("Easy Reload Guard", ReloadGuardStyles.Title);
                    GUILayout.Label(Version, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(ReloadGuardStyles.Content(" Enter Play Mode 设置", "_Popup", "打开 Project Settings/Editor 中的 Enter Play Mode 设置"),
                            EditorStyles.miniButton, GUILayout.ExpandWidth(false)))
                        SettingsService.OpenProjectSettings(k_EditorSettingsPath);
                }

                EditorGUILayout.Space(2);
                using (new EditorGUILayout.HorizontalScope())
                {
                    ReloadGuardStyles.DrawPill($"Domain Reload {(disabled ? "关闭" : "开启")}", disabled ? ReloadGuardStyles.Accent : ReloadGuardStyles.Neutral);
                    var sceneDisabled = EditorCompat.SceneReloadDisabled;
                    ReloadGuardStyles.DrawPill($"Scene Reload {(sceneDisabled ? "关闭" : "开启")}", sceneDisabled ? ReloadGuardStyles.Caution : ReloadGuardStyles.Neutral);
                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField(disabled
                        ? "进入 Play 时重置声明过的静态状态，退出 Play 时调用释放方法。"
                        : "Domain Reload 开启时不需要重置，退出 Play 时仍调用释放方法；可以先运行检查，为关闭 Domain Reload 做准备。",
                    EditorStyles.wordWrappedMiniLabel);
                if (EditorCompat.PlayModeOptionsMismatch)
                    EditorGUILayout.HelpBox(EditorCompat.MismatchHint + "。", MessageType.Warning);
            }

            ReloadGuardStyles.DrawLeftAccent(disabled ? ReloadGuardStyles.Accent : ReloadGuardStyles.Neutral);
        }

        void DrawSettings()
        {
            m_Settings.Update();

            m_ShowEnter = Section(m_ShowEnter, "进入 Play 前", () =>
            {
                Field(nameof(ReloadGuardSettings.prePlayCheck), "进入 Play 前检查");
                Field(nameof(ReloadGuardSettings.reloadOnResetFailure), "重置失败后重新加载脚本");
            });

            m_ShowExit = Section(m_ShowExit, "停止 Play 后", () =>
            {
                var probe = Field(nameof(ReloadGuardSettings.residueProbe), "停止 Play 后检查残留");
                using (new EditorGUI.DisabledScope(!probe.boolValue))
                using (new EditorGUI.IndentLevelScope())
                    Field(nameof(ReloadGuardSettings.residueProbeReadTypeInitializers), "也读取有静态构造的类型");
            });

            m_ShowScope = Section(m_ShowScope, "检查范围", () =>
            {
                Field(nameof(ReloadGuardSettings.excludedPackages), "排除的包");
                Field(nameof(ReloadGuardSettings.excludedAssemblies), "排除的程序集");
            });

            m_ShowLog = Section(m_ShowLog, "日志", () => Field(nameof(ReloadGuardSettings.verboseLog), "详细日志"));

            m_ShowSkill = Section(m_ShowSkill, "Agent Skill", DrawSkill);

            // 只按属性是否改动判断：展开或折叠分组也会置 GUI.changed，不能因此丢弃检查结果
            if (m_Settings.ApplyModifiedPropertiesWithoutUndo()) ReloadGuardSettings.instance.SaveAndInvalidate();
        }

        /// <summary>Claude Code 与 Codex 的 Skill 安装状态；安装只复制文件，不需要结束本次布局。</summary>
        static void DrawSkill()
        {
            EditorGUILayout.LabelField(
                $"把本包的约束（{ReloadGuardSkillInstaller.SkillName}）安装到 Agent 的用户 Skill 目录，写代码时自动遵守。包升级后 Skill 内容有变化时，这里会提示重新安装。",
                EditorStyles.wordWrappedMiniLabel);
            foreach (var target in ReloadGuardSkillInstaller.Targets)
            {
                var status = ReloadGuardSkillInstaller.GetStatus(target);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(target.Name, GUILayout.Width(90));
                    switch (status)
                    {
                        case ReloadGuardSkillInstaller.SkillStatus.UpToDate:
                            ReloadGuardStyles.DrawPill("已是最新", ReloadGuardStyles.Good);
                            break;
                        case ReloadGuardSkillInstaller.SkillStatus.Outdated:
                            ReloadGuardStyles.DrawPill("有更新", ReloadGuardStyles.Caution);
                            break;
                        case ReloadGuardSkillInstaller.SkillStatus.NotManaged:
                            ReloadGuardStyles.DrawPill("同名目录不是本包安装的", ReloadGuardStyles.Caution);
                            break;
                        default:
                            ReloadGuardStyles.DrawPill(target.ToolPresent ? "未安装" : "未安装（本机未发现该工具）", ReloadGuardStyles.Neutral);
                            break;
                    }

                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(status == ReloadGuardSkillInstaller.SkillStatus.NotManaged))
                    {
                        var label = status == ReloadGuardSkillInstaller.SkillStatus.UpToDate ? "重新安装" : "安装";
                        if (GUILayout.Button(new GUIContent(label, target.Directory), EditorStyles.miniButton, GUILayout.Width(70)))
                            ReloadGuardSkillInstaller.Install(target);
                    }
                }
            }
        }

        /// <summary>可折叠的卡片分组。不用 BeginFoldoutHeaderGroup：其中的列表字段会再画一个折叠标题，Unity 不允许嵌套。</summary>
        static bool Section(bool expanded, string title, System.Action body)
        {
            using (new EditorGUILayout.VerticalScope(ReloadGuardStyles.Card))
            {
                expanded = EditorGUILayout.Foldout(expanded, title, true, ReloadGuardStyles.GroupFoldout);
                if (expanded)
                {
                    EditorGUILayout.Space(2);
                    using (new EditorGUI.IndentLevelScope())
                        body();
                }
            }

            return expanded;
        }

        /// <summary>显示名由本页给出，提示文字取字段上的 [Tooltip]。</summary>
        SerializedProperty Field(string name, string label)
        {
            var property = m_Settings.FindProperty(name);
            EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), true);
            return property;
        }

        static void DrawActions()
        {
            EditorGUILayout.Space(6);
            System.Action action = null;
            using (new EditorGUILayout.VerticalScope(ReloadGuardStyles.Card))
            {
                GUILayout.Label("操作", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(ReloadGuardStyles.Content(" 运行检查", "Search Icon", "扫描 Assets 与全部包中的静态状态，结果显示在下方"), GUILayout.Height(26)))
                        action = ReloadGuardMenu.RunAudit;
                    if (GUILayout.Button(ReloadGuardStyles.Content(" 把当前问题记为已知遗留", "SaveAs", $"写入 {Baseline.FilePath}，记录的问题不再阻止进入 Play"), GUILayout.Height(26)))
                        action = ReloadGuardMenu.WriteBaseline;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(ReloadGuardStyles.Content(" 立即重新加载脚本", "Refresh", "不编译代码，只重新加载脚本；Play 中会先停止"), GUILayout.Height(22)))
                        action = ForceReload.ReloadNow;
                    if (GUILayout.Button(ReloadGuardStyles.Content(" 重新加载脚本并进入 Play", "PlayButton", "重新加载完成后自动进入 Play；主工具栏 Play 按钮旁也有同样的按钮"), GUILayout.Height(22)))
                        action = () => ForceReload.ReloadAndPlay("设置页手动请求");
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label("报告位置", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                    if (GUILayout.Button(ReloadGuardStyles.Content(" 检查报告", "FolderOpened Icon", ReloadAudit.DefaultReportPath), EditorStyles.miniButtonLeft))
                        action = () => EditorUtility.RevealInFinder(ReloadAudit.DefaultReportPath);
                    if (GUILayout.Button(ReloadGuardStyles.Content(" 停止后检查报告", "FolderOpened Icon", ResidueProbe.ReportPath), EditorStyles.miniButtonRight))
                        action = () => EditorUtility.RevealInFinder(ResidueProbe.ReportPath);
                    GUILayout.FlexibleSpace();
                }
            }

            if (action == null) return;
            action();
            GUIUtility.ExitGUI(); // 弹出模态对话框或切换窗口后终止本次布局，避免 GUILayout 不匹配
        }

        void DrawReport()
        {
            var report = ReloadAudit.Last;
            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("检查结果", ReloadGuardStyles.Title);
                if (report != null) GUILayout.Label(report.Time.ToString("HH:mm:ss"), EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
                if (report != null)
                {
                    m_ShowInfo = GUILayout.Toggle(m_ShowInfo, "显示提示", EditorStyles.miniButtonLeft);
                    m_ShowBaselined = GUILayout.Toggle(m_ShowBaselined, "显示已知遗留", EditorStyles.miniButtonRight);
                }
            }

            if (report == null)
            {
                EditorGUILayout.HelpBox("本次脚本加载后还没有运行检查。", MessageType.None);
                return;
            }

            DrawCounts(report);
            DrawReportDetails(report);
            EditorGUILayout.Space(4);
            DrawFindings(report);
        }

        static void DrawCounts(AuditReport report)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                CountPill("错误", report.Count(Severity.Error), ReloadGuardStyles.Bad);
                CountPill("警告", report.Count(Severity.Warning), ReloadGuardStyles.Caution);
                CountPill("提示", report.Count(Severity.Info), ReloadGuardStyles.Accent);
                ReloadGuardStyles.DrawPill($"已知遗留 {report.BaselinedCount}", ReloadGuardStyles.Neutral, $"记录在 {Baseline.FilePath} 中，不阻止进入 Play");
                GUILayout.FlexibleSpace();
            }
        }

        static void CountPill(string label, int count, Color color) =>
            ReloadGuardStyles.DrawPill($"{label} {count}", count > 0 ? color : ReloadGuardStyles.Neutral);

        void DrawReportDetails(AuditReport report)
        {
            var adapters = report.Plan.Adapters.Where(a => a.Active).ToList();
            m_ShowAdapters = EditorGUILayout.Foldout(m_ShowAdapters, $"生效的适配器（{adapters.Count}）", true);
            if (m_ShowAdapters)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    foreach (var adapter in adapters)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField($"{adapter.Target} {adapter.InstalledVersion}", EditorStyles.miniLabel);
                            if (!adapter.BuiltIn)
                                ReloadGuardStyles.DrawPill("比对静态字段", ReloadGuardStyles.Neutral, "跳过检查的代码中静态字段有变化时提醒复核，不比对版本号");
                            else
                                ReloadGuardStyles.DrawPill(adapter.Verified ? "版本已复核" : "版本未复核",
                                    adapter.Verified ? ReloadGuardStyles.Good : ReloadGuardStyles.Caution);
                        }
                    }
                }
            }

            m_ShowScanned = EditorGUILayout.Foldout(m_ShowScanned, $"扫描的程序集（{report.ScannedAssemblies.Count}）", true);
            if (m_ShowScanned)
            {
                using (new EditorGUI.IndentLevelScope())
                    EditorGUILayout.LabelField(report.ScannedAssemblies.Count == 0 ? "无" : string.Join(", ", report.ScannedAssemblies),
                        EditorStyles.wordWrappedMiniLabel);
            }

            if (report.MarkerExcludedAssemblies.Count > 0)
            {
                EditorGUILayout.LabelField(
                    $"目录中放了排除标记、未检查的程序集（{report.MarkerExcludedAssemblies.Count}）：" +
                    string.Join("；", report.MarkerExcludedAssemblies.Select(p => $"{p.Key}（{p.Value}）")),
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        /// <summary>按“是否已知遗留 → 级别 → 问题类型”分组，组内逐条列出成员、原因与读写位置。</summary>
        void DrawFindings(AuditReport report)
        {
            var visible = report.Findings
                .Where(f => (m_ShowInfo || f.Severity != Severity.Info) && (m_ShowBaselined || !f.Baselined))
                .ToList();
            if (visible.Count == 0)
            {
                using (new EditorGUILayout.HorizontalScope(ReloadGuardStyles.Card))
                {
                    GUILayout.Label(ReloadGuardStyles.Icon("TestPassed"), GUILayout.Width(18), GUILayout.Height(18));
                    GUILayout.Label("没有需要处理的问题。", EditorStyles.boldLabel);
                }

                return;
            }

            var shown = 0;
            foreach (var group in visible.GroupBy(f => (f.Baselined, f.Severity, f.Title)).OrderBy(g => g.Key.Baselined))
            {
                if (shown >= k_MaxRows) break;
                var (baselined, severity, title) = group.Key;
                var key = $"{baselined}|{severity}|{title}";
                if (!m_GroupExpanded.TryGetValue(key, out var expanded)) expanded = !baselined;

                var header = new GUIContent($" {title}（{group.Count()}）{(baselined ? " · 已知遗留" : "")}", ReloadGuardStyles.SeverityIcon(severity));
                using (new EditorGUILayout.VerticalScope(ReloadGuardStyles.Card))
                {
                    m_GroupExpanded[key] = expanded = EditorGUILayout.Foldout(expanded, header, true, ReloadGuardStyles.GroupFoldout);
                    if (expanded)
                    {
                        var index = 0;
                        foreach (var finding in group)
                        {
                            if (shown >= k_MaxRows) break;
                            DrawFinding(finding, index++);
                            shown++;
                        }
                    }
                }

                ReloadGuardStyles.DrawLeftAccent(baselined ? ReloadGuardStyles.Neutral : ReloadGuardStyles.SeverityColor(severity));
            }

            if (shown >= k_MaxRows && visible.Count > k_MaxRows)
                EditorGUILayout.HelpBox($"仅显示前 {k_MaxRows} 条，完整内容见 {ReloadAudit.DefaultReportPath}。", MessageType.None);
        }

        static void DrawFinding(Finding finding, int index)
        {
            // 隔行底色先于文字绘制；BeginVertical 的返回值只在 Repaint 时有效
            var rect = EditorGUILayout.BeginVertical();
            if (index % 2 == 1 && Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, ReloadGuardStyles.RowStripe);
            GUILayout.Label(finding.DisplaySubject, ReloadGuardStyles.RowSubject);
            GUILayout.Label(finding.Message, ReloadGuardStyles.RowMessage);
            if (finding.Context != null) GUILayout.Label("读写位置：" + finding.Context, ReloadGuardStyles.RowDetail);
            EditorGUILayout.Space(2);
            EditorGUILayout.EndVertical();
        }
    }
}
