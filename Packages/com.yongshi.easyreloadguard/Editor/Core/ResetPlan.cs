using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 本次域内的重置计划：由特性与适配器生成，域重载后自然失效重建。
    /// 构建只读取反射元数据，不触发任何静态构造函数。
    /// </summary>
    internal sealed class ResetPlan
    {
        static ResetPlan s_Current;

        public readonly List<FieldResetStep> FieldSteps = new List<FieldResetStep>();
        public readonly List<ResetStep> EnterSteps = new List<ResetStep>();
        public readonly List<ResetStep> ExitSteps = new List<ResetStep>();
        public readonly List<Finding> Findings = new List<Finding>();
        public readonly List<AdapterStatus> Adapters = new List<AdapterStatus>();
        public readonly List<string> RequiredReloadReasons = new List<string>();

        readonly HashSet<string> m_HandledFields = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<Type> m_ManagedTypes = new HashSet<Type>();
        readonly Dictionary<string, AdapterStatus> m_SelfHandledTargets = new Dictionary<string, AdapterStatus>(StringComparer.Ordinal);

        /// <summary>项目适配器用 HandlesType 跳过检查的类型、声明它的适配器与依据。检查器记录其中的静态字段，变化时提醒复核。</summary>
        public readonly List<(Type Type, AdapterStatus Adapter, string Evidence)> ReviewedTypes =
            new List<(Type, AdapterStatus, string)>();

        public static ResetPlan Current => s_Current ?? (s_Current = BuildFromProject());

        public bool IsFieldHandled(FieldInfo field) => m_HandledFields.Contains(ReflectionUtil.Subject(field));

        public bool IsOwnerManaged(Type type) => m_ManagedTypes.Contains(type);

        public bool IsSelfHandled(string target) => m_SelfHandledTargets.ContainsKey(target);

        /// <summary>声明该包或程序集自行处理的项目适配器；内置适配器或未声明时为 null。</summary>
        public AdapterStatus ProjectSelfHandler(string target) =>
            m_SelfHandledTargets.TryGetValue(target, out var status) && !status.BuiltIn ? status : null;

        static ResetPlan BuildFromProject()
        {
            var plan = new ResetPlan();
            foreach (var field in TypeCache.GetFieldsWithAttribute<ResetStaticAttribute>().Where(IsProjectMember))
                plan.AddField(field, field.GetCustomAttribute<ResetStaticAttribute>(false).Mode, null);
            ScanPropertyAndEventAttributes(plan);
            foreach (var method in TypeCache.GetMethodsWithAttribute<ResetMethodAttribute>().Where(IsProjectMember))
                plan.AddMethod(method, method.GetCustomAttribute<ResetMethodAttribute>(false).Order, true, null);
            foreach (var method in TypeCache.GetMethodsWithAttribute<OnExitPlayModeAttribute>().Where(IsProjectMember))
                plan.AddMethod(method, method.GetCustomAttribute<OnExitPlayModeAttribute>(false).Order, false, null);

            var packages = EditorCompat.GetRegisteredPackages();
            foreach (var adapterType in TypeCache.GetTypesDerivedFrom<ReloadAdapter>())
            {
                if (!adapterType.IsAbstract && !ReflectionUtil.IsTestAssembly(adapterType.Assembly))
                    plan.ApplyAdapter(adapterType, packages);
            }

            plan.Seal();
            return plan;
        }

        /// <summary>
        /// TypeCache 没有 GetProperties/EventsWithAttribute，手动扫描直接标注了 [ResetStatic] 的属性和事件。
        /// 只有引用了特性所在程序集的程序集才可能使用该特性。
        /// </summary>
        static void ScanPropertyAndEventAttributes(ResetPlan plan)
        {
            var attributeAssembly = typeof(ResetStaticAttribute).Assembly;
            var attributeAssemblyName = attributeAssembly.GetName().Name;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.IsDynamic || ReflectionUtil.IsTestAssembly(asm)) continue;
                if (asm != attributeAssembly && !ReflectionUtil.References(asm, attributeAssemblyName)) continue;
                foreach (var type in ReflectionUtil.SafeGetTypes(asm)) plan.AddMemberAttributes(type);
            }
        }

        /// <summary>登记类型上直接标注了 [ResetStatic] 的属性和事件；实例成员也收集，以便报告声明错误。</summary>
        internal void AddMemberAttributes(Type type)
        {
            foreach (var prop in type.GetProperties(ReflectionUtil.DeclaredMembers))
            {
                var attr = prop.GetCustomAttribute<ResetStaticAttribute>(false);
                if (attr != null) AddAutoProperty(prop, attr.Mode, null);
            }

            foreach (var evt in type.GetEvents(ReflectionUtil.DeclaredMembers))
            {
                var attr = evt.GetCustomAttribute<ResetStaticAttribute>(false);
                if (attr != null) AddEvent(evt, attr.Mode, null);
            }
        }

        static bool IsProjectMember(MemberInfo member) => !ReflectionUtil.IsTestAssembly(member.DeclaringType.Assembly);

        internal void AddAutoProperty(PropertyInfo prop, ResetMode mode, string source)
        {
            var accessor = prop.GetMethod ?? prop.SetMethod;
            if (accessor != null && !accessor.IsStatic)
            {
                Findings.Add(Declaration(prop, source, ResetRules.InstanceStateError));
                return;
            }

            var backing = prop.DeclaringType.GetField(
                $"<{prop.Name}>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (backing != null)
            {
                if (!IsFieldHandled(backing))
                    AddField(backing, mode, source);
            }
            else
            {
                Findings.Add(new Finding(RuleIds.InvalidDeclaration, Severity.Error,
                    ReflectionUtil.Subject(prop),
                    "[ResetStatic] 在属性上只支持自动属性（需编译器生成 backing field）；手写属性请标记底层字段"));
            }
        }

        internal void AddEvent(EventInfo evt, ResetMode mode, string source)
        {
            if (evt.AddMethod != null && !evt.AddMethod.IsStatic)
            {
                Findings.Add(Declaration(evt, source, ResetRules.InstanceStateError));
                return;
            }

            var backing = evt.DeclaringType.GetField(evt.Name,
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (backing != null)
            {
                if (!IsFieldHandled(backing))
                    AddField(backing, mode, source);
            }
            else
            {
                Findings.Add(new Finding(RuleIds.InvalidDeclaration, Severity.Error,
                    ReflectionUtil.Subject(evt),
                    "[ResetStatic] 在事件上只支持字段式事件（field-like event）；自定义 add/remove 事件请标记底层字段"));
            }
        }

        internal void AddField(FieldInfo field, ResetMode mode, string source)
        {
            m_HandledFields.Add(ReflectionUtil.Subject(field));
            if (field.DeclaringType.IsGenericTypeDefinition)
            {
                ExpandGenericField(field, mode, source);
                return;
            }

            if (TryCreateFieldStep(field, mode, source, out var step)) FieldSteps.Add(step);
        }

        internal void AddMethod(MethodInfo method, int order, bool onEnter, string source)
        {
            var error = ResetRules.ValidateMethod(method);
            if (error != null)
            {
                Findings.Add(Declaration(method, source, error));
                return;
            }

            var step = new ActionStep(Prefix(source) + ReflectionUtil.Describe(method), order, () => method.Invoke(null, null));
            (onEnter ? EnterSteps : ExitSteps).Add(step);
            if (onEnter) m_ManagedTypes.Add(method.DeclaringType);
        }

        internal void AddAction(string source, string name, Action action, int order, bool onEnter)
        {
            if (action == null || string.IsNullOrWhiteSpace(name))
            {
                Findings.Add(new Finding(RuleIds.AdapterBroken, Severity.Error, source, "OnReset/OnExitPlayMode 需要非空名称与动作"));
                return;
            }

            (onEnter ? EnterSteps : ExitSteps).Add(new ActionStep(Prefix(source) + name, order, action));
        }

        internal void MarkOwnerManaged(Type type, AdapterStatus adapter = null, string evidence = null)
        {
            if (!m_ManagedTypes.Add(type) || adapter == null || adapter.BuiltIn) return;
            ReviewedTypes.Add((type, adapter, evidence));
        }

        internal void MarkSelfHandled(string target, AdapterStatus adapter) => m_SelfHandledTargets[target] = adapter;

        internal void RequireReload(string source, string reason)
        {
            RequiredReloadReasons.Add(Prefix(source) + reason);
            Findings.Add(new Finding(RuleIds.AdapterRequiresReload, Severity.Info, source,
                $"关闭 Domain Reload 时，每次进入 Play 前都会先重新加载脚本：{reason}"));
        }

        internal void ApplyAdapter(Type adapterType, IReadOnlyDictionary<string, InstalledPackage> packages) =>
            ApplyAdapter(adapterType, packages, adapterType.Assembly == typeof(ResetPlan).Assembly);

        /// <param name="builtIn">本包内置的适配器针对 Unity 官方发布的包，按 package.json 版本复核；项目适配器不比对版本。</param>
        internal void ApplyAdapter(Type adapterType, IReadOnlyDictionary<string, InstalledPackage> packages, bool builtIn)
        {
            ReloadAdapter adapter;
            AdapterStatus status;
            try
            {
                adapter = (ReloadAdapter)Activator.CreateInstance(adapterType);
                if (string.IsNullOrWhiteSpace(adapter.Target)) throw new InvalidOperationException("Target 为空");
                status = new AdapterStatus(adapterType.FullName, adapter.Target, adapter.TargetKind, builtIn);
            }
            catch (Exception e)
            {
                Findings.Add(new Finding(RuleIds.AdapterBroken, Severity.Error, adapterType.FullName,
                    $"无法初始化适配器（需要公开无参构造与非空 Target）：{e.GetBaseException().Message}"));
                return;
            }

            Adapters.Add(status);
            try
            {
                string version;
                if (adapter.TargetKind == AdapterTargetKind.Package)
                {
                    if (!packages.TryGetValue(adapter.Target, out var package)) return; // 目标不存在：适配器休眠
                    status.InstalledVersion = package.Describe();
                    version = package.Version;
                }
                else
                {
                    version = ReflectionUtil.FindLoadedAssembly(adapter.Target)?.GetName().Version?.ToString();
                    if (version == null) return;
                    status.InstalledVersion = version;
                }

                if (builtIn)
                {
                    var verified = adapter.VerifiedVersions ?? Array.Empty<string>();
                    status.Verified = Array.IndexOf(verified, version) >= 0;
                    if (!status.Verified)
                    {
                        Findings.Add(new Finding(RuleIds.AdapterUnverified, Severity.Warning, status.Name,
                            $"内置适配器复核过的 {adapter.Target} 版本为 [{string.Join(", ", verified)}]，当前为 {status.InstalledVersion}。" +
                            "请升级 Easy Reload Guard，或联系本包维护者复核该版本。"));
                    }
                }

                adapter.Configure(new ReloadAdapterBuilder(this, status));
            }
            catch (Exception e)
            {
                Findings.Add(new Finding(RuleIds.AdapterBroken, Severity.Error, status.Name,
                    $"适配器执行失败：{e.GetBaseException().Message}"));
            }
        }

        internal void Seal()
        {
            FieldSteps.Sort(ResetStep.Compare);
            EnterSteps.Sort(ResetStep.Compare);
            ExitSteps.Sort(ResetStep.Compare);
        }

        bool TryCreateFieldStep(FieldInfo field, ResetMode mode, string source, out FieldResetStep step)
        {
            step = null;
            var error = ResetRules.ValidateField(field, mode, out var clear);
            if (error != null)
            {
                Findings.Add(Declaration(field, source, error));
                return false;
            }

            step = new FieldResetStep(field, mode, clear, Prefix(source) + ReflectionUtil.Describe(field));
            return true;
        }

        void ExpandGenericField(FieldInfo openField, ResetMode mode, string source)
        {
            var count = 0;
            foreach (var closedType in ResetRules.FindClosedConstructions(openField.DeclaringType))
            {
                var field = closedType.GetField(openField.Name, ReflectionUtil.StaticMembers);
                if (field != null && TryCreateFieldStep(field, mode, source, out var step))
                {
                    FieldSteps.Add(step);
                    count++;
                }
            }

            var subject = ReflectionUtil.Subject(openField);
            const string hint = "直接使用、未经派生类出现的闭合类型无法静态发现，需在 Editor 适配器中按闭合类型用 ResetField 声明（不要在业务代码中插入登记）。";
            Findings.Add(count == 0
                ? new Finding(RuleIds.GenericExpansion, Severity.Warning, subject, "泛型静态字段没有找到任何闭合构造，未生成重置步骤。" + hint)
                : new Finding(RuleIds.GenericExpansion, Severity.Info, subject, $"泛型静态字段已展开 {count} 个由派生类型确定的闭合构造。" + hint));
        }

        static Finding Declaration(MemberInfo member, string source, string message) =>
            new Finding(RuleIds.InvalidDeclaration, Severity.Error, ReflectionUtil.Subject(member), Prefix(source) + message);

        static string Prefix(string source) => source == null ? string.Empty : $"[{source}] ";
    }
}
