using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;

namespace EasyReloadGuard.Editor
{
    /// <summary>声明合法性规则与类型分析。返回 null 表示合法，否则返回面向人的错误说明。</summary>
    internal static class ResetRules
    {
        public const string InstanceStateError = "[ResetStatic] 只能用于静态成员；实例状态由对象自身生命周期管理";

        public static string ValidateField(FieldInfo field, ResetMode mode, out MethodInfo clear)
        {
            clear = null;
            if (!field.IsStatic) return InstanceStateError;
            if (field.IsLiteral) return "const 字段不会改变，无需重置";
            if (field.IsDefined(typeof(ReloadSafeAttribute), false))
                return "同时标记了 [ResetStatic] 与 [ReloadSafe]，语义冲突";

            var type = field.FieldType;
            switch (mode)
            {
                case ResetMode.Default:
                    if (field.IsInitOnly)
                        return "readonly 字段不能写回 default；集合改用 ResetMode.Clear，其余改为非 readonly 或使用 [ResetMethod]";
                    break;
                case ResetMode.InitialValue:
                    if (field.IsInitOnly) return "readonly 字段的值不会改变，无需 InitialValue";
                    if (!type.IsValueType && type != typeof(string))
                        return $"InitialValue 仅支持值类型与 string；{type.Name} 是引用类型，只能保存同一个已被修改的对象。集合用 Clear，其余用 [ResetMethod]";
                    break;
                case ResetMode.Clear:
                    if (type.IsValueType) return "值类型的 Clear() 只作用于副本，不能用于 ResetMode.Clear";
                    if (!FieldAccessAnalyzer.IsCollection(type)) return "ResetMode.Clear 只支持集合；其他对象用 [ResetMethod] 恢复";
                    clear = FindClearMethod(type);
                    if (clear == null) return $"{type.Name} 没有公开的无参 Clear() 方法";
                    break;
                default:
                    return $"未知的 ResetMode {mode}";
            }

            return null;
        }

        public static string ValidateMethod(MethodInfo method)
        {
            if (!method.IsStatic) return "只能用于静态方法";
            if (method.GetParameters().Length != 0) return "方法必须无参数";
            if (method.ContainsGenericParameters) return "不能是泛型方法，也不能位于未闭合的泛型类型中；泛型状态请在 Editor 适配器中按闭合类型用 ResetField 或 OnReset 声明";
            if (method.ReturnType != typeof(void)) return "返回值必须为 void";
            if (method.IsDefined(typeof(AsyncStateMachineAttribute), false)) return "不能是 async 方法：重置与释放必须同步完成";
            return null;
        }

        public static MethodInfo FindClearMethod(Type type)
        {
            var clear = type.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (clear != null || !type.IsInterface) return clear;
            foreach (var inherited in type.GetInterfaces())
            {
                clear = inherited.GetMethod("Clear", Type.EmptyTypes);
                if (clear != null) return clear;
            }

            return null;
        }

        /// <summary>
        /// 在引用了泛型定义所在程序集的程序集中，查找基类链上出现的闭合构造（如 Singleton&lt;Foo&gt;）。
        /// 不经过派生类、直接使用的闭合类型无法静态发现。
        /// </summary>
        public static List<Type> FindClosedConstructions(Type genericDefinition)
        {
            var result = new List<Type>();
            var seen = new HashSet<Type>();
            var definitionAssembly = genericDefinition.Assembly;
            var definitionName = definitionAssembly.GetName().Name;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                if (assembly != definitionAssembly && !ReflectionUtil.References(assembly, definitionName)) continue;
                foreach (var type in ReflectionUtil.SafeGetTypes(assembly))
                {
                    for (var current = type.BaseType; current != null; current = current.BaseType)
                    {
                        if (current.IsGenericType && !current.ContainsGenericParameters &&
                            current.GetGenericTypeDefinition() == genericDefinition && seen.Add(current))
                            result.Add(current);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 按泛型约束枚举闭合类型：只支持一个泛型参数，使用不含泛型参数的接口或基类约束，找出全部满足约束的非抽象类型。
        /// 没有可用约束或参数多于一个时返回空列表。测试程序集中的类型不参与（泛型定义本身在测试程序集中时除外）。
        /// </summary>
        public static List<Type> CloseByConstraints(Type genericDefinition, out string invalid)
        {
            invalid = null;
            var result = new List<Type>();
            var parameters = genericDefinition.GetGenericArguments();
            if (parameters.Length != 1) return result;

            var parameter = parameters[0];
            var constraints = parameter.GetGenericParameterConstraints().Where(c => !c.ContainsGenericParameters).ToList();
            if (constraints.Count == 0) return result;

            var includeTests = ReflectionUtil.IsTestAssembly(genericDefinition.Assembly);
            var candidates = TypeCache.GetTypesDerivedFrom(constraints[0]).ToList();
            if (!constraints[0].IsInterface) candidates.Add(constraints[0]);
            foreach (var candidate in candidates.Distinct())
            {
                if (candidate.IsAbstract || candidate.IsInterface || candidate.ContainsGenericParameters) continue;
                if (!includeTests && ReflectionUtil.IsTestAssembly(candidate.Assembly)) continue;
                if (!constraints.All(c => c.IsAssignableFrom(candidate)) || !SatisfiesSpecialConstraints(parameter, candidate)) continue;
                var closed = TryClose(genericDefinition, candidate);
                if (closed != null) result.Add(closed);
            }

            return result;
        }

        /// <summary>按给出的类型实参构造闭合类型（适配器 ResetGenericField）。只支持一个泛型参数；不满足约束的实参写入 invalid。</summary>
        public static List<Type> Close(Type genericDefinition, IEnumerable<Type> typeArguments, out string invalid)
        {
            invalid = null;
            var result = new List<Type>();
            if (genericDefinition.GetGenericArguments().Length != 1)
            {
                invalid = $"ResetGenericField 只支持一个泛型参数的类型，{ReflectionUtil.TypeName(genericDefinition)} 请按闭合类型用 ResetField 声明";
                return result;
            }

            var rejected = new List<string>();
            foreach (var argument in typeArguments.Distinct())
            {
                if (argument == null || argument.ContainsGenericParameters || argument.IsByRef || argument.IsPointer || argument == typeof(void))
                {
                    rejected.Add(argument == null ? "null" : ReflectionUtil.TypeName(argument));
                    continue;
                }
                var closed = TryClose(genericDefinition, argument);
                if (closed != null) result.Add(closed);
                else rejected.Add(ReflectionUtil.TypeName(argument));
            }

            if (rejected.Count > 0)
                invalid = $"{ReflectionUtil.TypeName(genericDefinition)} 的类型实参不满足约束：{string.Join("、", rejected)}";
            return result;
        }

        static Type TryClose(Type genericDefinition, Type argument)
        {
            try
            {
                return genericDefinition.MakeGenericType(argument);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        static bool SatisfiesSpecialConstraints(Type parameter, Type candidate)
        {
            var attributes = parameter.GenericParameterAttributes;
            if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0 && !candidate.IsValueType) return false;
            if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0 && candidate.IsValueType) return false;
            if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 && !candidate.IsValueType &&
                candidate.GetConstructor(Type.EmptyTypes) == null)
                return false;
            return true;
        }

        /// <summary>
        /// Unity 在编辑模式对象退出之前调用的 [InitializeOnEnterPlayMode] 方法：早于本包的重置执行，写入的值会被本包的重置覆盖，
        /// 必须同时标 [ResetMethod]，由本包在字段重置之后再执行一次。
        /// </summary>
        public static bool IsEnterPlayModeHook(MethodInfo method) => method.IsDefined(typeof(InitializeOnEnterPlayModeAttribute), false);
    }
}
