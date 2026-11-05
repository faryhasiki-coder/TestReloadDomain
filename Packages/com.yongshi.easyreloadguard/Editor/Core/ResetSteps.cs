using System;
using System.Reflection;

namespace EasyReloadGuard.Editor
{
    internal abstract class ResetStep
    {
        protected ResetStep(string name, int order)
        {
            Name = name;
            Order = order;
        }

        public string Name { get; }
        public int Order { get; }

        public abstract void Execute();

        public static int Compare(ResetStep a, ResetStep b)
        {
            var byOrder = a.Order.CompareTo(b.Order);
            return byOrder != 0 ? byOrder : string.CompareOrdinal(a.Name, b.Name);
        }
    }

    internal sealed class ActionStep : ResetStep
    {
        readonly Action m_Action;

        public ActionStep(string name, int order, Action action) : base(name, order)
        {
            m_Action = action;
        }

        public override void Execute() => m_Action();
    }

    internal sealed class FieldResetStep : ResetStep
    {
        readonly MethodInfo m_Clear;
        bool m_Observed;
        object m_Initial;

        public FieldResetStep(FieldInfo field, ResetMode mode, MethodInfo clear, string name) : base(name, 0)
        {
            Field = field;
            Mode = mode;
            m_Clear = clear;
        }

        public FieldInfo Field { get; }
        public ResetMode Mode { get; }

        /// <summary>InitialValue 尚未记录基准值；在已运行过 Play 的域中首次记录会拿到脏值。</summary>
        public bool NeedsCleanBaseline => Mode == ResetMode.InitialValue && !m_Observed;

        /// <summary>已经观察过初值（提前在 ExitingEditMode，或第一次重置时）。</summary>
        public bool Observed => m_Observed;

        public override void Execute()
        {
            if (!m_Observed) Observe();

            switch (Mode)
            {
                case ResetMode.Default:
                    Field.SetValue(null, DefaultOf(Field.FieldType));
                    break;
                case ResetMode.InitialValue:
                    Field.SetValue(null, m_Initial);
                    break;
                case ResetMode.Clear:
                    var value = Field.GetValue(null);
                    if (value != null) m_Clear.Invoke(value, null);
                    break;
            }
        }

        /// <summary>
        /// 观察初值（提前在脚本加载后第一次 Play 的 ExitingEditMode，或首次执行时）：InitialValue 记为基准值；
        /// Default 模式下初值非 default 往往意味着选错了模式。只有 InitialValue 长期持有初值，Default/Clear 不保留字段曾引用的对象。
        /// </summary>
        internal void Observe()
        {
            if (Mode == ResetMode.Clear)
            {
                m_Observed = true;
                return;
            }

            var value = Field.GetValue(null);
            m_Observed = true;
            if (Mode == ResetMode.InitialValue)
            {
                m_Initial = value;
            }
            else if (!Equals(value, DefaultOf(Field.FieldType)))
            {
                Log.Warning($"{Name} 首次进入 Play 时的值为 {value}，ResetMode.Default 会把它写回 default。" +
                            "若这是字段初值：值类型/string 改用 ResetMode.InitialValue，集合改用 ResetMode.Clear，其余改用 [ResetMethod]。");
            }
        }

        static object DefaultOf(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
