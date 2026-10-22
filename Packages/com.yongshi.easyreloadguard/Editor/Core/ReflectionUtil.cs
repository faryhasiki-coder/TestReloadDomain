using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace EasyReloadGuard.Editor
{
    internal static class ReflectionUtil
    {
        public const BindingFlags StaticMembers =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        static readonly Dictionary<Assembly, bool> s_TestAssemblies = new Dictionary<Assembly, bool>();

        public static string TypeName(Type type) => type.FullName ?? type.Name;

        /// <summary>面向人的成员名：自动属性的 backing field 显示为属性名。</summary>
        public static string Describe(MemberInfo member)
        {
            var name = member.Name;
            if (member is FieldInfo && name.StartsWith("<", StringComparison.Ordinal) &&
                name.EndsWith("k__BackingField", StringComparison.Ordinal))
            {
                var end = name.IndexOf('>');
                if (end > 1) name = name.Substring(1, end - 1) + "（自动属性）";
            }

            return $"{TypeName(member.DeclaringType)}.{name}";
        }

        /// <summary>稳定标识（基线、去重使用）：程序集|类型.成员。</summary>
        public static string Subject(MemberInfo member) =>
            $"{member.DeclaringType.Assembly.GetName().Name}|{TypeName(member.DeclaringType)}.{member.Name}";

        public static string Subject(Type type) => $"{type.Assembly.GetName().Name}|{TypeName(type)}";

        public static Type[] SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null).ToArray();
            }
        }

        /// <summary>测试程序集的夹具不参与项目的重置计划与检查。</summary>
        public static bool IsTestAssembly(Assembly assembly)
        {
            if (!s_TestAssemblies.TryGetValue(assembly, out var result))
            {
                result = assembly.GetReferencedAssemblies().Any(r => r.Name == "nunit.framework");
                s_TestAssemblies[assembly] = result;
            }

            return result;
        }

        public static bool IsCompilerGenerated(Type type) =>
            type.Name.StartsWith("<", StringComparison.Ordinal) ||
            type.IsDefined(typeof(CompilerGeneratedAttribute), false);

        /// <summary>事件或自动属性的编译器生成字段，检查时按普通静态状态处理。</summary>
        public static bool IsBackingField(Type type, FieldInfo field) =>
            field.Name.EndsWith("k__BackingField", StringComparison.Ordinal) ||
            type.GetEvent(field.Name, StaticMembers) != null;

        public static Assembly FindLoadedAssembly(string name)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if (!assembly.IsDynamic && assembly.GetName().Name == name)
                    return assembly;
            return null;
        }

        /// <summary>解析 "Namespace.Type, AssemblyName"；只在已加载程序集中查找，不触发加载。</summary>
        public static Type ResolveType(string qualifiedName)
        {
            if (string.IsNullOrWhiteSpace(qualifiedName)) return null;
            var depth = 0;
            var split = -1;
            for (var i = 0; i < qualifiedName.Length && split < 0; i++)
            {
                switch (qualifiedName[i])
                {
                    case '[': depth++; break;
                    case ']': depth--; break;
                    case ',' when depth == 0: split = i; break;
                }
            }

            if (split < 0) return null;
            var typeName = qualifiedName.Substring(0, split).Trim();
            var assemblyName = qualifiedName.Substring(split + 1).Split(',')[0].Trim();
            return FindLoadedAssembly(assemblyName)?.GetType(typeName, false);
        }
    }
}
