using System;

namespace EasyReloadGuard
{
    /// <summary>静态字段的重置方式。</summary>
    public enum ResetMode
    {
        /// <summary>写回 default(T)：引用置 null，值类型归零。不可用于 readonly 字段。</summary>
        Default,

        /// <summary>写回本次脚本加载后记录的初值。纯初始化类型在首次 ExitingEditMode 提前记录，其余在首次重置时记录并提示局限。仅限值类型与 string。</summary>
        InitialValue,

        /// <summary>对字段当前值调用无参 Clear()，保留引用本身。适合 readonly 集合。</summary>
        Clear,
    }

    /// <summary>
    /// 进入 Play 时重置该静态字段、自动属性或事件。
    /// 三者均可直接标注或使用 field: 目标，效果相同。
    /// 仅在 Editor 关闭 Domain Reload 时执行；Player 每次启动都是新域，不产生运行时开销。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Event, AllowMultiple = false, Inherited = false)]
    public sealed class ResetStaticAttribute : Attribute
    {
        public ResetStaticAttribute(ResetMode mode = ResetMode.Default)
        {
            Mode = mode;
        }

        public ResetMode Mode { get; }
    }

    /// <summary>
    /// 进入 Play 时调用的静态无参 void 方法，在全部字段重置之后按 Order 升序执行。方法须幂等且同步完成。
    /// 声明该方法的类型视为自行管理其全部静态状态；检查器核对重置方法（含其调用的类型内方法）
    /// 是否处理了每个不能证明不变的字段，漏掉时报告“重置方法漏掉了字段”；不能靠 [ReloadSafe] 豁免可变状态。
    /// 业务没有现成的重置入口时，可在该类型中新增只在 Editor 编译的方法：方法连同特性整体放在 #if UNITY_EDITOR 内，
    /// 只把本类型的静态状态恢复为新域首次进入 Play 时的样子，不调用业务流程，也不被业务代码调用。Player 中不存在该方法。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ResetMethodAttribute : Attribute
    {
        public ResetMethodAttribute(int order = 0)
        {
            Order = order;
        }

        public int Order { get; }
    }

    /// <summary>
    /// 退出 Play（ExitingPlayMode）时调用的静态无参 void 方法，用于解除订阅、停止线程/计时器、释放外部资源。
    /// 按 Order 升序执行；无论 Domain Reload 是否关闭都会执行。只标注业务已有、语义适用的停止/释放方法，不要为此新建方法：
    /// 订阅、线程、计时器与外部资源的释放在 Player 中切场景、登出时同样需要，属于业务生命周期，缺失时修业务本身。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class OnExitPlayModeAttribute : Attribute
    {
        public OnExitPlayModeAttribute(int order = 0)
        {
            Order = order;
        }

        public int Order { get; }
    }

    /// <summary>
    /// 为检查器能证明初始化后不变的字段说明原因。不能豁免可变状态，纯计算缓存同样需要重置。
    /// 类型与程序集上的声明也逐字段证明；原因必须非空。证明不了时属于不符合规范，不能暂缓。
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field,
        AllowMultiple = false,
        Inherited = false
    )]
    public sealed class ReloadSafeAttribute : Attribute
    {
        public ReloadSafeAttribute(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }
}
