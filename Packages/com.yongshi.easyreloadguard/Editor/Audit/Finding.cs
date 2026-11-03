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
        public const string SafeDrift = "RG015";
        public const string AdapterBroken = "RG020";
        public const string AdapterUnverified = "RG021";
        public const string AdapterRequiresReload = "RG022";
        public const string AdapterKnownIssue = "RG023";
        public const string StaleBaseline = "RG030";
        public const string ScanFailed = "RG040";

        /// <summary>报告、Console 与设置页显示的问题类型。规则编号只作为遗留问题清单中每一行的标识，不出现在给人看的文字里。</summary>
        public static string Title(string ruleId)
        {
            switch (ruleId)
            {
                case StaticDelegate: return "静态事件或委托未处理";
                case StaticUnityObject: return "静态 Unity 对象未处理";
                case MutableStatic: return "静态字段的值会带到下一次 Play";
                case ReadonlyState: return "只读集合、数组或对象的内容会带到下一次 Play";
                case AsyncVoid: return "async void 方法";
                case AsyncResource: return "静态字段持有异步任务、线程或计时器";
                case InvalidDeclaration: return "特性用法错误";
                case MissingReason: return "[ReloadSafe] 没写原因";
                case GenericExpansion: return "泛型静态字段";
                case DeclarationRisk: return "已加的重置会破坏下一次 Play";
                case OwnerCoverage: return "重置方法漏掉了字段";
                case SafeDrift: return "声明保留的字段仍在 Play 中被修改";
                case AdapterBroken: return "适配器找不到目标";
                case AdapterUnverified: return "适配器目标的版本未复核";
                case AdapterRequiresReload: return "适配器要求进入 Play 前重新加载脚本";
                case AdapterKnownIssue: return "第三方代码的已知问题";
                case StaleBaseline: return "遗留问题清单中的过期行";
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

        /// <summary>只有遗留静态状态类问题可记为已知遗留；特性用错与适配器损坏必须修复。</summary>
        public bool Baselinable { get; }

        public bool Baselined { get; set; }

        /// <summary>IL 分析得到的读写位置（初值、赋值与修改位置），帮助判断处理方式；可为 null。</summary>
        public string Context { get; set; }

        /// <summary>问题对应的成员（静态字段等），供退出 Play 后的残留探测读取；可为 null。</summary>
        public MemberInfo Member { get; set; }

        /// <summary>附加在遗留清单行末尾的证据（如声明保留的字段的写入位置）：证据变化时该行失效，需要重新确认；可为 null。</summary>
        public string KeyDetail { get; set; }

        public string Key => KeyDetail == null ? RuleId + "|" + Subject : RuleId + "|" + Subject + "|" + KeyDetail;

        public bool IsBlocking => Severity == Severity.Error && !Baselined;

        public override string ToString() => $"[{Title}] {DisplaySubject}{(Baselined ? "（已知遗留）" : "")} — {Message}";
    }
}
