using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace EasyReloadGuard.Editor
{
    internal sealed class AuditReport
    {
        public AuditReport(ResetPlan plan)
        {
            Plan = plan;
            Time = DateTime.Now;
            PlayModeOptions = EditorCompat.DescribePlayModeOptions();
        }

        public ResetPlan Plan { get; }
        public DateTime Time { get; }
        public string PlayModeOptions { get; }
        public List<Finding> Findings { get; } = new List<Finding>();
        public List<string> ScannedAssemblies { get; } = new List<string>();
        public List<string> SelfHandledAssemblies { get; } = new List<string>();
        public List<string> LegacySelfResetTypes { get; } = new List<string>();

        public int BlockingCount => Findings.Count(f => f.IsBlocking);

        public int Count(Severity severity) => Findings.Count(f => f.Severity == severity && !f.Baselined);

        public int BaselinedCount => Findings.Count(f => f.Baselined);

        public void Sort() =>
            Findings.Sort((a, b) =>
            {
                var bySeverity = b.Severity.CompareTo(a.Severity);
                if (bySeverity != 0) return bySeverity;
                var byRule = string.CompareOrdinal(a.RuleId, b.RuleId);
                return byRule != 0 ? byRule : string.CompareOrdinal(a.Subject, b.Subject);
            });

        public string Summary() =>
            $"错误 {Count(Severity.Error)}、警告 {Count(Severity.Warning)}、提示 {Count(Severity.Info)}、已基线 {BaselinedCount}";

        public string TopLines(int max)
        {
            var lines = Findings.Where(f => !f.Baselined && f.Severity != Severity.Info).Take(max).Select(f => "  " + f);
            return string.Join("\n", lines);
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Easy Reload Guard 检查报告");
            sb.AppendLine();
            sb.AppendLine($"- 时间：{Time:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- Unity：{Application.unityVersion}");
            sb.AppendLine($"- Enter Play Mode：{PlayModeOptions}");
            sb.AppendLine($"- 统计：{Summary()}；阻断 {BlockingCount}");
            sb.AppendLine($"- 重置计划：字段 {Plan.FieldSteps.Count}、进入步骤 {Plan.EnterSteps.Count}、退出步骤 {Plan.ExitSteps.Count}");
            sb.AppendLine($"- 扫描程序集：{Join(ScannedAssemblies)}");
            sb.AppendLine($"- 自行处理而跳过的程序集：{Join(SelfHandledAssemblies)}");
            sb.AppendLine($"- 沿用 SubsystemRegistration/InitializeOnEnterPlayMode 自行重置的类型（{LegacySelfResetTypes.Count}）：{Join(LegacySelfResetTypes)}");
            sb.AppendLine();
            sb.AppendLine("## 适配器");
            sb.AppendLine();
            sb.AppendLine("| 适配器 | 目标 | 已安装版本 | 已验证 | 自行处理依据 |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var adapter in Plan.Adapters)
            {
                sb.AppendLine($"| {adapter.Name} | {adapter.Target} | {adapter.InstalledVersion ?? "未安装（休眠）"} | " +
                              $"{(adapter.Active ? adapter.Verified ? "是" : "否" : "-")} | {adapter.SelfHandledEvidence ?? "-"} |");
            }

            AppendSection(sb, "错误", Findings.Where(f => f.Severity == Severity.Error && !f.Baselined));
            AppendSection(sb, "警告", Findings.Where(f => f.Severity == Severity.Warning && !f.Baselined));
            AppendSection(sb, "提示", Findings.Where(f => f.Severity == Severity.Info && !f.Baselined));
            AppendSection(sb, "已基线（遗留，不阻断）", Findings.Where(f => f.Baselined));
            return sb.ToString();
        }

        static void AppendSection(StringBuilder sb, string title, IEnumerable<Finding> findings)
        {
            var list = findings.ToList();
            sb.AppendLine();
            sb.AppendLine($"## {title}（{list.Count}）");
            sb.AppendLine();
            foreach (var f in list) sb.AppendLine($"- `{f.RuleId}` `{f.Subject}` — {f.Message}");
        }

        static string Join(List<string> items) => items.Count == 0 ? "无" : string.Join(", ", items);
    }
}
