using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 离线检查：扫描范围内所有静态字段，找出未声明处理方式的跨会话状态，并汇总重置计划与适配器的问题。
    /// 读取反射元数据与方法体 IL（只读，见 <see cref="FieldAccessAnalyzer"/>），不触发静态构造。结果按域缓存，设置或基线变化时失效。
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
            ReviewDeclarations(plan, report);
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

            var resetMethods = ResetMethodsOf(type);
            if (resetMethods.Count > 0)
            {
                CheckOwnerCoverage(type, resetMethods, plan, report);
                return;
            }

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
            if (IsImmutableReadonly(field)) return null;
            var access = FieldAccessAnalyzer.Analyze(type);
            var usage = access.Of(field);
            var finding = Classify(field, usage);
            finding.Member = field;
            finding.Context = Describe(type, usage, access.Error);
            return finding;
        }

        /// <summary>按 IL 证据（初值、赋值与修改位置）给出针对该字段的处理建议；usage 为 null 时退回通用建议。</summary>
        static Finding Classify(FieldInfo field, FieldUsage usage)
        {
            var fieldType = field.FieldType;
            var subject = ReflectionUtil.Subject(field);
            var name = ReflectionUtil.Describe(field);

            if (typeof(Delegate).IsAssignableFrom(fieldType))
            {
                var kind = field.DeclaringType.GetEvent(field.Name, ReflectionUtil.StaticMembers) != null ? "静态事件" : "静态委托";
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
                        $"{name} 在下次 Play 时会指向已销毁对象（== null 但不是真 null），单例常见。" + UnityObjectAdvice(usage));
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
                if (fieldType.IsArray) return ReadonlyArray(subject, name, usage);
                if (FieldAccessAnalyzer.IsCollection(fieldType)) return ReadonlyCollection(subject, name, usage);
                return Legacy(RuleIds.ReadonlyState, Severity.Info, subject, usage != null && usage.Mutators.Count > 0
                    ? $"readonly 对象 {name} 在 {FieldUsage.Names(usage.Mutators)} 中被调用，内部状态（缓存/对象池/注册表）可能跨会话残留。" +
                      "复核后用 [ResetMethod] 重置或 [ReloadSafe] 说明。"
                    : $"readonly 对象 {name} 的内部状态（缓存/对象池/注册表）可能跨会话残留。复核后用 [ResetMethod] 重置或 [ReloadSafe] 说明。");
            }

            return MutableStatic(field, subject, name, usage);
        }

        static string UnityObjectAdvice(FieldUsage usage)
        {
            if (usage == null) return "用 [ResetStatic] 置空。";
            if (usage.Writers.Count > 0)
                return $"用 [ResetStatic] 置空，下一会话由 {FieldUsage.Names(usage.Writers)} 重新赋值（确认它们会再次执行）。";
            if (usage.InitializerWrites)
                return "它只在类型初始化中赋值，置空后不会重建：对象跨会话有效（如持久资产）时用 [ReloadSafe]，否则用 [ResetMethod] 重新获取。";
            return "用 [ResetStatic] 置空。声明类型内没有赋值位置，确认类型外的赋值在下一会话会重新执行。";
        }

        static Finding ReadonlyArray(string subject, string name, FieldUsage usage)
        {
            if (usage != null && usage.Mutators.Count > 0)
            {
                return Legacy(RuleIds.ReadonlyState, Severity.Warning, subject,
                    $"readonly 数组 {name} 的元素在 {FieldUsage.Names(usage.Mutators)} 中修改，会跨 Play 会话保留：用 [ResetMethod] 恢复内容。");
            }

            return Legacy(RuleIds.ReadonlyState, Severity.Info, subject,
                $"readonly 数组 {name} 的元素可被修改。若只是查找表可忽略或用 [ReloadSafe] 说明。");
        }

        static Finding ReadonlyCollection(string subject, string name, FieldUsage usage)
        {
            if (usage == null)
            {
                return Legacy(RuleIds.ReadonlyState, Severity.Warning, subject,
                    $"readonly 集合 {name} 的内容会跨 Play 会话累积。用 [ResetStatic(ResetMode.Clear)]；" +
                    "若内容只在类型初始化时填充，Clear 后不会重新填充，改用 [ReloadSafe]。");
            }

            if (usage.Mutators.Count > 0) return MutatedCollection(subject, $"readonly 集合 {name}", usage);

            if (usage.Complete)
            {
                return Legacy(RuleIds.ReadonlyState, Severity.Info, subject,
                    $"readonly 集合 {name} 只在类型初始化时填充、之后不再修改，内容不会跨会话变化。" +
                    "不要用 ResetMode.Clear（静态构造不会再次执行，清空后不会重新填充）；可用 [ReloadSafe(\"原因\")] 说明。");
            }

            return Legacy(RuleIds.ReadonlyState, Severity.Warning, subject,
                $"readonly 集合 {name} 在声明类型内没有修改位置，字段非私有，类型外是否修改未分析。外部不修改时用 [ReloadSafe]；外部会追加时" +
                (usage.InitializerMutates
                    ? "，Clear 会同时丢失类型初始化中填充的内容，用 [ResetMethod] 清除会话数据并恢复初始内容。"
                    : "用 [ResetStatic(ResetMode.Clear)]。"));
        }

        static Finding MutatedCollection(string subject, string what, FieldUsage usage)
        {
            var mutators = FieldUsage.Names(usage.Mutators);
            return Legacy(RuleIds.ReadonlyState, Severity.Warning, subject, usage.InitializerMutates
                ? $"{what} 既在类型初始化时填充，又在 {mutators} 中修改：Clear 会丢失初始化内容，用 [ResetMethod] 清除会话数据并恢复初始内容。"
                : $"{what} 在 {mutators} 中修改，内容会跨 Play 会话累积：用 [ResetStatic(ResetMode.Clear)]；需要与其他字段一起恢复不变量时用 [ResetMethod]。");
        }

        static Finding MutableStatic(FieldInfo field, string subject, string name, FieldUsage usage)
        {
            if (usage == null)
            {
                return Legacy(RuleIds.MutableStatic, Severity.Warning, subject,
                    $"可变静态字段 {name} 的值会跨 Play 会话保留。用 [ResetStatic]/[ResetMethod] 处理，或用 [ReloadSafe(\"原因\")] 声明有意保留。");
            }

            var fieldType = field.FieldType;
            var scope = usage.Complete ? string.Empty : "字段非私有，只统计了声明类型内的写入。";
            if (usage.Writers.Count == 0 && usage.Mutators.Count == 0)
            {
                if (usage.Complete)
                {
                    var target = fieldType.IsPrimitive || fieldType.IsEnum || fieldType == typeof(string) ? "const 或 static readonly" : "static readonly";
                    return Legacy(RuleIds.MutableStatic, Severity.Info, subject,
                        $"私有静态字段 {name} {(usage.InitializerWrites ? "只在类型初始化时赋值" : "从未赋值")}，之后不再改变，值不会跨会话变化：改为 {target}。");
                }

                return Legacy(RuleIds.MutableStatic, Severity.Warning, subject,
                    $"可变静态字段 {name} 在声明类型内没有赋值位置（类型外的写入未分析）：外部会在 Play 中赋值时用 [ResetStatic]，" +
                    "否则改为 readonly 或用 [ReloadSafe(\"原因\")] 说明。");
            }

            if (usage.Writers.Count == 0)
            {
                if (FieldAccessAnalyzer.IsCollection(fieldType))
                    return Retag(MutatedCollection(subject, $"静态集合 {name}（引用不变，可改为 readonly）", usage), RuleIds.MutableStatic);
                return Legacy(RuleIds.MutableStatic, Severity.Warning, subject,
                    $"静态对象 {name} 在 {FieldUsage.Names(usage.Mutators)} 中被调用，内部状态可能跨会话残留：用 [ResetMethod] 重置或 [ReloadSafe] 说明。" + scope);
            }

            var writers = FieldUsage.Names(usage.Writers);
            string advice;
            if (fieldType.IsValueType || fieldType == typeof(string))
            {
                advice = usage.InitializerWrites
                    ? $"可变静态字段 {name} 有初值，并在 {writers} 中修改：初值不是 default 时用 [ResetStatic(ResetMode.InitialValue)]，否则用 [ResetStatic]。"
                    : $"可变静态字段 {name} 在 {writers} 中赋值、没有显式初值：用 [ResetStatic]。";
            }
            else
            {
                advice = usage.InitializerWrites
                    ? $"静态引用 {name} 有初值，并在 {writers} 中重新赋值：[ResetStatic] 会把初值清为 null，只有下一会话必然先经过 {writers} 才可用；" +
                      "否则用 [ResetMethod] 恢复初值。"
                    : $"静态引用 {name} 在 {writers} 中赋值、没有初值：用 [ResetStatic] 置空，下一会话由这些位置重建（确认它们会再次执行，如 Awake/Init/判空创建）。";
            }

            if (usage.Mutators.Count > 0) advice += $"内容另在 {FieldUsage.Names(usage.Mutators)} 中修改。";
            return Legacy(RuleIds.MutableStatic, Severity.Warning, subject, advice + scope);
        }

        /// <summary>规则编号决定基线键，非 readonly 集合沿用 RG003，只复用集合的建议文字。</summary>
        static Finding Retag(Finding finding, string rule) =>
            new Finding(rule, finding.Severity, finding.Subject, finding.Message, finding.Baselinable);

        /// <summary>readonly 的值类型、string、Type、object 与委托不可变，不需要处理。</summary>
        static bool IsImmutableReadonly(FieldInfo field)
        {
            if (!field.IsInitOnly) return false;
            var type = field.FieldType;
            return type.IsValueType || type == typeof(string) || type == typeof(Type) || type == typeof(object) ||
                   typeof(Delegate).IsAssignableFrom(type);
        }

        /// <summary>报告中“上下文”一行：生成代码标记、初值、赋值与修改位置、分析范围。</summary>
        internal static string Describe(Type type, FieldUsage usage, string error)
        {
            if (usage == null) return error == null ? null : $"IL 分析失败：{error}";
            var parts = new List<string>();
            if (type.IsDefined(typeof(GeneratedCodeAttribute), false)) parts.Add("生成代码（改生成模板，不要改生成物）");
            parts.Add(usage.InitializerWrites ? "初值：类型初始化中赋值" : "初值：无");
            if (usage.InitializerMutates) parts.Add("初始内容：类型初始化中填充");
            parts.Add(usage.Writers.Count > 0 ? $"赋值：{FieldUsage.Names(usage.Writers)}" : "赋值：类型初始化外无");
            if (usage.Mutators.Count > 0) parts.Add($"内容修改：{FieldUsage.Names(usage.Mutators)}");
            parts.Add(usage.Complete ? "范围：私有字段，结论完整" : "范围：非私有字段，只统计声明类型内");
            return string.Join("；", parts);
        }

        /// <summary>
        /// 复核已声明的字段重置会不会破坏下一会话（RG013）：类型初始化创建、类型内没有其他重建位置的引用被 Default 置空；
        /// 类型初始化中填充的集合被 Clear。泛型闭合构造按定义去重。
        /// </summary>
        internal static void ReviewDeclarations(ResetPlan plan, AuditReport report)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var step in plan.FieldSteps)
            {
                var field = FieldAccessAnalyzer.OpenField(step.Field);
                var subject = ReflectionUtil.Subject(field);
                if (!seen.Add(subject)) continue;

                var usage = FieldAccessAnalyzer.Analyze(field.DeclaringType).Of(field);
                var risk = DeclarationRisk(field, step.Mode, usage);
                if (risk == null) continue;
                report.Findings.Add(new Finding(RuleIds.DeclarationRisk, Severity.Warning, subject, risk, baselinable: true)
                {
                    Member = field,
                    Context = Describe(field.DeclaringType, usage, null),
                });
            }
        }

        static string DeclarationRisk(FieldInfo field, ResetMode mode, FieldUsage usage)
        {
            if (usage == null) return null;
            var name = ReflectionUtil.Describe(field);
            var scope = usage.Complete ? string.Empty : "（字段非私有，类型外的写入未分析）";
            if (mode == ResetMode.Default && !field.FieldType.IsValueType && usage.InitializerWrites && usage.Writers.Count == 0)
            {
                return $"[ResetStatic] 会把 {name} 在类型初始化中创建的值置为 null，而声明类型内没有其他赋值位置能重建它{scope}。" +
                       "改用 [ResetMethod] 重新创建初值；确认类型外会在下一会话重新赋值时可写入基线。";
            }

            if (mode == ResetMode.Clear && usage.InitializerMutates)
            {
                return usage.Mutators.Count == 0
                    ? $"ResetMode.Clear 会清掉 {name} 在类型初始化中填充的内容，声明类型内没有其他位置会重新填充{scope}：内容确定不变时改用 [ReloadSafe(\"原因\")]。"
                    : $"ResetMode.Clear 会清掉 {name} 在类型初始化中填充的内容（{FieldUsage.Names(usage.Mutators)} 只修改会话数据，不会补回）：" +
                      "改用 [ResetMethod] 清除会话数据并恢复初始内容。";
            }

            return null;
        }

        static List<MethodInfo> ResetMethodsOf(Type type) =>
            type.GetMethods(ReflectionUtil.StaticMembers).Where(m => m.IsDefined(typeof(ResetMethodAttribute), false)).ToList();

        /// <summary>
        /// [ResetMethod] 让类型整体视为 owner 自行管理。检查重置方法（含其调用的类型内方法）是否处理了全部可变静态字段（RG014），
        /// 防止用一个局部重置方法掩盖其他字段；有意保留的字段用 [ReloadSafe] 说明。
        /// </summary>
        static void CheckOwnerCoverage(Type type, List<MethodInfo> resetMethods, ResetPlan plan, AuditReport report)
        {
            var access = FieldAccessAnalyzer.Analyze(type);
            if (access.Error != null) return;

            var touched = new HashSet<(Module, int)>();
            foreach (var method in resetMethods) touched.UnionWith(access.TouchedFrom(method));
            var owners = string.Join("、", resetMethods.Select(m => m.Name));

            foreach (var field in type.GetFields(ReflectionUtil.StaticMembers))
            {
                if (field.IsLiteral || plan.IsFieldHandled(field) || IsImmutableReadonly(field)) continue;
                var fieldSafe = field.GetCustomAttribute<ReloadSafeAttribute>(false);
                if (fieldSafe != null)
                {
                    CheckReason(fieldSafe, ReflectionUtil.Subject(field), report);
                    continue;
                }

                if (field.IsDefined(typeof(CompilerGeneratedAttribute), false) && !ReflectionUtil.IsBackingField(type, field)) continue;
                if (touched.Contains(TypeAccess.Key(field))) continue;

                report.Findings.Add(new Finding(RuleIds.OwnerCoverage, Severity.Warning, ReflectionUtil.Subject(field),
                    $"{ReflectionUtil.Describe(field)} 所在类型由 [ResetMethod] {owners} 管理，但重置方法（含其调用的类型内方法）没有赋值或清理该字段：" +
                    "有意跨会话保留时加 [ReloadSafe(\"原因\")]，否则在重置方法中恢复。", baselinable: true)
                {
                    Member = field,
                    Context = Describe(type, access.Of(field), null),
                });
            }
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
