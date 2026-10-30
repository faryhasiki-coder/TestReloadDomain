using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 退出 Play、回到编辑模式后的残留探测（仅关闭 Domain Reload 时）。读取引擎/编辑器的静态回调列表，以及最近一次检查报告中
    /// 未处理的静态字段，报告指向已销毁对象的引用与回调，以及跨会话持续增长的订阅和集合。做 Play → Stop → Play 时可直接定位 owner，
    /// 补足元数据检查看不到的运行时行为（如挂在 UnityEditor 事件上的订阅）。
    /// 读取静态字段会触发尚未执行的静态构造，因此默认跳过有静态构造的项目类型（设置“残留探测读取有静态构造的类型”可放开）。
    /// </summary>
    internal static class ResidueProbe
    {
        public const string ReportPath = "Library/EasyReloadGuard/ResidueReport.md";
        const int k_ConsoleLines = 10;

        /// <summary>编辑器启动后即已初始化的引擎类型，读取其静态回调不会带来副作用。</summary>
        static readonly Type[] k_EngineTypes =
        {
            typeof(EditorApplication), typeof(AssemblyReloadEvents), typeof(CompilationPipeline), typeof(EditorSceneManager),
            typeof(SceneView), typeof(Selection), typeof(Undo), typeof(Application), typeof(SceneManager), typeof(Camera),
            typeof(Canvas), typeof(RenderPipelineManager),
        };

        static readonly HashSet<string> k_ProbedRules = new HashSet<string>(StringComparer.Ordinal)
        {
            RuleIds.StaticDelegate, RuleIds.StaticUnityObject, RuleIds.MutableStatic, RuleIds.ReadonlyState,
            RuleIds.AsyncResource, RuleIds.OwnerCoverage, RuleIds.AdapterKnownIssue,
        };

        static ResidueSnapshot s_Previous;
        static int s_Returns;

        internal static void OnEnteredEditMode()
        {
            var settings = ReloadGuardSettings.instance;
            if (!EditorCompat.DomainReloadDisabled || !settings.residueProbe) return;

            try
            {
                Run(settings.residueProbeReadTypeInitializers);
            }
            catch (Exception e)
            {
                // 工具自身故障不影响编辑器，但必须可见
                Log.Exception("退出 Play 后的残留探测失败，本次结果未记录。", e);
            }
        }

        static void Run(bool readTypeInitializers)
        {
            var audit = ReloadAudit.Last;
            var current = ResidueSnapshot.Capture(EngineCallbackFields(), ProjectFields(audit), readTypeInitializers);
            var result = Compare(s_Previous, current);
            var first = s_Previous == null;
            s_Previous = current;
            s_Returns++;

            var directory = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(ReportPath, ToMarkdown(result, current, audit != null, first));

            if (result.Count == 0)
            {
                Log.Verbose($"残留探测（本域第 {s_Returns} 次回到编辑模式）：未发现残留。读取 {current.Read} 个字段，跳过 {current.Skipped} 个。");
                return;
            }

            var lines = result.Destroyed.Concat(result.Growth).Take(k_ConsoleLines).Select(l => "  " + l);
            Log.Warning($"残留探测（本域第 {s_Returns} 次回到编辑模式）：已销毁对象引用 {result.Destroyed.Count} 项，" +
                        $"跨会话增长 {result.Growth.Count} 项。详见 {ReportPath}\n" + string.Join("\n", lines));
        }

        static IEnumerable<FieldInfo> EngineCallbackFields() =>
            k_EngineTypes.SelectMany(t => t.GetFields(ReflectionUtil.StaticMembers))
                .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType));

        /// <summary>检查报告中未处理（含已基线）的静态字段；已声明重置的字段每次进入 Play 都会恢复，不在探测范围内。</summary>
        static IEnumerable<FieldInfo> ProjectFields(AuditReport audit) =>
            audit == null
                ? Enumerable.Empty<FieldInfo>()
                : audit.Findings.Where(f => k_ProbedRules.Contains(f.RuleId)).Select(f => f.Member as FieldInfo).Where(f => f != null);

        /// <summary>已销毁对象的引用总是报告；订阅与集合只在本次比上次回到编辑模式时增长才报告。</summary>
        internal static ResidueResult Compare(ResidueSnapshot previous, ResidueSnapshot current)
        {
            var result = new ResidueResult();
            result.Destroyed.AddRange(current.Destroyed);
            if (previous == null) return result;

            foreach (var field in current.Callbacks)
            {
                previous.Callbacks.TryGetValue(field.Key, out var before);
                foreach (var method in field.Value)
                {
                    var old = before != null && before.TryGetValue(method.Key, out var count) ? count : 0;
                    if (method.Value > old) result.Growth.Add($"{field.Key} 的订阅 {method.Key}：{old} → {method.Value}");
                }
            }

            foreach (var field in current.Counts)
            {
                if (previous.Counts.TryGetValue(field.Key, out var old) && field.Value > old)
                    result.Growth.Add($"{field.Key} 的元素：{old} → {field.Value}");
            }

            return result;
        }

        static string ToMarkdown(ResidueResult result, ResidueSnapshot snapshot, bool hasAudit, bool first)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Easy Reload Guard 残留探测");
            sb.AppendLine();
            sb.AppendLine($"- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- Unity：{Application.unityVersion}");
            sb.AppendLine($"- Enter Play Mode：{EditorCompat.DescribePlayModeOptions()}");
            sb.AppendLine($"- 本域第 {s_Returns} 次回到编辑模式（会话 #{PlaySession.Id}）" +
                          (first ? "；首次只检查已销毁对象，增长从第 2 次开始比较" : string.Empty));
            sb.AppendLine($"- 读取字段 {snapshot.Read} 个，跳过 {snapshot.Skipped} 个（有静态构造或无法读取）" +
                          (hasAudit ? string.Empty : "；本域尚未运行检查，只探测了引擎回调，运行检查后可探测项目静态字段"));
            AppendSection(sb, "已销毁对象的引用", result.Destroyed);
            AppendSection(sb, "跨会话增长", result.Growth);
            return sb.ToString();
        }

        static void AppendSection(StringBuilder sb, string title, List<string> lines)
        {
            sb.AppendLine();
            sb.AppendLine($"## {title}（{lines.Count}）");
            sb.AppendLine();
            foreach (var line in lines) sb.AppendLine("- " + line);
        }
    }

    internal sealed class ResidueResult
    {
        public readonly List<string> Destroyed = new List<string>();
        public readonly List<string> Growth = new List<string>();

        public int Count => Destroyed.Count + Growth.Count;
    }

    /// <summary>一次回到编辑模式时静态状态的快照：回调按方法计数、集合元素数、指向已销毁对象的引用。</summary>
    internal sealed class ResidueSnapshot
    {
        const int k_MaxElements = 1000;

        public readonly Dictionary<string, Dictionary<string, int>> Callbacks = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly List<string> Destroyed = new List<string>();

        public int Read { get; private set; }
        public int Skipped { get; private set; }

        /// <summary>engineFields 总是读取；projectFields 中有静态构造的类型只在 readTypeInitializers 为 true 时读取。</summary>
        public static ResidueSnapshot Capture(IEnumerable<FieldInfo> engineFields, IEnumerable<FieldInfo> projectFields, bool readTypeInitializers)
        {
            var snapshot = new ResidueSnapshot();
            var seen = new HashSet<FieldInfo>();
            foreach (var field in engineFields)
                if (seen.Add(field))
                    snapshot.Probe(field);

            foreach (var field in projectFields)
            {
                if (!seen.Add(field)) continue;
                var type = field.DeclaringType;
                if (type == null || type.ContainsGenericParameters || (!readTypeInitializers && type.TypeInitializer != null))
                {
                    snapshot.Skipped++;
                    continue;
                }

                snapshot.Probe(field);
            }

            return snapshot;
        }

        void Probe(FieldInfo field)
        {
            object value;
            try
            {
                value = field.GetValue(null);
            }
            catch (Exception)
            {
                Skipped++;
                return;
            }

            Read++;
            Inspect(ReflectionUtil.Describe(field), value);
        }

        void Inspect(string label, object value)
        {
            switch (value)
            {
                case null:
                case string _:
                    return;
                case Delegate callback:
                    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var item in callback.GetInvocationList())
                    {
                        var method = $"{item.Method.DeclaringType?.FullName}.{item.Method.Name}";
                        counts[method] = counts.TryGetValue(method, out var count) ? count + 1 : 1;
                        if (IsDestroyed(item.Target)) Destroyed.Add($"{label} 的回调 {method} 的目标已销毁");
                    }

                    Callbacks[label] = counts;
                    return;
                case UnityEngine.Object unityObject:
                    if (unityObject == null) Destroyed.Add($"{label} 指向已销毁的 {unityObject.GetType().Name}");
                    return;
                case IEnumerable items:
                    InspectCollection(label, value, items);
                    return;
            }
        }

        void InspectCollection(string label, object value, IEnumerable items)
        {
            var count = CountOf(value);
            if (count < 0) return;
            Counts[label] = count;

            var destroyed = 0;
            var seen = 0;
            try
            {
                foreach (var item in value is IDictionary dictionary ? dictionary.Values : items)
                {
                    if (++seen > k_MaxElements) break;
                    if (IsDestroyed(item)) destroyed++;
                }
            }
            catch (Exception)
            {
                return; // 枚举时被修改或自定义集合不支持枚举，只记录数量
            }

            if (destroyed > 0) Destroyed.Add($"{label} 含 {destroyed} 个已销毁对象（共 {count} 个元素）");
        }

        static int CountOf(object value)
        {
            if (value is ICollection collection) return collection.Count;
            var property = value.GetType().GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.PropertyType != typeof(int) || property.GetIndexParameters().Length != 0) return -1;
            try
            {
                return (int)property.GetValue(value);
            }
            catch (Exception)
            {
                return -1;
            }
        }

        static bool IsDestroyed(object value) => value is UnityEngine.Object unityObject && unityObject == null;
    }
}
