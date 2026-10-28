using System;

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
    /// 为无法修改源码的 UPM 包或 DLL 声明 Domain Reload 处理方式。放在任意 Editor 程序集中，
    /// 需要公开无参构造；目标不存在时休眠，版本不在 <see cref="VerifiedVersions"/> 中时报告警告。
    /// </summary>
    public abstract class ReloadAdapter
    {
        public abstract string Target { get; }

        public virtual AdapterTargetKind TargetKind => AdapterTargetKind.Package;

        /// <summary>已人工复核过的目标版本。升级目标后必须复核并追加，检查器以此提示迁移。</summary>
        public abstract string[] VerifiedVersions { get; }

        public abstract void Configure(ReloadAdapterBuilder builder);
    }

    /// <summary>
    /// 适配器的声明入口。类型名使用 "Namespace.Type, AssemblyName"（嵌套类型用 +）。
    /// 找不到类型或成员时报告 RG020 错误，提示目标已升级或改名。
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

        /// <summary>目标自身已处理关闭 Domain Reload 的情况；其程序集不再纳入检查。必须给出依据。</summary>
        public void SelfHandled(string evidence)
        {
            if (!RequireText(evidence, nameof(SelfHandled))) return;
            m_Status.SelfHandledEvidence = evidence;
            m_Plan.MarkSelfHandled(m_Status.Target);
        }

        /// <summary>进入 Play 时重置目标的静态字段；fieldName 也可写自动属性名。</summary>
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

        /// <summary>进入 Play 时调用目标的静态无参方法（如其自带的 Reset/Shutdown）。该类型视为已处理。</summary>
        public void InvokeOnReset(string typeName, string methodName, int order = 0) =>
            AddMethod(typeName, methodName, order, true);

        /// <summary>退出 Play 时调用目标的静态无参方法。</summary>
        public void InvokeOnExitPlayMode(string typeName, string methodName, int order = 0) =>
            AddMethod(typeName, methodName, order, false);

        /// <summary>进入 Play 时执行任意重置代码（适配器可直接引用目标程序集时使用）。</summary>
        public void OnReset(string name, Action action, int order = 0) =>
            m_Plan.AddAction(Source, name, action, order, true);

        /// <summary>退出 Play 时执行任意释放代码。</summary>
        public void OnExitPlayMode(string name, Action action, int order = 0) =>
            m_Plan.AddAction(Source, name, action, order, false);

        /// <summary>声明该类型的静态状态已复核为安全或已由其他方式处理，检查器不再报告。</summary>
        public void HandlesType(string typeName, string evidence)
        {
            if (!RequireText(evidence, nameof(HandlesType))) return;
            var type = Resolve(typeName);
            if (type != null) m_Plan.MarkOwnerManaged(type);
        }

        /// <summary>
        /// 登记目标中经复核会影响关闭 Domain Reload 后运行逻辑的静态成员，报告为 RG023 警告（可写入基线）。
        /// 与 <see cref="SelfHandled"/> 搭配：整包其余状态已复核为不影响运行逻辑，这些例外仍需使用方知晓或处理。
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

            m_Plan.Findings.Add(new Finding(RuleIds.AdapterKnownIssue, Severity.Warning, ReflectionUtil.Subject(members[0]),
                $"[{Source}] {issue}", baselinable: true));
        }

        /// <summary>
        /// 目标的静态状态无法安全重置时的兜底：关闭 Domain Reload 时，每次进入 Play 前强制重载域。
        /// </summary>
        public void RequireDomainReload(string reason)
        {
            if (RequireText(reason, nameof(RequireDomainReload))) m_Plan.RequireReload(Source, reason);
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
            Broken($"{api} 必须给出非空的依据或原因");
            return false;
        }

        void Broken(string message) =>
            m_Plan.Findings.Add(new Finding(RuleIds.AdapterBroken, Severity.Error, Source,
                message + "。目标可能已升级或改名，请复核适配器"));
    }

    internal sealed class AdapterStatus
    {
        public AdapterStatus(string name, string target, AdapterTargetKind kind)
        {
            Name = name;
            Target = target;
            Kind = kind;
        }

        public string Name { get; }
        public string Target { get; }
        public AdapterTargetKind Kind { get; }
        public string InstalledVersion { get; set; }
        public bool Verified { get; set; }
        public string SelfHandledEvidence { get; set; }
        public bool Active => InstalledVersion != null;
    }
}
