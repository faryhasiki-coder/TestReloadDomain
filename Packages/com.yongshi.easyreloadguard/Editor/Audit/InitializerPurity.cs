using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 只读 IL 判断类型的静态构造是否“纯初始化”：只创建 BCL 集合/字符串等对象、写入本类型字段、调用本类型内同样纯的方法。
    /// 纯初始化在编辑模式下被读取字段触发时没有外部副作用，残留探测可以安全读取这类类型。
    /// 判断从严：调用 BCL 白名单以外的方法（UnityEngine、业务代码、IO、线程等）、读写其他类型的静态字段（会触发对方的静态构造）、
    /// calli 或无法解析的指令，一律视为不纯。没有静态构造的类型视为纯。
    /// </summary>
    internal static class InitializerPurity
    {
        const int k_MaxDepth = 8;

        static readonly Dictionary<Type, bool> s_Cache = new Dictionary<Type, bool>();

        /// <summary>只构造数据、不触达外部状态的 BCL 命名空间。</summary>
        static readonly HashSet<string> k_PureNamespaces = new HashSet<string>(StringComparer.Ordinal)
        {
            "System.Collections", "System.Collections.Generic", "System.Collections.Concurrent", "System.Collections.ObjectModel",
            "System.Collections.Specialized", "System.Text", "System.Runtime.CompilerServices",
        };

        static readonly HashSet<Type> k_PureTypes = new HashSet<Type>
        {
            typeof(object), typeof(string), typeof(Type), typeof(Math), typeof(StringComparer), typeof(Array), typeof(Enum),
            typeof(Nullable<>), typeof(TimeSpan), typeof(Guid), typeof(Tuple), typeof(ValueTuple),
            typeof(bool), typeof(char), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
            typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
        };

        public static bool IsPure(Type type)
        {
            if (type.ContainsGenericParameters) return false;
            if (!s_Cache.TryGetValue(type, out var pure))
            {
                try
                {
                    var initializer = type.TypeInitializer;
                    pure = initializer == null || IsPureMethod(type, initializer, new HashSet<MethodBase>(), 0);
                }
                catch (Exception)
                {
                    pure = false;
                }

                s_Cache[type] = pure;
            }

            return pure;
        }

        static bool IsPureMethod(Type owner, MethodBase method, HashSet<MethodBase> visiting, int depth)
        {
            if (depth > k_MaxDepth) return false;
            if (!visiting.Add(method)) return true; // 递归调用：由外层判定
            var il = FieldAccessAnalyzer.ReadIL(method);
            if (il == null) return false;

            var module = method.Module;
            var typeArgs = method.DeclaringType != null && method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
            for (var pos = 0; pos < il.Length;)
            {
                if (!FieldAccessAnalyzer.TryDecode(il, ref pos, out var op, out var operandStart)) return false;
                var token = op.OperandType == OperandType.InlineNone ? 0 : BitConverter.ToInt32(il, operandStart);

                if (op == OpCodes.Calli) return false;
                if (op == OpCodes.Ldsfld || op == OpCodes.Ldsflda || op == OpCodes.Stsfld)
                {
                    var field = Resolve(() => module.ResolveField(token, typeArgs, methodArgs));
                    if (field == null || !IsOwnOrGenerated(field.DeclaringType, owner)) return false;
                    if (field.DeclaringType != owner && !IsPureNested(owner, field.DeclaringType, visiting, depth)) return false;
                    continue;
                }

                if (op == OpCodes.Call || op == OpCodes.Callvirt || op == OpCodes.Newobj || op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn)
                {
                    var callee = Resolve(() => module.ResolveMethod(token, typeArgs, methodArgs));
                    if (callee == null) return false;
                    if (op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn) continue; // 只取函数指针（构造委托），不执行
                    if (!IsPureCall(owner, callee, visiting, depth)) return false;
                }
            }

            return true;
        }

        static bool IsPureCall(Type owner, MethodBase callee, HashSet<MethodBase> visiting, int depth)
        {
            var declaring = callee.DeclaringType;
            if (declaring == null) return false;
            if (typeof(Delegate).IsAssignableFrom(declaring)) return callee is ConstructorInfo; // 构造委托不执行目标
            if (IsPureBcl(declaring)) return true;
            // 值类型构造（如 new Vector3(...)）只要函数体同样纯即可，函数体中的调用按相同规则检查
            if (callee is ConstructorInfo && declaring.IsValueType) return IsPureMethod(owner, callee, visiting, depth + 1);
            if (!IsOwnOrGenerated(declaring, owner)) return false;
            if (declaring != owner && !IsPureNested(owner, declaring, visiting, depth)) return false;
            return IsPureMethod(owner, callee, visiting, depth + 1);
        }

        /// <summary>编译器生成的嵌套类型（lambda 缓存 &lt;&gt;c 等）首次访问会运行其静态构造，同样要求纯。</summary>
        static bool IsPureNested(Type owner, Type nested, HashSet<MethodBase> visiting, int depth)
        {
            var initializer = nested.TypeInitializer;
            return initializer == null || IsPureMethod(owner, initializer, visiting, depth + 1);
        }

        static bool IsOwnOrGenerated(Type type, Type owner)
        {
            if (type == owner) return true;
            for (var current = type; current != null; current = current.DeclaringType)
            {
                if (current.DeclaringType == owner)
                    return current.IsDefined(typeof(CompilerGeneratedAttribute), false) || current.Name.StartsWith("<", StringComparison.Ordinal);
            }

            return false;
        }

        static bool IsPureBcl(Type type)
        {
            var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (k_PureTypes.Contains(definition)) return true;
            if (type.Assembly != typeof(object).Assembly && !type.Assembly.GetName().Name.StartsWith("System", StringComparison.Ordinal))
                return false;
            return type.Namespace != null && k_PureNamespaces.Contains(type.Namespace);
        }

        static T Resolve<T>(Func<T> resolve) where T : class
        {
            try
            {
                return resolve();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
