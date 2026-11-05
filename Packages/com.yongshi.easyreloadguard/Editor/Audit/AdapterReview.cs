using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 项目适配器对 Unity 官方包用 SelfHandled（复核后信任）时，按“静态字段签名”判断是否需要重新复核，而不是按版本号：
    /// 版本号既会在无关更新时误报，又会在不改版本时漏报。每个类型一条“信任的官方包中静态字段有变化”，标识中附带当前的字段签名；
    /// 列入暂缓清单即表示已复核。之后字段增删或类型变化时这一行失效，报告列出与上次记录相比的差异。
    /// 这些包不在规范判定范围内（报告中列为未检查范围），所以这一条只是建议，不计入结论。只比对字段定义，不比对方法体。
    /// </summary>
    internal static class AdapterReview
    {
        const string k_DetailPrefix = "字段=";
        const int k_MaxListed = 20;

        public static void Check(AuditReport report, HashSet<string> baseline)
        {
            var seen = new HashSet<Type>();
            foreach (var pair in report.ReviewedAssemblies)
            {
                var assembly = ReflectionUtil.FindLoadedAssembly(pair.Key);
                if (assembly == null) continue;
                var how = $"SelfHandled（依据：{pair.Value.SelfHandledEvidence}）";
                foreach (var type in ReflectionUtil.SafeGetTypes(assembly))
                {
                    if (!ReflectionUtil.IsCompilerGenerated(type) && seen.Add(type)) Add(report, baseline, type, pair.Value, how);
                }
            }
        }

        static void Add(AuditReport report, HashSet<string> baseline, Type type, AdapterStatus adapter, string how)
        {
            var signatures = Signatures(type);
            if (signatures.Count == 0) return;

            var finding = new Finding(RuleIds.AdapterReviewChanged, Severity.Suggestion, ReflectionUtil.Subject(type),
                Describe(adapter.Name, how, signatures, Previous(baseline, RuleIds.AdapterReviewChanged + "|" + ReflectionUtil.Subject(type))),
                baselinable: true)
            {
                KeyDetail = k_DetailPrefix + string.Join(";", signatures),
            };
            report.Findings.Add(finding);
        }

        /// <summary>类型自身声明的静态字段签名（名称:类型，只读字段加 readonly），排序后返回。常量与编译器生成的缓存字段不计。</summary>
        internal static List<string> Signatures(Type type)
        {
            var result = new List<string>();
            foreach (var field in type.GetFields(ReflectionUtil.StaticMembers))
            {
                if (field.IsLiteral) continue;
                if (field.IsDefined(typeof(CompilerGeneratedAttribute), false) && !ReflectionUtil.IsBackingField(type, field)) continue;
                result.Add($"{field.Name}:{TypeLabel(field.FieldType)}{(field.IsInitOnly ? " readonly" : string.Empty)}");
            }

            result.Sort(StringComparer.Ordinal);
            return result;
        }

        /// <summary>暂缓清单中同一类型上一次记录的字段签名；没有记录时为 null。</summary>
        internal static HashSet<string> Previous(HashSet<string> baseline, string keyPrefix)
        {
            var prefix = keyPrefix + "|" + k_DetailPrefix;
            var line = baseline.FirstOrDefault(k => k.StartsWith(prefix, StringComparison.Ordinal));
            if (line == null) return null;
            var detail = line.Substring(prefix.Length);
            return new HashSet<string>(detail.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
        }

        internal static string Describe(string adapter, string how, List<string> current, HashSet<string> previous)
        {
            if (previous == null)
            {
                return $"[{adapter}] 用 {how} 信任该官方包，还没有记录复核时的静态字段：{List(current)}。" +
                       "复核后列入暂缓清单；之后这些字段有增删或类型变化时会再次提醒。";
            }

            var added = current.Where(s => !previous.Contains(s)).ToList();
            var removed = previous.Where(s => !current.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList();
            var changes = new List<string>();
            if (added.Count > 0) changes.Add("新增或改变 " + List(added));
            if (removed.Count > 0) changes.Add("删除 " + List(removed));
            return $"[{adapter}] 用 {how} 信任该官方包，静态字段与上次记录相比有变化：{string.Join("；", changes)}。" +
                   "重新复核这些字段：仍可信任的，重新列入暂缓清单作为复核记录；不能信任的，用 ResetField、InvokeOnReset 处理或用 KnownIssue 登记。";
        }

        static string List(List<string> items) =>
            items.Count <= k_MaxListed
                ? string.Join("，", items)
                : string.Join("，", items.Take(k_MaxListed)) + $" 等 {items.Count} 个";

        /// <summary>字段类型的短名，泛型写出参数（如 Dictionary&lt;String,List&lt;Int32&gt;&gt;）。</summary>
        internal static string TypeLabel(Type type)
        {
            if (type.IsArray) return TypeLabel(type.GetElementType()) + "[]";
            if (!type.IsGenericType) return type.Name;
            var name = type.Name;
            var tick = name.IndexOf('`');
            if (tick > 0) name = name.Substring(0, tick);
            return name + "<" + string.Join(",", type.GetGenericArguments().Select(TypeLabel)) + ">";
        }
    }
}
