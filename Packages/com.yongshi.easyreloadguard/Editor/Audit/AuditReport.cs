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

        /// <summary>适配器要求重新加载脚本的程序集；静态字段由域重载恢复，异步资源规则仍检查。</summary>
        public List<string> ReloadedAssemblies { get; } = new List<string>();

        /// <summary>由 Unity 官方包的 SelfHandled 覆盖、因此不检查的程序集（复核后信任）。</summary>
        public List<string> SelfHandledAssemblies { get; } = new List<string>();

        /// <summary>由项目适配器的 SelfHandled 覆盖的程序集 → 声明它的适配器。检查器记录其中的静态字段，变化时提醒复核。</summary>
        public SortedDictionary<string, AdapterStatus> ReviewedAssemblies { get; } = new SortedDictionary<string, AdapterStatus>(StringComparer.Ordinal);

        /// <summary>所在目录放了排除标记、因此不检查的程序集 → “标记目录：原因”。</summary>
        public SortedDictionary<string, string> MarkerExcludedAssemblies { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>设置中排除的包或程序集、因此不检查的程序集 → 排除方式。</summary>
        public SortedDictionary<string, string> SettingsExcludedAssemblies { get; } = new SortedDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>扫描过的程序集的来源（Assets、包 名称、DLL（…）），用于按类型汇总。</summary>
        public Dictionary<string, string> AssemblyOrigins { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>全部不符合项（含暂缓）。为 0 才符合规范。</summary>
        public int ViolationCount => Findings.Count(f => f.IsViolation);

        /// <summary>列在暂缓清单中的不符合项：不阻止进入 Play，仍然不符合规范。</summary>
        public int DeferredCount => Findings.Count(f => f.IsViolation && f.Baselined);

        /// <summary>不在暂缓清单中的不符合项：按设置阻止进入 Play。</summary>
        public int BlockingCount => Findings.Count(f => f.IsBlocking);

        /// <summary>建议（不计入结论）。已列入暂缓清单的官方包复核记录不计。</summary>
        public int SuggestionCount => Findings.Count(f => !f.IsViolation && !f.Baselined);

        public bool IsConformant => ViolationCount == 0;

        public void Sort() =>
            Findings.Sort((a, b) =>
            {
                var bySeverity = b.Severity.CompareTo(a.Severity);
                if (bySeverity != 0) return bySeverity;
                var byRule = string.CompareOrdinal(a.RuleId, b.RuleId);
                return byRule != 0 ? byRule : string.CompareOrdinal(a.Subject, b.Subject);
            });

        public string Summary() =>
            IsConformant
                ? $"符合规范；建议 {SuggestionCount}"
                : $"不符合规范：{ViolationCount} 项（待处理 {BlockingCount}、暂缓 {DeferredCount}）；建议 {SuggestionCount}";

        public string TopLines(int max)
        {
            var lines = Findings.Where(f => f.IsBlocking).Take(max).Select(f => "  " + f);
            return string.Join("\n", lines);
        }

        /// <summary>报告中“未检查范围”一节的条目：官方包的 SelfHandled、设置排除、目录排除标记。</summary>
        public List<string> UncheckedScope()
        {
            var items = new List<string>();
            items.AddRange(SelfHandledAssemblies.Select(a => $"{a}（Unity 官方包，复核后信任）"));
            items.AddRange(SettingsExcludedAssemblies.Select(p => $"{p.Key}（{p.Value}）"));
            items.AddRange(MarkerExcludedAssemblies.Select(p => $"{p.Key}（排除标记 {p.Value}）"));
            return items;
        }

        public string ToMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Easy Reload Guard 检查报告");
            sb.AppendLine();
            sb.AppendLine($"- **结论：{Summary()}**。按规范判定：每个静态字段要有本包执行的处理方式，或能证明在初始化之后不变；建议不计入结论");
            sb.AppendLine($"- 时间：{Time:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- Unity：{Application.unityVersion}");
            sb.AppendLine($"- Enter Play Mode：{PlayModeOptions}");
            sb.AppendLine($"- 暂缓指 {Baseline.FilePath} 中列出的不符合项：不阻止进入 Play，但仍然不符合规范");
            sb.AppendLine($"- 进入 Play 时重置 {Plan.FieldSteps.Count} 个字段、执行 {Plan.EnterSteps.Count} 个重置方法或动作；退出 Play 时执行 {Plan.ExitSteps.Count} 个释放方法或动作");
            sb.AppendLine($"- 扫描的程序集：{Join(ScannedAssemblies)}");
            sb.AppendLine($"- 静态字段通过重新加载脚本恢复：{Join(ReloadedAssemblies)}");
            sb.AppendLine("- 0.5.0 尚未执行重置后与初值的一致性比对；ResetType 动作和重置方法的实际结果需连续 Play → Stop 验收");
            sb.AppendLine($"- 未检查范围（不参与规范判定，但必须可见）：{Join(UncheckedScope())}");
            sb.AppendLine();
            sb.AppendLine("## 适配器");
            sb.AppendLine();
            sb.AppendLine("| 适配器 | 处理的包或程序集 | 已安装版本 | 复核方式 | 信任依据（SelfHandled） |");
            sb.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var adapter in Plan.Adapters)
            {
                sb.AppendLine($"| {adapter.Name} | {adapter.Target} | {adapter.InstalledVersion ?? "未安装（不生效）"} | " +
                              $"{(!adapter.Active ? "-" : !adapter.BuiltIn ? "比对静态字段" : adapter.Verified ? "版本已复核" : "版本未复核")} | {adapter.SelfHandledEvidence ?? "-"} |");
            }

            AppendWorkList(sb);
            AppendSection(sb, "不符合规范（待处理）", Findings.Where(f => f.IsBlocking));
            AppendSection(sb, "暂缓（仍不符合规范）", Findings.Where(f => f.IsViolation && f.Baselined));
            AppendSection(sb, "建议（不计入结论）", Findings.Where(f => !f.IsViolation && !f.Baselined));
            return sb.ToString();
        }

        /// <summary>同一部分内按问题类型分小节：标题说明是什么问题，条目只写成员、原因与处理方式。</summary>
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
        /// 按来源 → 程序集 → 类型汇总待处理的不符合项：Assets 源码在前，其次是包源码与 DLL，同来源内问题多的在前。
        /// 一个类型的静态状态通常由同一段代码负责，按类型分工比逐条处理更容易判断重置顺序与字段之间的关系。
        /// </summary>
        void AppendWorkList(StringBuilder sb)
        {
            var rows = Findings
                .Where(f => f.IsBlocking && f.Subject.IndexOf('|') > 0)
                .GroupBy(f => SplitSubject(f.Subject))
                .Select(g => new
                {
                    Assembly = g.Key.Item1,
                    Type = g.Key.Item2,
                    Origin = AssemblyOrigins.TryGetValue(g.Key.Item1, out var origin) ? origin : "未扫描（来自重置声明或适配器）",
                    Count = g.Count(),
                    Problems = string.Join("；", g.OrderBy(f => f.RuleId, StringComparer.Ordinal).Select(f => f.Title).Distinct()),
                })
                .OrderBy(r => OriginRank(r.Origin))
                .ThenByDescending(r => r.Count)
                .ThenBy(r => r.Assembly, StringComparer.Ordinal)
                .ThenBy(r => r.Type, StringComparer.Ordinal)
                .ToList();

            sb.AppendLine();
            sb.AppendLine($"## 按类型汇总（{rows.Count} 个类型有待处理的不符合项）");
            sb.AppendLine();
            sb.AppendLine("| 来源 | 程序集 | 类型 | 不符合 | 问题 |");
            sb.AppendLine("| --- | --- | --- | ---: | --- |");
            foreach (var r in rows) sb.AppendLine($"| {r.Origin} | {r.Assembly} | {r.Type} | {r.Count} | {r.Problems} |");
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
