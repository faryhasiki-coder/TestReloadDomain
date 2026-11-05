using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 暂缓清单（ProjectSettings/EasyReloadGuardBaseline.txt，纳入版本管理）：已知不符合规范、暂缓处理的问题。
    /// 清单内的问题不阻止进入 Play，但报告结论始终是“不符合规范（暂缓 N 项）”，列入清单不会变成符合。
    /// 只有“没有处理方式”一类问题可以列入；特性用错、适配器问题、[ReloadSafe] 证明不了、无法分析的类型不能列入。
    /// 另外记录对信任的 Unity 官方包的复核（静态字段签名），它们不计入结论。
    /// 每行是“规则编号|程序集|类型.成员[|证据]”，由工具生成和比对；文件中按问题类型加注释行，方便人阅读。
    /// </summary>
    internal static class Baseline
    {
        public const string FilePath = "ProjectSettings/EasyReloadGuardBaseline.txt";

        public static HashSet<string> Load()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(FilePath)) return keys;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var key = line.Trim();
                if (key.Length > 0 && !key.StartsWith("#", StringComparison.Ordinal)) keys.Add(key);
            }

            return keys;
        }

        /// <summary>
        /// 按当前清单重新标记每个问题；清单中已没有对应问题的行报告为建议。
        /// 适配器登记的已知问题来自按域缓存的重置计划，同一 Finding 会被多次检查复用，因此每次都要写回 true/false。
        /// </summary>
        public static void Apply(AuditReport report, HashSet<string> keys)
        {
            var present = new HashSet<string>(report.Findings.Select(f => f.Key), StringComparer.Ordinal);
            foreach (var finding in report.Findings)
                finding.Baselined = finding.Baselinable && keys.Contains(finding.Key);

            foreach (var key in keys.Where(k => !present.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
                report.Findings.Add(new Finding(RuleIds.StaleBaseline, Severity.Suggestion, key, "这一行已没有对应的问题（问题已修复或成员已改名），可以从暂缓清单中删除"));
        }

        public static DateTime LastWriteTimeUtc => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;

        /// <summary>写入的行：可暂缓的不符合项，以及信任的官方包的复核记录。</summary>
        internal static bool IsRecorded(Finding finding) =>
            finding.Baselinable && (finding.IsViolation || finding.RuleId == RuleIds.AdapterReviewChanged);

        public static int Write(AuditReport report)
        {
            var groups = report.Findings
                .Where(IsRecorded)
                .GroupBy(f => f.Key, StringComparer.Ordinal)
                .Select(g => g.First())
                .GroupBy(f => f.RuleId)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .ToList();

            var lines = new List<string>
            {
                "# Easy Reload Guard 暂缓清单：这里的问题已知不符合规范、暂缓处理，不阻止进入 Play，但报告结论仍是“不符合规范”。",
                "# 修复后删除对应行。由设置页 Project Settings/Easy Reload Guard 的“把当前问题列为暂缓”生成，不要手工添加新问题。",
                "# “信任的官方包中静态字段有变化”一组是对 Unity 官方包的复核记录，不计入结论。",
                "# 每行格式：规则编号|程序集|类型.成员（部分行末尾附带证据）。以 # 开头的行是说明。",
            };
            var count = 0;
            foreach (var group in groups)
            {
                lines.Add(string.Empty);
                lines.Add("# " + RuleIds.Title(group.Key));
                lines.AddRange(group.Select(f => f.Key).OrderBy(k => k, StringComparer.Ordinal));
                count += group.Count();
            }

            File.WriteAllLines(FilePath, lines);
            return count;
        }
    }
}
