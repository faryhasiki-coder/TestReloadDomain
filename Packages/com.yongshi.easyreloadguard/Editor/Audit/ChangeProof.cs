using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// “能证明不变”：字段的值和它引用的内容，在静态初始化之后都不会再改变。只认检查器能从代码中确认的情况，
    /// “逻辑上不会出错”“业务会自己重置”都不算；证明不了就是不符合规范，不退回“可能有问题”。
    /// 字段本身：const、readonly，或私有且静态初始化之外没有赋值。非私有、非 readonly 的字段在类型外可能被重新赋值，证明不了。
    /// 内容：不可变类型直接成立；集合与数组要求私有、类型内每次使用都在只读白名单中（<see cref="FieldUsage.UnsafeUses"/>），
    /// 且元素类型不可变；其他引用类型证明不了。
    /// </summary>
    internal static class ChangeProof
    {
        const int k_MaxDepth = 6;

        static readonly HashSet<Type> k_ImmutableTypes = new HashSet<Type>
        {
            typeof(string), typeof(decimal), typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(Guid),
        };

        static readonly Dictionary<Type, bool> s_Cache = new Dictionary<Type, bool>();

        /// <summary>能证明不变时返回 null，否则返回面向人的原因。usage 为 null 时（分析失败）证明不了。</summary>
        public static string WhyNotProvable(FieldInfo field, FieldUsage usage, string analysisError)
        {
            if (field.IsLiteral) return null;
            if (analysisError != null) return $"IL 分析失败（{analysisError}）";
            if (usage == null) return analysisError == null ? "没有读写位置分析结果" : $"IL 分析失败（{analysisError}）";

            if (!field.IsInitOnly)
            {
                if (!usage.Complete) return "字段不是私有的（或经非私有的访问器写入），类型外是否重新赋值无法确认";
                if (usage.Writers.Count > 0) return $"在 {FieldUsage.Names(usage.Writers)} 中赋值";
            }

            var type = field.FieldType;
            if (IsImmutableType(type)) return null;

            if (type.IsArray || FieldAccessAnalyzer.IsCollection(type))
            {
                if (!field.IsPrivate) return "集合字段不是私有的，类型外是否修改内容无法确认";
                if (!usage.Complete) return "集合字段不是私有的，类型外是否修改内容无法确认";
                if (usage.InitializerContentsUnknown) return "集合初值来自不能证明独占的来源，可能与外部共享可变内容";
                if (usage.UnsafeUses.Count > 0) return $"在 {FieldUsage.Names(usage.UnsafeUses)} 中修改内容或把集合传了出去";
                var mutable = ElementTypes(type).FirstOrDefault(t => !IsImmutableType(t));
                if (mutable != null) return $"元素类型 {AdapterReview.TypeLabel(mutable)} 不是不可变类型，元素的内容可能被修改";
                if (!ElementTypes(type).Any()) return $"无法确定 {AdapterReview.TypeLabel(type)} 的元素类型";
                return null;
            }

            return $"{AdapterReview.TypeLabel(type)} 不是不可变类型，引用的对象内容可能被修改";
        }

        /// <summary>
        /// 不可变类型：基础值类型、枚举、string 等；委托与 Type；字段全部不可变的结构体；
        /// 密封、所有实例字段 readonly 且类型不可变的类。Unity 对象一律不算。逐层检查，层数过深时按可变处理。
        /// </summary>
        public static bool IsImmutableType(Type type) => IsImmutable(type, 0, new HashSet<Type>());

        static bool IsImmutable(Type type, int depth, HashSet<Type> visiting)
        {
            if (type.IsPrimitive || type.IsEnum || k_ImmutableTypes.Contains(type)) return true;
            if (typeof(Delegate).IsAssignableFrom(type) || typeof(Type).IsAssignableFrom(type)) return true;
            if (typeof(UnityEngine.Object).IsAssignableFrom(type)) return false;
            if (type.IsPointer || type.IsByRef || type.IsArray || type.ContainsGenericParameters) return false;

            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null) return IsImmutable(underlying, depth + 1, visiting);
            if (depth > k_MaxDepth) return false;
            if (s_Cache.TryGetValue(type, out var cached)) return cached;
            if (!visiting.Add(type)) return true; // 自引用：由外层判定

            var result = IsImmutableComposite(type, depth, visiting);
            visiting.Remove(type);
            if (depth == 0 || !result) s_Cache[type] = result;
            return result;
        }

        static bool IsImmutableComposite(Type type, int depth, HashSet<Type> visiting)
        {
            if (!type.IsValueType && !type.IsSealed) return false; // 派生类可能加入可变字段
            for (var current = type; current != null && current != typeof(object) && current != typeof(ValueType); current = current.BaseType)
            {
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    // 结构体按值复制，字段本身不必 readonly；类的字段必须 readonly
                    if (!type.IsValueType && !field.IsInitOnly) return false;
                    if (!IsImmutable(field.FieldType, depth + 1, visiting)) return false;
                }
            }

            return true;
        }

        /// <summary>数组的元素类型；集合按 IEnumerable&lt;T&gt; 取 T（字典为 KeyValuePair&lt;K,V&gt;）；非泛型集合返回 object。</summary>
        internal static IEnumerable<Type> ElementTypes(Type type)
        {
            if (type.IsArray) return new[] { type.GetElementType() };
            var enumerable = (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                    ? new[] { type }
                    : type.GetInterfaces())
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                .Select(i => i.GetGenericArguments()[0])
                .Distinct()
                .ToList();
            return enumerable.Count > 0 ? enumerable : new List<Type> { typeof(object) };
        }
    }
}
