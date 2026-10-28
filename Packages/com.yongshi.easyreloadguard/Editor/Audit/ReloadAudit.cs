using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 离线检查：扫描范围内所有静态字段，找出未声明处理方式的跨会话状态，并汇总重置计划与适配器的问题。
    /// 只读取反射元数据，不触发静态构造。结果按域缓存，设置或基线变化时失效。
    /// </summary>
    internal static class ReloadAudit
    {
        public const string DefaultReportPath = "Library/EasyReloadGuard/AuditReport.md";

        static AuditReport s_Last;
        static DateTime s_BaselineWriteTime;

        public static AuditReport Last => s_Last;

        /// <summary>本域缓存的结果；基线文件被外部修改（如版本管理拉取）时重新检查。</summary>
        public static AuditReport Cached =>
            s_Last != null && s_BaselineWriteTime == Baseline.LastWriteTimeUtc ? s_Last : Run();

        public static void Invalidate() => s_Last = null;

        public static AuditReport Run()
        {
            var plan = ResetPlan.Current;
            var report = new AuditReport(plan);
            report.Findings.AddRange(plan.Findings);
            foreach (var assembly in AuditScope.Resolve(ReloadGuardSettings.instance, plan, report))
                ScanAssembly(assembly, plan, report);
            s_BaselineWriteTime = Baseline.LastWriteTimeUtc;
            Baseline.Apply(report, Baseline.Load());
            report.Sort();
            WriteReport(report, DefaultReportPath);
            s_Last = report;
            return report;
        }

        public static void WriteReport(AuditReport report, string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, report.ToMarkdown());
        }

        internal static void ScanAssembly(Assembly assembly, ResetPlan plan, AuditReport report)
        {
            var assemblySafe = assembly.GetCustomAttribute<ReloadSafeAttribute>();
            if (assemblySafe != null)
            {
                CheckReason(assemblySafe, assembly.GetName().Name, report);
                return;
            }

            foreach (var type in ReflectionUtil.SafeGetTypes(assembly))
            {
                try
                {
                    ScanType(type, plan, report);
                }
                catch (Exception e)
                {
                    report.Findings.Add(new Finding(RuleIds.ScanFailed, Severity.Warning, ReflectionUtil.Subject(type),
                        $"无法分析该类型，已跳过：{e.GetBaseException().Message}"));
                }
            }
        }

        internal static void ScanType(Type type, ResetPlan plan, AuditReport report)
        {
            if (ReflectionUtil.IsCompilerGenerated(type)) return;

            var typeSafe = type.GetCustomAttribute<ReloadSafeAttribute>(false);
            if (typeSafe != null)
            {
                CheckReason(typeSafe, ReflectionUtil.Subject(type), report);
                return;
            }

            // async void 与静态状态的处理方式无关，owner 自行管理或沿用旧写法的类型也报告
            ReportAsyncVoid(type, report);

            if (plan.IsOwnerManaged(type)) return;
            if (ResetRules.IsLegacySelfReset(type))
            {
                report.LegacySelfResetTypes.Add(ReflectionUtil.TypeName(type));
                return;
            }

            foreach (var field in type.GetFields(ReflectionUtil.StaticMembers))
            {
                if (field.IsLiteral || plan.IsFieldHandled(field)) continue;

                var fieldSafe = field.GetCustomAttribute<ReloadSafeAttribute>(false);
                if (fieldSafe != null)
                {
                    CheckReason(fieldSafe, ReflectionUtil.Subject(field), report);
                    continue;
                }

                if (field.IsDefined(typeof(CompilerGeneratedAttribute), false) && !ReflectionUtil.IsBackingField(type, field))
                    continue;

                var finding = Classify(type, field);
                if (finding != null) report.Findings.Add(finding);
            }
        }

        static Finding Classify(Type type, FieldInfo field)
        {
            var fieldType = field.FieldType;
            var subject = ReflectionUtil.Subject(field);
            var name = ReflectionUtil.Describe(field);

            if (typeof(Delegate).IsAssignableFrom(fieldType))
            {
                if (field.IsInitOnly) return null; // readonly 委托不可变
                var kind = type.GetEvent(field.Name, ReflectionUtil.StaticMembers) != null ? "静态事件" : "静态委托";
                return Legacy(RuleIds.StaticDelegate, Severity.Error, subject,
                    $"{kind} {name} 的订阅会跨 Play 会话残留，导致重复回调或持有已销毁对象。" +
                    "订阅方在 [OnExitPlayMode] 中成对解除，或用 [field: ResetStatic] 清空。");
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
            {
                return field.IsInitOnly
                    ? Legacy(RuleIds.ReadonlyState, Severity.Warning, subject,
                        $"readonly {name} 引用的 Unity 对象在下次 Play 时可能已被销毁。改为非 readonly 并用 [ResetStatic]。")
                    : Legacy(RuleIds.StaticUnityObject, Severity.Error, subject,
                        $"{name} 在下次 Play 时会指向已销毁对象（== null 但不是真 null），单例常见。用 [ResetStatic] 置空。");
            }

            if (IsAsyncResource(fieldType))
            {
                return Legacy(RuleIds.AsyncResource, Severity.Warning, subject,
                    $"{name}（{fieldType.Name}）持有的异步工作或取消源会跨 Play 会话存活。" +
                    "由业务 owner 在会话结束时取消/释放（自有取消源、Application.exitCancellationToken 或 destroyCancellationToken），" +
                    "业务已有的停止方法可标注 [OnExitPlayMode]；只置 null 不会停止已在运行的工作。");
            }

            if (field.IsInitOnly)
            {
                if (fieldType.IsValueType || fieldType == typeof(string) || fieldType == typeof(Type) || fieldType == typeof(object))
                    return null;
                if (fieldType.IsArray)
                {
                    return Legacy(RuleIds.ReadonlyState, Severity.Info, subject,
                        $"readonly 数组 {name} 的元素可被修改。若只是查找表可忽略或用 [ReloadSafe] 说明。");
                }

                if (typeof(IEnumerable).IsAssignableFrom(fieldType))
                {
                    return Legacy(RuleIds.ReadonlyState, Severity.Warning, subject,
                        $"readonly 集合 {name} 的内容会跨 Play 会话累积。用 [ResetStatic(ResetMode.Clear)]。");
                }

                return Legacy(RuleIds.ReadonlyState, Severity.Info, subject,
                    $"readonly 对象 {name} 的内部状态（缓存/对象池/注册表）可能跨会话残留。复核后用 [ResetMethod] 重置或 [ReloadSafe] 说明。");
            }

            return Legacy(RuleIds.MutableStatic, Severity.Warning, subject,
                $"可变静态字段 {name} 的值会跨 Play 会话保留。用 [ResetStatic]/[ResetMethod] 处理，或用 [ReloadSafe(\"原因\")] 声明有意保留。");
        }

        static Finding Legacy(string rule, Severity severity, string subject, string message) =>
            new Finding(rule, severity, subject, message, baselinable: true);

        /// <summary>
        /// async void 无法被调用方等待或取消，关闭 Domain Reload 时其延续会在下一会话继续执行。
        /// 只检查直接声明的方法；lambda 编译为编译器生成类型的方法，不在检查范围内。
        /// </summary>
        static void ReportAsyncVoid(Type type, AuditReport report)
        {
            foreach (var method in type.GetMethods(ReflectionUtil.DeclaredMembers))
            {
                if (method.ReturnType != typeof(void) || !method.IsDefined(typeof(AsyncStateMachineAttribute), false)) continue;
                report.Findings.Add(Legacy(RuleIds.AsyncVoid, Severity.Warning, ReflectionUtil.Subject(method),
                    $"async void {ReflectionUtil.Describe(method)} 的延续会跨 Play 会话执行，旧结果可能写入新会话。" +
                    "由业务 owner 用自有取消源、Application.exitCancellationToken 或 destroyCancellationToken 结束它，并在 await 后不再写入已失效的状态。"));
            }
        }

        static bool IsAsyncResource(Type type)
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>)) return true;
            return type == typeof(ValueTask) ||
                   typeof(Task).IsAssignableFrom(type) ||
                   typeof(CancellationTokenSource).IsAssignableFrom(type) ||
                   typeof(Thread).IsAssignableFrom(type) ||
                   type == typeof(System.Threading.Timer) ||
                   typeof(System.Timers.Timer).IsAssignableFrom(type);
        }

        static void CheckReason(ReloadSafeAttribute attribute, string subject, AuditReport report)
        {
            if (string.IsNullOrWhiteSpace(attribute.Reason))
                report.Findings.Add(new Finding(RuleIds.MissingReason, Severity.Error, subject, "[ReloadSafe] 必须写明跨会话保留是安全的原因"));
        }
    }
}
