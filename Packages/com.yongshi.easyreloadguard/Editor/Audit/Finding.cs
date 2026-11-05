using System.Reflection;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 检查结论只有两级：不符合规范（按规范判定，不按出错的可能性分级），以及不计入结论的建议。
    /// </summary>
    internal enum Severity
    {
        /// <summary>建议：不影响“是否符合规范”，不阻止进入 Play。</summary>
        Suggestion,

        /// <summary>不符合规范。不在暂缓清单中时阻止进入 Play（按设置）。</summary>
        Violation,
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
        public const string SafeDrift = "RG015";
        public const string EnterPlayModeHook = "RG016";
        public const string InitialValueTiming = "RG017";
        public const string AdapterBroken = "RG020";
        public const string AdapterUnverified = "RG021";
        public const string AdapterRequiresReload = "RG022";
        public const string AdapterKnownIssue = "RG023";
        public const string AdapterReviewChanged = "RG024";
        public const string AdapterInvalid = "RG025";
        public const string StaleBaseline = "RG030";
        public const string ScanFailed = "RG040";

        /// <summary>报告、Console 与设置页显示的问题类型。规则编号只作为暂缓清单中每一行的标识，不出现在给人看的文字里。</summary>
        public static string Title(string ruleId)
        {
            switch (ruleId)
            {
                case StaticDelegate: return "静态事件或委托未处理";
                case StaticUnityObject: return "静态 Unity 对象未处理";
                case MutableStatic: return "静态字段没有处理方式";
                case ReadonlyState: return "只读集合、数组或对象的内容没有处理方式";
                case AsyncVoid: return "async void 方法";
                case AsyncResource: return "静态字段持有异步任务、线程或计时器";
                case InvalidDeclaration: return "特性用法错误";
                case MissingReason: return "[ReloadSafe] 没写原因";
                case GenericExpansion: return "泛型静态字段";
                case DeclarationRisk: return "重置方式与字段初值不匹配";
                case OwnerCoverage: return "重置方法漏掉了字段";
                case SafeDrift: return "[ReloadSafe] 的字段不能证明不变";
                case EnterPlayModeHook: return "[InitializeOnEnterPlayMode] 方法没有标 [ResetMethod]";
                case InitialValueTiming: return "初值可能不干净";
                case AdapterBroken: return "适配器找不到目标";
                case AdapterUnverified: return "适配器目标的版本未复核";
                case AdapterRequiresReload: return "适配器要求进入 Play 前重新加载脚本";
                case AdapterKnownIssue: return "第三方代码的已知问题";
                case AdapterReviewChanged: return "信任的官方包中静态字段有变化";
                case AdapterInvalid: return "适配器用法错误";
                case StaleBaseline: return "暂缓清单中的过期行";
                case ScanFailed: return "无法分析的类型";
                default: return ruleId;
            }
        }
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

        public string Title => RuleIds.Title(RuleId);

        /// <summary>
        /// 给人看的成员名："程序集|命名空间.类型.成员" 显示为 "命名空间.类型.成员（程序集）"。
        /// 过期清单行保持原样，它就是需要删除的那一行。
        /// </summary>
        public string DisplaySubject
        {
            get
            {
                var bar = Subject.IndexOf('|');
                if (bar <= 0 || RuleId == RuleIds.StaleBaseline) return Subject;
                return $"{Subject.Substring(bar + 1)}（{Subject.Substring(0, bar)}）";
            }
        }

        /// <summary>只有“没有处理方式”一类问题可以列为暂缓；特性用错、适配器损坏、[ReloadSafe] 证明不了必须修复。</summary>
        public bool Baselinable { get; }

        /// <summary>列在暂缓清单中。暂缓的不符合项不阻止进入 Play，但仍然不符合规范。</summary>
        public bool Baselined { get; set; }

        /// <summary>IL 分析得到的读写位置（初值、赋值与修改位置），帮助判断处理方式；可为 null。</summary>
        public string Context { get; set; }

        /// <summary>问题对应的成员（静态字段等），供退出 Play 后的残留探测读取；可为 null。</summary>
        public MemberInfo Member { get; set; }

        /// <summary>附加在暂缓清单行末尾的证据（如官方包的静态字段签名）：证据变化时该行失效，需要重新确认；可为 null。</summary>
        public string KeyDetail { get; set; }

        public string Key => KeyDetail == null ? RuleId + "|" + Subject : RuleId + "|" + Subject + "|" + KeyDetail;

        public bool IsViolation => Severity == Severity.Violation;

        public bool IsBlocking => IsViolation && !Baselined;

        public override string ToString() => $"[{Title}] {DisplaySubject}{(Baselined ? "（暂缓）" : "")} — {Message}";
    }
}
