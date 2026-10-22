using System;

namespace EasyReloadGuard
{
    /// <summary>静态字段的重置方式。</summary>
    public enum ResetMode
    {
        /// <summary>写回 default(T)：引用置 null，值类型归零。不可用于 readonly 字段。</summary>
        Default,

        /// <summary>写回本次域加载后首次进入 Play 时观察到的值（通常即字段初值）。仅限值类型与 string。</summary>
        InitialValue,

        /// <summary>对字段当前值调用无参 Clear()，保留引用本身。适合 readonly 集合。</summary>
        Clear,
    }

    /// <summary>
    /// 进入 Play 时重置该静态字段。事件与自动属性使用 field: 目标：
    /// <c>[field: ResetStatic] public static event Action Changed;</c>。
    /// 仅在 Editor 关闭 Domain Reload 时执行；Player 每次启动都是新域，不产生运行时开销。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class ResetStaticAttribute : Attribute
    {
        public ResetStaticAttribute(ResetMode mode = ResetMode.Default)
        {
            Mode = mode;
        }

        public ResetMode Mode { get; }
    }

    /// <summary>
    /// 进入 Play 时调用的静态无参 void 方法，在全部字段重置之后按 Order 升序执行。
    /// 声明该方法的类型视为自行管理其全部静态状态（owner 负责）。方法须幂等且同步完成。
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
    /// 在 <see cref="PlaySession.Token"/> 取消之后按 Order 升序执行；无论 Domain Reload 是否关闭都会执行。
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
    /// 声明静态状态有意跨 Play 会话保留（如纯计算缓存、编辑器偏好）。必须给出非空原因，检查器会校验。
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field,
        AllowMultiple = false, Inherited = false)]
    public sealed class ReloadSafeAttribute : Attribute
    {
        public ReloadSafeAttribute(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }
}
