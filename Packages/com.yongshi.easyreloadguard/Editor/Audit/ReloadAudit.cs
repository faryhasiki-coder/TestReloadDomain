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
    /// 离线检查：按规范判定检查范围内每个静态字段。符合规范 = 有恰好一种处理方式（本包执行的重置），或检查器能证明它在初始化之后不会再改变
    /// （<see cref="ChangeProof"/>）。不按出错的可能性分级：没有处理方式就是不符合规范；只给建议的内容单独列出，不计入结论。
    /// 读取反射元数据与方法体 IL（只读，见 <see cref="FieldAccessAnalyzer"/>），不触发静态构造。结果按域缓存，设置或暂缓清单变化时失效。
    /// </summary>
    internal static class ReloadAudit
    {
        public const string DefaultReportPath = "Library/EasyReloadGuard/AuditReport.md";

        /// <summary>静态入口的异步资源：退出方法对字段调用这些方法之一，视为已取消或释放。</summary>
        static readonly HashSet<string> k_StopCalls = new HashSet<string>(StringComparer.Ordinal)
        {
            "Cancel", "Dispose",
        };

        static AuditReport s_Last;
        static DateTime s_BaselineWriteTime;

        public static AuditReport Last => s_Last;

        /// <summary>本域缓存的结果；暂缓清单文件被外部修改（如版本管理拉取）时重新检查。</summary>
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
            var baseline = Baseline.Load();
            AdapterReview.Check(report, baseline);
            Baseline.Apply(report, baseline);
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
            // 程序集级 [ReloadSafe] 不再整体放行：只写明原因，其中每个字段仍逐个证明
            var assemblySafe = assembly.GetCustomAttribute<ReloadSafeAttribute>();
            if (assemblySafe != null) CheckReason(assemblySafe, assembly.GetName().Name, report);

            foreach (var type in ReflectionUtil.SafeGetTypes(assembly))
            {
                try
                {
                    ScanType(type, plan, report, assemblySafe, report.ReloadedAssemblies.Contains(assembly.GetName().Name));
                }
                catch (Exception e)
                {
                    report.Findings.Add(new Finding(RuleIds.ScanFailed, Severity.Violation, ReflectionUtil.Subject(type),
                        $"无法分析该类型，不能确认其中的静态字段符合规范：{e.GetBaseException().Message}"));
                }
            }
        }

        internal static void ScanType(Type type, ResetPlan plan, AuditReport report, ReloadSafeAttribute assemblySafe = null, bool reloaded = false)
        {
            if (ReflectionUtil.IsCompilerGenerated(type)) return;

            ReportAsyncVoid(type, report);
            CheckAsyncResources(type, plan, report);
            if (reloaded) return;
            ReportEnterPlayModeHooks(type, plan, report);

            // 类型级 [ReloadSafe] 同样不整体放行：其中每个字段逐个证明
            var typeSafe = type.GetCustomAttribute<ReloadSafeAttribute>(false);
            if (typeSafe != null) CheckReason(typeSafe, ReflectionUtil.Subject(type), report);
            var inheritedSafe = typeSafe ?? assemblySafe;

            var fields = type.GetFields(ReflectionUtil.StaticMembers);
            if (fields.Any(f => !f.IsLiteral))
            {
                var access = FieldAccessAnalyzer.Analyze(type);
                if (access.Error != null)
                {
                    report.Findings.Add(new Finding(RuleIds.ScanFailed, Severity.Violation, ReflectionUtil.Subject(type),
                        "无法完整分析该类型，不能证明字段不变或重置声明覆盖完整：" + access.Error));
                    return;
                }
            }
            if (plan.IsResetType(type))
            {
                foreach (var field in fields)
                    if (!field.IsLiteral && (inheritedSafe != null || field.IsDefined(typeof(ReloadSafeAttribute), false)))
                        CheckField(type, field, inheritedSafe, report);
                return;
            }
            var resetMethods = ResetMethodsOf(type, plan);
            if (resetMethods.Count > 0)
            {
                CheckOwnerCoverage(type, resetMethods, plan, report, inheritedSafe);
                return;
            }

            foreach (var field in fields)
            {
                if (field.IsLiteral) continue;
                if (plan.IsFieldHandled(field))
                {
                    if (inheritedSafe != null) CheckField(type, field, inheritedSafe, report);
                    continue;
                }
                if (field.IsDefined(typeof(CompilerGeneratedAttribute), false) && !ReflectionUtil.IsBackingField(type, field)) continue;
                CheckField(type, field, inheritedSafe, report);
            }
        }

        /// <summary>没有重置声明的字段：[ReloadSafe] 必须能证明不变；没有声明时，能证明不变则符合（可给建议），否则不符合规范。</summary>
        static void CheckField(Type type, FieldInfo field, ReloadSafeAttribute inheritedSafe, AuditReport report)
        {
            var access = FieldAccessAnalyzer.Analyze(type);
            var usage = access.Of(field);
            var whyNot = ChangeProof.WhyNotProvable(field, usage, access.Error);

            var fieldSafe = field.GetCustomAttribute<ReloadSafeAttribute>(false);
            if (fieldSafe != null) CheckReason(fieldSafe, ReflectionUtil.Subject(field), report);
            var safe = fieldSafe ?? inheritedSafe;
            if (safe != null)
            {
                if (whyNot != null) report.Findings.Add(UnprovableSafe(type, field, safe, usage, access.Error, whyNot));
                return;
            }

            if (whyNot == null)
            {
                var suggestion = ConstSuggestion(field, usage);
                if (suggestion != null) report.Findings.Add(suggestion);
                return;
            }

            var finding = Classify(field, usage);
            finding.Member = field;
            finding.Context = Describe(type, usage, access.Error) + $"；不能证明不变：{whyNot}";
            report.Findings.Add(finding);
        }

        static Finding UnprovableSafe(Type type, FieldInfo field, ReloadSafeAttribute safe, FieldUsage usage, string error, string whyNot) =>
            new Finding(RuleIds.SafeDrift, Severity.Violation, ReflectionUtil.Subject(field),
                $"{ReflectionUtil.Describe(field)} 声明为 [ReloadSafe(\"{safe.Reason}\")]，但不能证明它在初始化之后不变：{whyNot}。" +
                "[ReloadSafe] 只能用于能证明不变的字段；“逻辑上不会出错”“业务会自己重置”都不算。改用 [ResetStatic] 或 [ResetMethod]，" +
                "纯计算缓存同样重置（下一次 Play 重新计算）。")
            {
                Member = field,
                Context = Describe(type, usage, error),
            };

        /// <summary>能证明不变、但没有写成 const 或 readonly 的字段：给出建议，不计入结论。</summary>
        static Finding ConstSuggestion(FieldInfo field, FieldUsage usage)
        {
            if (field.IsInitOnly) return null;
            var fieldType = field.FieldType;
            var target = fieldType.IsPrimitive || fieldType.IsEnum || fieldType == typeof(string) ? "const 或 static readonly" : "static readonly";
            return new Finding(RuleIds.MutableStatic, Severity.Suggestion, ReflectionUtil.Subject(field),
                $"私有静态字段 {ReflectionUtil.Describe(field)} {(usage != null && usage.InitializerWrites ? "只在静态初始化时赋值" : "从未赋值")}，" +
                $"能证明不变，符合规范；可以改为 {target}，让编译器保证这一点。")
            {
                Member = field,
            };
        }

        /// <summary>按 IL 证据（初值、赋值与修改位置）给出该字段该用哪种处理方式；usage 为 null 时退回通用建议。全部不符合规范、可列为暂缓。</summary>
        static Finding Classify(FieldInfo field, FieldUsage usage)
        {
            var fieldType = field.FieldType;
            var subject = ReflectionUtil.Subject(field);
            var name = ReflectionUtil.Describe(field);

            if (typeof(Delegate).IsAssignableFrom(fieldType))
            {
                var kind = field.DeclaringType.GetEvent(field.Name, ReflectionUtil.StaticMembers) != null ? "静态事件" : "静态委托";
                return Violation(RuleIds.StaticDelegate, subject,
                    $"{kind} {name} 没有处理方式，停止 Play 后订阅仍然保留。订阅方应在自己销毁或退出时取消订阅；" +
                    "事件上加 [field: ResetStatic]，进入 Play 时清空剩余订阅。");
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(fieldType))
            {
                return field.IsInitOnly
                    ? Violation(RuleIds.ReadonlyState, subject, $"readonly {name} 引用的 Unity 对象没有处理方式。改为非 readonly 并加 [ResetStatic]。")
                    : Violation(RuleIds.StaticUnityObject, subject, $"{name} 没有处理方式，下一次 Play 会指向上一次 Play 已销毁的对象（单例常见）。" + UnityObjectAdvice(usage));
            }

            if (field.IsInitOnly)
            {
                if (fieldType.IsArray) return ReadonlyArray(subject, name, usage);
                if (FieldAccessAnalyzer.IsCollection(fieldType)) return ReadonlyCollection(subject, name, usage);
                return Violation(RuleIds.ReadonlyState, subject, usage != null && usage.Mutators.Count > 0
                    ? $"readonly 对象 {name} 在 {FieldUsage.Names(usage.Mutators)} 中被调用，内部状态（缓存、对象池、注册表）没有处理方式：用 [ResetMethod] 恢复。"
                    : $"readonly 对象 {name} 的内部状态（缓存、对象池、注册表）没有处理方式：用 [ResetMethod] 恢复。");
            }

            return MutableStatic(field, subject, name, usage);
        }

        static string UnityObjectAdvice(FieldUsage usage)
        {
            if (usage == null) return "用 [ResetStatic] 置空。";
            if (usage.Writers.Count > 0)
                return $"用 [ResetStatic] 置空，下一次 Play 由 {FieldUsage.Names(usage.Writers)} 重新赋值（确认它们会再次执行）。";
            if (usage.InitializerWrites)
                return "它只在静态初始化中赋值，置空后不会重建：用 [ResetMethod] 重新获取。";
            return "用 [ResetStatic] 置空。所在类型内没有赋值的地方，确认类型外的赋值在下一次 Play 会重新执行。";
        }

        static Finding ReadonlyArray(string subject, string name, FieldUsage usage)
        {
            if (usage != null && usage.Mutators.Count > 0)
                return Violation(RuleIds.ReadonlyState, subject, $"readonly 数组 {name} 的元素在 {FieldUsage.Names(usage.Mutators)} 中修改：用 [ResetMethod] 恢复内容。");
            return Violation(RuleIds.ReadonlyState, subject,
                $"readonly 数组 {name} 的元素可被修改，没有处理方式。只读查找表请改为私有、只读取元素，检查器能证明后即符合；否则用 [ResetMethod] 恢复内容。");
        }

        static Finding ReadonlyCollection(string subject, string name, FieldUsage usage)
        {
            if (usage == null)
                return Violation(RuleIds.ReadonlyState, subject, $"readonly 集合 {name} 没有处理方式：用 [ResetStatic(ResetMode.Clear)]；静态初始化中填充过内容时用 [ResetMethod] 恢复初始内容。");
            if (usage.InitializerContentsUnknown)
                return Violation(RuleIds.ReadonlyState, subject, $"readonly 集合 {name} 的初始内容与独占性不能确认：用 [ResetMethod] 明确恢复初始内容；不能直接用 Clear。");
            if (usage.Mutators.Count > 0) return MutatedCollection(subject, $"readonly 集合 {name}", usage);
            if (usage.InitializerMutates)
            {
                return Violation(RuleIds.ReadonlyState, subject,
                    $"readonly 集合 {name} 在静态初始化中填充，但不能证明之后只被读取（见读写位置）。只读表请改为私有、只用 TryGetValue/索引读取等方式访问，" +
                    "检查器能证明后即符合；否则用 [ResetMethod] 恢复初始内容（不要用 Clear，静态构造不会再次执行）。");
            }

            return Violation(RuleIds.ReadonlyState, subject, $"readonly 集合 {name} 没有处理方式：用 [ResetStatic(ResetMode.Clear)]。");
        }

        static Finding MutatedCollection(string subject, string what, FieldUsage usage)
        {
            var mutators = FieldUsage.Names(usage.Mutators);
            if (usage.InitializerContentsUnknown)
                return Violation(RuleIds.ReadonlyState, subject, $"{what} 在 {mutators} 中修改，初始内容无法确认：用 [ResetMethod] 明确恢复；不能直接用 Clear。");
            return Violation(RuleIds.ReadonlyState, subject, usage.InitializerMutates
                ? $"{what} 既在静态初始化时填充，又在 {mutators} 中修改：Clear 会丢失初始内容，用 [ResetMethod] 清除 Play 中加入的数据并恢复初始内容。"
                : $"{what} 在 {mutators} 中修改，内容会在多次 Play 之间累积：用 [ResetStatic(ResetMode.Clear)]；需要和其他字段一起恢复时用 [ResetMethod]。");
        }

        static Finding MutableStatic(FieldInfo field, string subject, string name, FieldUsage usage)
        {
            if (usage == null)
                return Violation(RuleIds.MutableStatic, subject, $"静态字段 {name} 没有处理方式。用 [ResetStatic] 或 [ResetMethod] 重置。");

            var fieldType = field.FieldType;
            var scope = usage.Complete ? string.Empty : "字段不是私有的，只统计了所在类型内的写入。";
            if (usage.Writers.Count == 0 && usage.Mutators.Count == 0)
            {
                if (usage.Complete)
                    return Violation(RuleIds.MutableStatic, subject,
                        $"私有静态字段 {name} 没有已识别的重新赋值，但引用的内容或使用方式不能证明不变；用 [ResetMethod] 恢复初始状态。" +
                        "仅把引用改成 readonly 不会保证内容不变。");
                return Violation(RuleIds.MutableStatic, subject,
                    $"静态字段 {name} 在所在类型内没有被赋值，但不是私有的，类型外是否赋值无法确认：外部会在 Play 中赋值时用 [ResetStatic]；" +
                    "不会赋值时改为 private 或 readonly，检查器能证明后即符合。");
            }

            if (usage.Writers.Count == 0)
            {
                if (FieldAccessAnalyzer.IsCollection(fieldType))
                    return Retag(MutatedCollection(subject, $"静态集合 {name}（引用不变，可改为 readonly）", usage), RuleIds.MutableStatic);
                return Violation(RuleIds.MutableStatic, subject,
                    $"静态对象 {name} 在 {FieldUsage.Names(usage.Mutators)} 中被调用，内部状态没有处理方式：用 [ResetMethod] 恢复。" + scope);
            }

            var writers = FieldUsage.Names(usage.Writers);
            string advice;
            if (fieldType.IsValueType || fieldType == typeof(string))
            {
                advice = usage.InitializerNonDefault
                    ? $"静态字段 {name} 有非默认初值，并在 {writers} 中修改：用 [ResetStatic(ResetMode.InitialValue)]。"
                    : $"静态字段 {name} 在 {writers} 中赋值、初值为默认值：用 [ResetStatic]。";
            }
            else
            {
                advice = usage.InitializerNonDefault
                    ? $"静态引用 {name} 有初值，并在 {writers} 中重新赋值：[ResetStatic] 会把初值清为 null，不等于刚加载时的值，用 [ResetMethod] 恢复初值。"
                    : $"静态引用 {name} 在 {writers} 中赋值、没有初值：用 [ResetStatic] 置空，下一次 Play 由这些地方重建（确认它们会再次执行，如 Awake、Init、判空创建）。";
            }

            if (usage.Mutators.Count > 0) advice += $"内容另在 {FieldUsage.Names(usage.Mutators)} 中修改。";
            return Violation(RuleIds.MutableStatic, subject, advice + scope);
        }

        /// <summary>规则编号决定暂缓清单中的行，非 readonly 集合沿用“静态字段”这一类，只复用集合的建议文字。</summary>
        static Finding Retag(Finding finding, string rule) =>
            new Finding(rule, finding.Severity, finding.Subject, finding.Message, finding.Baselinable);

        /// <summary>报告中“读写位置”一行：生成代码标记、初值、赋值与修改位置、分析范围。</summary>
        internal static string Describe(Type type, FieldUsage usage, string error)
        {
            if (usage == null) return error == null ? "没有读写位置分析结果" : $"IL 分析失败：{error}";
            var parts = new List<string>();
            if (type.IsDefined(typeof(GeneratedCodeAttribute), false)) parts.Add("生成代码（改生成模板，不要改生成物）");
            parts.Add(!usage.InitializerWrites ? "初值：无" : usage.InitializerNonDefault ? "初值：静态初始化中赋非默认值" : "初值：默认值");
            if (usage.InitializerMutates) parts.Add("初始内容：静态初始化中填充");
            parts.Add(usage.Writers.Count > 0 ? $"赋值：{FieldUsage.Names(usage.Writers)}" : "赋值：静态初始化以外没有");
            if (usage.Mutators.Count > 0) parts.Add($"内容修改：{FieldUsage.Names(usage.Mutators)}");
            parts.Add(usage.Complete ? "范围：私有字段，结论完整" : "范围：非私有字段，只统计了所在类型内，类型外的写入需要自己搜索");
            return string.Join("；", parts);
        }

        /// <summary>
        /// 已加的字段重置必须让字段回到刚加载时的值：有非默认初值的字段不能用 ResetMode.Default；静态初始化中填充过的集合不能用 Clear。
        /// 处理方式与初值不匹配属于声明错误，不能列为暂缓。泛型闭合构造按定义去重。
        /// </summary>
        internal static void ReviewDeclarations(ResetPlan plan, AuditReport report)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var step in plan.FieldSteps)
            {
                var field = FieldAccessAnalyzer.OpenField(step.Field);
                var subject = ReflectionUtil.Subject(field);
                if (!seen.Add(subject)) continue;

                var access = FieldAccessAnalyzer.Analyze(field.DeclaringType);
                var usage = access.Of(field);
                if (access.Error != null)
                {
                    report.Findings.Add(new Finding(RuleIds.ScanFailed, Severity.Violation, subject,
                        "无法核对重置方式与初值是否匹配：" + access.Error));
                    continue;
                }
                if (step.Mode == ResetMode.InitialValue && !InitializerPurity.IsPure(step.Field.DeclaringType))
                    report.Findings.Add(new Finding(RuleIds.InitialValueTiming, Severity.Suggestion, subject,
                        "静态构造不能证明为纯初始化，初值只能在第一次重置时记录；此前可能已被编辑器回调修改，初值可能不干净。"));
                var risk = DeclarationRisk(field, step.Mode, usage);
                if (risk == null) continue;
                report.Findings.Add(new Finding(RuleIds.DeclarationRisk, Severity.Violation, subject, risk)
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
            if (mode == ResetMode.Default && usage.InitializerNonDefault)
            {
                var fieldType = field.FieldType;
                var fix = fieldType.IsValueType || fieldType == typeof(string)
                    ? "改用 [ResetStatic(ResetMode.InitialValue)]"
                    : FieldAccessAnalyzer.IsCollection(fieldType) && !usage.InitializerMutates && !usage.InitializerContentsUnknown
                        ? "集合改为 readonly 并用 ResetMode.Clear，或用 [ResetMethod] 重新创建"
                        : "改用 [ResetMethod] 重新创建初值";
                return $"[ResetStatic] 会把有非默认初值的 {name} 写回 default，不等于刚加载时的值：{fix}。";
            }

            if (mode == ResetMode.Clear && usage.InitializerContentsUnknown)
                return $"无法证明 {name} 的集合初值为空（初值经方法、其他字段或带参数的构造取得），不能确认 Clear 会恢复初始内容；用 [ResetMethod] 明确恢复。";

            if (mode == ResetMode.Clear && usage.InitializerMutates)
            {
                return $"ResetMode.Clear 会清掉 {name} 在静态初始化中填充的内容，静态构造不会再次执行，不等于刚加载时的值：" +
                       (usage.Mutators.Count == 0
                           ? "内容之后只被读取时改为私有、只读访问，由检查器证明不变；否则用 [ResetMethod] 恢复初始内容。"
                           : "用 [ResetMethod] 清除 Play 中加入的数据并恢复初始内容。");
            }

            return null;
        }

        /// <summary>类型中由进入 Play 的重置方法负责：带 [ResetMethod] 的方法，以及适配器 InvokeOnReset 指定的方法。</summary>
        static List<MethodInfo> ResetMethodsOf(Type type, ResetPlan plan) =>
            type.GetMethods(ReflectionUtil.StaticMembers)
                .Where(m => m.IsDefined(typeof(ResetMethodAttribute), false))
                .Union(plan.EnterMethodsOf(type))
                .ToList();

        /// <summary>
        /// 由重置方法负责的类型：检查重置方法（含其调用的类型内方法）是否处理了全部不能证明不变的字段，防止用一个局部重置方法掩盖其他字段。
        /// 漏掉的字段属于覆盖不完整，不符合规范且不能列为暂缓；其中声明 [ReloadSafe] 的字段必须能证明不变。
        /// </summary>
        static void CheckOwnerCoverage(Type type, List<MethodInfo> resetMethods, ResetPlan plan, AuditReport report, ReloadSafeAttribute inheritedSafe)
        {
            var access = FieldAccessAnalyzer.Analyze(type);
            var touched = new HashSet<(Module, int)>();
            if (access.Error == null)
                foreach (var method in resetMethods)
                    touched.UnionWith(access.TouchedFrom(method));
            var owners = string.Join("、", resetMethods.Select(m => m.Name));

            foreach (var field in type.GetFields(ReflectionUtil.StaticMembers))
            {
                if (field.IsLiteral) continue;
                if (plan.IsFieldHandled(field))
                {
                    if (inheritedSafe != null) CheckField(type, field, inheritedSafe, report);
                    continue;
                }
                if (field.IsDefined(typeof(CompilerGeneratedAttribute), false) && !ReflectionUtil.IsBackingField(type, field)) continue;

                var usage = access.Of(field);
                var whyNot = ChangeProof.WhyNotProvable(field, usage, access.Error);
                var fieldSafe = field.GetCustomAttribute<ReloadSafeAttribute>(false);
                if (fieldSafe != null) CheckReason(fieldSafe, ReflectionUtil.Subject(field), report);
                var safe = fieldSafe ?? inheritedSafe;
                if (safe != null)
                {
                    if (whyNot != null) report.Findings.Add(UnprovableSafe(type, field, safe, usage, access.Error, whyNot));
                    continue;
                }

                if (whyNot == null || touched.Contains(TypeAccess.Key(field))) continue;

                report.Findings.Add(new Finding(RuleIds.OwnerCoverage, Severity.Violation, ReflectionUtil.Subject(field),
                    $"{ReflectionUtil.Describe(field)} 所在类型由重置方法 {owners} 负责，但这些方法（含其调用的类型内方法）没有赋值或清理该字段，" +
                    $"字段也不能证明不变（{whyNot}）：在重置方法中恢复它。")
                {
                    Member = field,
                    Context = Describe(type, usage, access.Error),
                });
            }
        }

        /// <summary>
        /// Unity 在编辑模式对象退出之前调用 [InitializeOnEnterPlayMode]，早于本包的重置：它写入的值会被本包的重置覆盖，失败时也没有兜底。
        /// 这类方法必须同时标 [ResetMethod]（或由适配器 InvokeOnReset 指定），由本包在字段重置之后再执行一次。
        /// </summary>
        static void ReportEnterPlayModeHooks(Type type, ResetPlan plan, AuditReport report)
        {
            var invoked = plan.EnterMethodsOf(type);
            foreach (var method in type.GetMethods(ReflectionUtil.StaticMembers))
            {
                if (!ResetRules.IsEnterPlayModeHook(method)) continue;
                if (method.IsDefined(typeof(ResetMethodAttribute), false) || invoked.Contains(method)) continue;
                report.Findings.Add(Violation(RuleIds.EnterPlayModeHook, ReflectionUtil.Subject(method),
                    $"{ReflectionUtil.Describe(method)} 标了 [InitializeOnEnterPlayMode]：Unity 在编辑模式对象退出之前调用它，早于本包的重置，" +
                    "它写入的值会被本包的重置覆盖，抛出异常时也没有兜底。给它加上 [ResetMethod]，由本包在字段重置之后再执行一次（要求可以重复执行）；" +
                    "第三方代码在适配器中用 InvokeOnReset 指定。"));
            }
        }

        /// <summary>
        /// async void 无法被调用方等待或取消，关闭 Domain Reload 时其 await 之后的代码可能在下一次 Play 中继续执行。
        /// 只检查直接声明的方法；lambda 编译为编译器生成类型的方法，不在检查范围内。
        /// </summary>
        static void ReportAsyncVoid(Type type, AuditReport report)
        {
            foreach (var method in type.GetMethods(ReflectionUtil.DeclaredMembers))
            {
                if (method.ReturnType != typeof(void) || !method.IsDefined(typeof(AsyncStateMachineAttribute), false)) continue;
                report.Findings.Add(Violation(RuleIds.AsyncVoid, ReflectionUtil.Subject(method),
                    $"async void {ReflectionUtil.Describe(method)}：规范不允许 async void，停止 Play 后它仍会继续执行，上一次 Play 的结果可能写进下一次 Play。" +
                    "改为返回 Task（或 UniTask 等可等待类型），由发起者传入自有 CancellationTokenSource、Application.exitCancellationToken 或 destroyCancellationToken，取消后不再写入状态。"));
            }
        }

        /// <summary>
        /// Task、CancellationTokenSource、线程、计时器不放在静态字段中；静态入口的服务例外，条件是同一类型中有退出方法
        /// （[OnExitPlayMode] 或适配器 InvokeOnExitPlayMode）对该字段调用 Cancel、Dispose 等。与字段有没有重置声明无关：只置空不会停止已在运行的工作。
        /// </summary>
        static void CheckAsyncResources(Type type, ResetPlan plan, AuditReport report)
        {
            var fields = type.GetFields(ReflectionUtil.StaticMembers).Where(f => IsAsyncResource(f.FieldType)).ToList();
            if (fields.Count == 0) return;

            var exitMethods = type.GetMethods(ReflectionUtil.StaticMembers)
                .Where(m => m.IsDefined(typeof(OnExitPlayModeAttribute), false))
                .Union(plan.ExitMethodsOf(type))
                .ToList();
            var access = FieldAccessAnalyzer.Analyze(type);
            var stopped = new HashSet<(Module, int)>();
            if (access.Error == null)
            {
                foreach (var method in exitMethods)
                foreach (var (field, callee) in access.CallsFrom(method))
                    if (k_StopCalls.Contains(callee))
                        stopped.Add(field);
            }

            foreach (var field in fields)
            {
                if (stopped.Contains(TypeAccess.Key(FieldAccessAnalyzer.OpenField(field)))) continue;
                report.Findings.Add(new Finding(RuleIds.AsyncResource, Severity.Violation, ReflectionUtil.Subject(field),
                    $"{ReflectionUtil.Describe(field)}（{field.FieldType.Name}）：规范不允许把异步任务、取消源、线程或计时器放在静态字段中，停止 Play 后它仍会运行。" +
                    "放到负责它的实例上，由实例的停止方法取消或释放；静态入口的服务需要在同一类型中有 [OnExitPlayMode] 方法对该字段调用 Cancel 或 Dispose。" +
                    "只把字段置 null 不会停止已在运行的工作。")
                {
                    Member = field,
                    Context = Describe(type, access.Of(field), access.Error),
                });
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
                report.Findings.Add(new Finding(RuleIds.MissingReason, Severity.Violation, subject, "[ReloadSafe] 必须写明为什么这个字段不会改变"));
        }

        static Finding Violation(string rule, string subject, string message) =>
            new Finding(rule, Severity.Violation, subject, message,
                baselinable: rule == RuleIds.MutableStatic || rule == RuleIds.ReadonlyState ||
                            rule == RuleIds.StaticDelegate || rule == RuleIds.StaticUnityObject);
    }
}
