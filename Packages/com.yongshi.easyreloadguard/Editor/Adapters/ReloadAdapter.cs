using System;
using System.Collections.Generic;
using System.Linq;

namespace EasyReloadGuard.Editor
{
    public enum AdapterTargetKind
    {
        /// <summary>Target 为 UPM 包名，版本取 Package Manager 登记的版本。</summary>
        Package,

        /// <summary>Target 为程序集名（预编译 DLL 等），版本取 AssemblyVersion。</summary>
        Assembly,
    }

    /// <summary>
    /// 为暂时无法按规范修改源码的代码（第三方包、DLL、生成代码、其他团队负责的代码）声明处理方式：由适配器替它完成重置或释放。
    /// 适配器做的是“处理”，不是“豁免”；唯一的例外是对 Unity 官方提供的包用 SelfHandled 采用“复核后信任”。
    /// 放在任意 Editor 程序集中，需要公开无参构造；目标不存在时休眠。
    /// </summary>
    public abstract class ReloadAdapter
    {
        public abstract string Target { get; }

        public virtual AdapterTargetKind TargetKind => AdapterTargetKind.Package;

        /// <summary>
        /// 复核时的目标版本，只作记录，项目适配器不按它判断是否需要重新复核（SelfHandled 改为比对静态字段）。
        /// 本包内置的适配器针对 Unity 官方发布的包，仍按 package.json 版本比对。
        /// </summary>
        public virtual string[] VerifiedVersions => Array.Empty<string>();

        public abstract void Configure(ReloadAdapterBuilder builder);
    }

    /// <summary>
    /// 适配器的声明入口。类型名使用 "Namespace.Type, AssemblyName"（嵌套类型用 +，泛型定义用 `1 等）。
    /// 找不到类型或成员时报告“适配器找不到目标”，提示目标已升级或改名。
    /// </summary>
    public sealed class ReloadAdapterBuilder
    {
        readonly ResetPlan m_Plan;
        readonly AdapterStatus m_Status;

        internal ReloadAdapterBuilder(ResetPlan plan, AdapterStatus status)
        {
            m_Plan = plan;
            m_Status = status;
        }

        string Source => m_Status.Name;

        /// <summary>
        /// 目标包经复核自身已处理关闭 Domain Reload 的情况，其程序集不再纳入检查，列入报告的“未检查范围”。必须给出依据。
        /// 只能用于 Unity 官方提供的包（随 Editor 提供或来自 Unity 官方注册表；官方包改动频率低，复核后信任，大版本更新时再复核）；
        /// 其他包和 DLL 用 ResetField、ResetType 等声明处理方式。
        /// </summary>
        public void SelfHandled(string evidence)
        {
            if (!RequireText(evidence, nameof(SelfHandled))) return;
            if (m_Status.Kind != AdapterTargetKind.Package || !m_Status.FromUnityRegistry)
            {
                Invalid($"SelfHandled 只能用于 Unity 官方提供的包（随 Editor 提供或来自 Unity 官方注册表），{m_Status.Target}（{m_Status.InstalledVersion}）不是。" +
                        "这个目标的静态状态照常检查；暂时无法修改源码的地方用 ResetField、ResetType、ResetGenericField 或 InvokeOnReset 处理");
                return;
            }
            if (m_Status.BuiltIn && !m_Status.Verified) return; // 版本问题已由计划报告，未复核的版本继续逐字段检查。

            m_Status.SelfHandledEvidence = evidence;
            m_Plan.MarkSelfHandled(m_Status.Target, m_Status);
        }

        /// <summary>进入 Play 时重置目标的静态字段；fieldName 也可写自动属性名。重置方式必须与字段初值匹配（检查器核对）。</summary>
        public void ResetField(string typeName, string fieldName, ResetMode mode = ResetMode.Default)
        {
            var type = Resolve(typeName);
            if (type == null) return;
            var field = type.GetField(fieldName, ReflectionUtil.StaticMembers) ??
                        type.GetField($"<{fieldName}>k__BackingField", ReflectionUtil.StaticMembers);
            if (field == null)
            {
                Broken($"类型 {typeName} 中找不到静态字段 {fieldName}");
                return;
            }

            m_Plan.AddField(field, mode, Source);
        }

        /// <summary>
        /// 进入 Play 时重置泛型定义上的静态字段（如 "EasyECS.ComponentID`1, EasyECS" 的 ID），展开到每个闭合类型。
        /// 不给 typeArguments 时，按泛型参数的接口或基类约束枚举全部满足约束的类型；只支持一个泛型参数。
        /// </summary>
        public void ResetGenericField(string openTypeName, string fieldName, ResetMode mode = ResetMode.Default, Func<IEnumerable<Type>> typeArguments = null)
        {
            var type = Resolve(openTypeName);
            if (type == null) return;
            if (!type.IsGenericTypeDefinition)
            {
                Broken($"{openTypeName} 不是泛型定义，请用 ResetField");
                return;
            }

            var field = type.GetField(fieldName, ReflectionUtil.StaticMembers) ??
                        type.GetField($"<{fieldName}>k__BackingField", ReflectionUtil.StaticMembers);
            if (field == null)
            {
                Broken($"类型 {openTypeName} 中找不到静态字段 {fieldName}");
                return;
            }

            if (typeArguments == null && type.GetGenericArguments().Length == 1 &&
                type.GetGenericArguments()[0].GetGenericParameterConstraints().All(c => c.ContainsGenericParameters))
            {
                Invalid($"{openTypeName} 的泛型参数没有可枚举的接口或基类约束，请通过 typeArguments 给出类型实参");
                return;
            }

            var arguments = typeArguments == null ? null : typeArguments();
            if (typeArguments != null && arguments == null)
            {
                Invalid("ResetGenericField 的 typeArguments 必须返回类型实参列表，不能返回 null");
                return;
            }
            m_Plan.AddGenericField(field, mode, Source, arguments);
        }

        /// <summary>
        /// 进入 Play 时由 action 恢复该类型的全部静态字段（适配器可直接引用目标程序集，或用反射）。检查器把整个类型视为已处理；
        /// 动作是否恢复了每个字段静态检查看不到，由停止后检查与连续 Play → Stop 验收确认。
        /// </summary>
        public void ResetType(string typeName, Action action, int order = 0)
        {
            var type = Resolve(typeName);
            if (type == null) return;
            if (type.ContainsGenericParameters)
            {
                Invalid("ResetType 必须指定闭合类型；泛型定义上的字段请用 ResetGenericField");
                return;
            }
            m_Plan.AddResetType(type, Source, action, order);
        }

        /// <summary>进入 Play 时调用目标的静态无参方法（如其自带的 Reset）。检查器核对它是否处理了类型中的全部可变静态字段。</summary>
        public void InvokeOnReset(string typeName, string methodName, int order = 0) =>
            AddMethod(typeName, methodName, order, true);

        /// <summary>退出 Play 时调用目标的静态无参方法。</summary>
        public void InvokeOnExitPlayMode(string typeName, string methodName, int order = 0) =>
            AddMethod(typeName, methodName, order, false);

        /// <summary>进入 Play 时执行任意重置代码。不让任何字段视为已处理；负责整个类型时用 <see cref="ResetType"/>。</summary>
        public void OnReset(string name, Action action, int order = 0) =>
            m_Plan.AddAction(Source, name, action, order, true);

        /// <summary>退出 Play 时执行任意释放代码。</summary>
        public void OnExitPlayMode(string name, Action action, int order = 0) =>
            m_Plan.AddAction(Source, name, action, order, false);

        /// <summary>
        /// 登记目标中已知不符合规范、暂缓处理的静态成员，报告为“第三方代码的已知问题”（不符合规范，可列为暂缓）。
        /// 常与 <see cref="SelfHandled"/> 搭配：官方包其余状态复核后信任，这些例外仍需使用方知晓或处理。
        /// </summary>
        public void KnownIssue(string typeName, string memberName, string issue)
        {
            if (!RequireText(issue, nameof(KnownIssue))) return;
            var type = Resolve(typeName);
            if (type == null) return;
            var members = type.GetMember(memberName, ReflectionUtil.StaticMembers);
            if (members.Length == 0)
            {
                Broken($"类型 {typeName} 中找不到静态成员 {memberName}");
                return;
            }

            m_Plan.Findings.Add(new Finding(RuleIds.AdapterKnownIssue, Severity.Violation, ReflectionUtil.Subject(members[0]),
                $"[{Source}] {issue}", baselinable: true));
        }

        /// <summary>
        /// 目标的静态状态无法安全重置时的兜底：关闭 Domain Reload 时，每次进入 Play 前强制重载域（即采用参照物本身，视为符合规范）。
        /// </summary>
        public void RequireDomainReload(string reason)
        {
            if (RequireText(reason, nameof(RequireDomainReload))) m_Plan.RequireReload(Source, reason, m_Status.Target);
        }

        void AddMethod(string typeName, string methodName, int order, bool onEnter)
        {
            var type = Resolve(typeName);
            if (type == null) return;
            var method = type.GetMethod(methodName, ReflectionUtil.StaticMembers, null, Type.EmptyTypes, null);
            if (method == null)
            {
                Broken($"类型 {typeName} 中找不到静态无参方法 {methodName}");
                return;
            }

            m_Plan.AddMethod(method, order, onEnter, Source);
        }

        Type Resolve(string typeName)
        {
            var type = ReflectionUtil.ResolveType(typeName);
            if (type == null) Broken($"找不到类型 {typeName}（格式应为 \"Namespace.Type, AssemblyName\"）");
            return type;
        }

        bool RequireText(string text, string api)
        {
            if (!string.IsNullOrWhiteSpace(text)) return true;
            Invalid($"{api} 必须给出非空的依据或原因");
            return false;
        }

        void Broken(string message) =>
            m_Plan.Findings.Add(new Finding(RuleIds.AdapterBroken, Severity.Violation, Source,
                message + "。目标可能已升级或改名，请复核适配器"));

        void Invalid(string message) =>
            m_Plan.Findings.Add(new Finding(RuleIds.AdapterInvalid, Severity.Violation, Source, message));
    }

    /// <summary>已安装的包。Git 来源时 Revision 为 manifest 中 # 后的 tag、分支或提交号，Hash 为实际解析的提交号。</summary>
    internal readonly struct InstalledPackage
    {
        public InstalledPackage(string version, string source, string revision, string hash, bool fromUnityRegistry = false)
        {
            Version = version;
            Source = source;
            Revision = string.IsNullOrEmpty(revision) ? null : revision;
            Hash = string.IsNullOrEmpty(hash) ? null : hash;
            FromUnityRegistry = fromUnityRegistry;
        }

        public string Version { get; }
        public string Source { get; }
        public string Revision { get; }
        public string Hash { get; }
        public bool IsGit => Hash != null || Revision != null;

        /// <summary>由 Unity 官方提供：随 Editor 提供（BuiltIn），或来自 Package Manager 的默认注册表。嵌入、本地、Git 与其他注册表的包都不是。</summary>
        public bool FromUnityRegistry { get; }

        /// <summary>报告中显示的版本。</summary>
        public string Describe()
        {
            if (IsGit)
                return $"Git {Revision ?? "（未指定）"}（提交 {Short(Hash)}，package.json {Version}）";
            return Source == "Embedded" ? $"{Version}（嵌入）" : Version;
        }

        static string Short(string hash) => hash == null ? "未知" : hash.Length > 8 ? hash.Substring(0, 8) : hash;
    }

    internal sealed class AdapterStatus
    {
        public AdapterStatus(string name, string target, AdapterTargetKind kind, bool builtIn)
        {
            Name = name;
            Target = target;
            Kind = kind;
            BuiltIn = builtIn;
        }

        /// <summary>本包内置的适配器：按版本号复核；项目适配器按静态字段比对。</summary>
        public bool BuiltIn { get; }

        public string Name { get; }
        public string Target { get; }
        public AdapterTargetKind Kind { get; }
        public string InstalledVersion { get; set; }
        public bool Verified { get; set; }
        public string SelfHandledEvidence { get; set; }

        /// <summary>目标包由 Unity 官方提供；只有这类目标可以用 SelfHandled。</summary>
        public bool FromUnityRegistry { get; set; }

        public bool Active => InstalledVersion != null;
    }
}
