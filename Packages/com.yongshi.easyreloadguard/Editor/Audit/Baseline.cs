using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 遗留问题基线（ProjectSettings/EasyReloadGuardBaseline.txt，纳入版本管理）。
    /// 基线内的问题不阻断 Play 但仍出现在报告中；新增问题照常阻断。声明错误与适配器问题不可入基线。
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
        /// 按当前基线重新标记每个问题；基线中已不对应任何当前问题的条目报告为提示。
        /// 适配器问题（RG023）来自按域缓存的重置计划，同一 Finding 会被多次检查复用，因此每次都要写回 true/false。
        /// </summary>
        public static void Apply(AuditReport report, HashSet<string> keys)
        {
            var present = new HashSet<string>(report.Findings.Select(f => f.Key), StringComparer.Ordinal);
            foreach (var finding in report.Findings)
                finding.Baselined = finding.Baselinable && keys.Contains(finding.Key);

            foreach (var key in keys.Where(k => !present.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
                report.Findings.Add(new Finding(RuleIds.StaleBaseline, Severity.Info, key, "基线条目已不存在对应问题，可删除该行"));
        }

        public static DateTime LastWriteTimeUtc => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;

        public static int Write(AuditReport report)
        {
            var keys = report.Findings
                .Where(f => f.Baselinable && f.Severity != Severity.Info)
                .Select(f => f.Key)
                .Distinct()
                .OrderBy(k => k, StringComparer.Ordinal)
                .ToList();

            var lines = new List<string>
            {
                "# Easy Reload Guard 基线：已知的遗留静态状态问题，不阻断 Play，但仍出现在检查报告中。",
                "# 修复后删除对应行。由 Tools/Easy Reload Guard/将当前遗留问题写入基线 生成，不要手工添加新问题。",
            };
            lines.AddRange(keys);
            File.WriteAllLines(FilePath, lines);
            return keys.Count;
        }
    }
}
