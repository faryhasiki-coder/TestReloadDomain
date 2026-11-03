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

        /// <summary>所在目录放了排除标记、因此跳过的程序集 → “标记目录：原因”。</summary>
        public SortedDictionary<string, string> MarkerExcludedAssemblies { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public List<string> LegacySelfResetTypes { get; } = new List<string>();

        /// <summary>扫描过的程序集的来源（Assets、包 名称、DLL（…）），用于按类型汇总。</summary>
        public Dictionary<string, string> AssemblyOrigins { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

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
            $"待处理：错误 {Count(Severity.Error)}、警告 {Count(Severity.Warning)}、提示 {Count(Severity.Info)}；已知遗留 {BaselinedCount}";

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
            sb.AppendLine($"- {Summary()}。已知遗留指 {Baseline.FilePath} 中记录过的问题，仍列在报告末尾，但不阻止进入 Play");
            sb.AppendLine($"- 进入 Play 时重置 {Plan.FieldSteps.Count} 个字段、调用 {Plan.EnterSteps.Count} 个重置方法；退出 Play 时调用 {Plan.ExitSteps.Count} 个释放方法");
            sb.AppendLine($"- 扫描的程序集：{Join(ScannedAssemblies)}");
            sb.AppendLine($"- 由适配器声明为不需要处理、因此跳过的程序集：{Join(SelfHandledAssemblies)}");
            sb.AppendLine($"- 所在目录放了排除标记（{AuditScope.ExcludeMarkerFileName}）、因此跳过的程序集：{Join(MarkerExcludedAssemblies.Select(p => $"{p.Key}（{p.Value}）").ToList())}");
            sb.AppendLine($"- 已用 SubsystemRegistration 或 InitializeOnEnterPlayMode 自行重置、因此跳过的类型（{LegacySelfResetTypes.Count}）：{Join(LegacySelfResetTypes)}");
            sb.AppendLine();
            sb.AppendLine("## 第三方包与 DLL 的适配器");
            sb.AppendLine();
            sb.AppendLine("| 适配器 | 处理的包或程序集 | 已安装版本 | 版本已复核 | 不需要处理的依据 |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var adapter in Plan.Adapters)
            {
                sb.AppendLine($"| {adapter.Name} | {adapter.Target} | {adapter.InstalledVersion ?? "未安装（不生效）"} | " +
                              $"{(adapter.Active ? adapter.Verified ? "是" : "否" : "-")} | {adapter.SelfHandledEvidence ?? "-"} |");
            }

            AppendWorkList(sb);
            AppendSection(sb, "错误", Findings.Where(f => f.Severity == Severity.Error && !f.Baselined));
            AppendSection(sb, "警告", Findings.Where(f => f.Severity == Severity.Warning && !f.Baselined));
            AppendSection(sb, "提示", Findings.Where(f => f.Severity == Severity.Info && !f.Baselined));
            AppendSection(sb, "已知遗留（不阻止进入 Play）", Findings.Where(f => f.Baselined));
            return sb.ToString();
        }

        /// <summary>同一级别内按问题类型分小节：标题说明是什么问题，条目只写成员、原因与建议。</summary>
        static void AppendSection(StringBuilder sb, string title, IEnumerable<Finding> findings)
        {
            var list = findings.ToList();
            sb.AppendLine();
            sb.AppendLine($"## {title}（{list.Count}）");
            foreach (var group in list.GroupBy(f => f.Title))
            {
                sb.AppendLine();
                sb.AppendLine($"### {group.Key}（{group.Count()}）");
                sb.AppendLine();
                foreach (var f in group)
                {
                    sb.AppendLine($"- `{f.DisplaySubject}` — {f.Message}");
                    if (f.Context != null) sb.AppendLine($"  - 读写位置：{f.Context}");
                }
            }
        }

        /// <summary>
        /// 按来源 → 程序集 → 类型汇总待处理的错误与警告：Assets 源码在前，其次是包源码与 DLL，同来源内问题多的在前。
        /// 一个类型的静态状态通常由同一段代码负责，按类型分工比逐条处理更容易判断重置顺序与字段之间的关系。
        /// </summary>
        void AppendWorkList(StringBuilder sb)
        {
            var rows = Findings
                .Where(f => !f.Baselined && f.Severity != Severity.Info && f.Subject.IndexOf('|') > 0)
                .GroupBy(f => SplitSubject(f.Subject))
                .Select(g => new
                {
                    Assembly = g.Key.Item1,
                    Type = g.Key.Item2,
                    Origin = AssemblyOrigins.TryGetValue(g.Key.Item1, out var origin) ? origin : "未扫描（来自重置声明或适配器）",
                    Errors = g.Count(f => f.Severity == Severity.Error),
                    Warnings = g.Count(f => f.Severity == Severity.Warning),
                    Problems = string.Join("；", g.OrderBy(f => f.RuleId, StringComparer.Ordinal).Select(f => f.Title).Distinct()),
                })
                .OrderBy(r => OriginRank(r.Origin))
                .ThenByDescending(r => r.Errors)
                .ThenByDescending(r => r.Warnings)
                .ThenBy(r => r.Assembly, StringComparer.Ordinal)
                .ThenBy(r => r.Type, StringComparer.Ordinal)
                .ToList();

            sb.AppendLine();
            sb.AppendLine($"## 按类型汇总（{rows.Count} 个类型有待处理的错误或警告）");
            sb.AppendLine();
            sb.AppendLine("| 来源 | 程序集 | 类型 | 错误 | 警告 | 问题 |");
            sb.AppendLine("| --- | --- | --- | ---: | ---: | --- |");
            foreach (var r in rows) sb.AppendLine($"| {r.Origin} | {r.Assembly} | {r.Type} | {r.Errors} | {r.Warnings} | {r.Problems} |");
        }

        /// <summary>主体 "程序集|命名空间.类型.成员" 拆成 (程序集, 类型)。类型级主体（如无法分析的类型）没有成员部分，会归到其命名空间一行。</summary>
        internal static (string, string) SplitSubject(string subject)
        {
            var bar = subject.IndexOf('|');
            var assembly = subject.Substring(0, bar);
            var rest = subject.Substring(bar + 1);
            var dot = rest.LastIndexOf('.');
            return (assembly, dot > 0 ? rest.Substring(0, dot) : rest);
        }

        static int OriginRank(string origin)
        {
            if (origin == "Assets") return 0;
            if (origin.StartsWith("包 ", StringComparison.Ordinal)) return 1;
            return origin.StartsWith("DLL", StringComparison.Ordinal) ? 2 : 3;
        }

        static string Join(List<string> items) => items.Count == 0 ? "无" : string.Join(", ", items);
    }
}
