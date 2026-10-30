using System.Reflection;

namespace EasyReloadGuard.Editor
{
    internal enum Severity
    {
        Info,
        Warning,
        Error,
    }

    internal static class RuleIds
    {
        public const string StaticDelegate = "RG001";
        public const string StaticUnityObject = "RG002";
        public const string MutableStatic = "RG003";
        public const string ReadonlyState = "RG004";
        public const string AsyncVoid = "RG005";
        public const string AsyncResource = "RG006";
        public const string InvalidDeclaration = "RG010";
        public const string MissingReason = "RG011";
        public const string GenericExpansion = "RG012";
        public const string DeclarationRisk = "RG013";
        public const string OwnerCoverage = "RG014";
        public const string AdapterBroken = "RG020";
        public const string AdapterUnverified = "RG021";
        public const string AdapterRequiresReload = "RG022";
        public const string AdapterKnownIssue = "RG023";
        public const string StaleBaseline = "RG030";
        public const string ScanFailed = "RG040";
    }

    internal sealed class Finding
    {
        public Finding(string ruleId, Severity severity, string subject, string message, bool baselinable = false)
        {
            RuleId = ruleId;
            Severity = severity;
            Subject = subject;
            Message = message;
            Baselinable = baselinable;
        }

        public string RuleId { get; }
        public Severity Severity { get; }
        public string Subject { get; }
        public string Message { get; }

        /// <summary>只有遗留静态状态类问题可写入基线；声明错误与适配器损坏必须修复。</summary>
        public bool Baselinable { get; }

        public bool Baselined { get; set; }

        /// <summary>IL 分析得到的上下文（初值、赋值与修改位置），帮助判断处理方式；可为 null。</summary>
        public string Context { get; set; }

        /// <summary>问题对应的成员（静态字段等），供退出 Play 后的残留探测读取；可为 null。</summary>
        public MemberInfo Member { get; set; }

        public string Key => RuleId + "|" + Subject;

        public bool IsBlocking => Severity == Severity.Error && !Baselined;

        public override string ToString() => $"[{RuleId}] {Subject}{(Baselined ? "（已基线）" : "")} — {Message}";
    }
}
