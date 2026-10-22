using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor.Compilation;
using Assembly = System.Reflection.Assembly;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 检查范围：Assets 下的 Player 程序集与预编译 DLL；包内程序集需在设置中显式纳入。
    /// 适配器声明 SelfHandled 的包/程序集与设置中排除的程序集不扫描。
    /// </summary>
    internal static class AuditScope
    {
        public static List<Assembly> Resolve(ReloadGuardSettings settings, ResetPlan plan, AuditReport report)
        {
            var wanted = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies))
            {
                var package = assembly.sourceFiles.Length > 0 ? PackageNameFromPath(ToProjectRelative(assembly.sourceFiles[0])) : null;
                Consider(assembly.name, package);
            }

            foreach (var path in CompilationPipeline.GetPrecompiledAssemblyPaths(
                         CompilationPipeline.PrecompiledAssemblySources.UserAssembly))
            {
                var relative = ToProjectRelative(path);
                var package = PackageNameFromPath(relative);
                if (package != null || relative.StartsWith("Assets/", StringComparison.Ordinal))
                    Consider(AssemblyNameOf(path), package);
            }

            var result = new List<Assembly>();
            foreach (var name in wanted)
            {
                var loaded = ReflectionUtil.FindLoadedAssembly(name);
                if (loaded == null) continue;
                result.Add(loaded);
                report.ScannedAssemblies.Add(name);
            }

            return result;

            void Consider(string name, string package)
            {
                if (settings.excludedAssemblies.Contains(name)) return;
                if (package != null && !settings.includedPackages.Contains(package)) return;
                if (plan.IsSelfHandled(name) || (package != null && plan.IsSelfHandled(package)))
                {
                    if (!report.SelfHandledAssemblies.Contains(name)) report.SelfHandledAssemblies.Add(name);
                    return;
                }

                wanted.Add(name);
            }
        }

        /// <summary>
        /// 从项目相对路径 "Packages/&lt;name&gt;/..." 或 "Library/PackageCache/&lt;name&gt;@hash/..." 解析包名；不属于包返回 null。
        /// </summary>
        internal static string PackageNameFromPath(string projectRelativePath)
        {
            var path = projectRelativePath.Replace('\\', '/');
            string rest;
            if (path.StartsWith(k_Packages, StringComparison.Ordinal)) rest = path.Substring(k_Packages.Length);
            else if (path.StartsWith(k_PackageCache, StringComparison.Ordinal)) rest = path.Substring(k_PackageCache.Length);
            else return null;

            var end = rest.IndexOfAny(new[] { '@', '/' });
            return end > 0 ? rest.Substring(0, end) : null;
        }

        const string k_Packages = "Packages/";
        const string k_PackageCache = "Library/PackageCache/";

        static string ToProjectRelative(string path)
        {
            var normalized = path.Replace('\\', '/');
            var root = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
            return normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? normalized.Substring(root.Length) : normalized;
        }

        static string AssemblyNameOf(string dllPath)
        {
            try
            {
                return System.Reflection.AssemblyName.GetAssemblyName(Path.GetFullPath(dllPath)).Name;
            }
            catch (Exception)
            {
                return Path.GetFileNameWithoutExtension(dllPath);
            }
        }
    }
}
