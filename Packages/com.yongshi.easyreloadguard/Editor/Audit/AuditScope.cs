using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using Assembly = System.Reflection.Assembly;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 检查范围：Assets 与全部包（嵌入式、注册表缓存）中的 Player 程序集，以及对当前 Player 平台可用的预编译 DLL。
    /// 仅编辑器可用的 DLL（版本管理、IDE、IL 处理等工具库）不参与 Play 运行逻辑，不扫描；
    /// 设置中排除的包/程序集、适配器声明 SelfHandled 的包/程序集也不扫描。
    /// </summary>
    internal static class AuditScope
    {
        public static List<Assembly> Resolve(ReloadGuardSettings settings, ResetPlan plan, AuditReport report)
        {
            var wanted = new SortedSet<string>(StringComparer.Ordinal);
            var origins = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies))
            {
                var package = assembly.sourceFiles.Length > 0 ? PackageNameFromPath(ToProjectRelative(assembly.sourceFiles[0])) : null;
                Consider(assembly.name, package, false);
            }

            var playerPlugins = PlayerPluginPaths();
            foreach (var path in CompilationPipeline.GetPrecompiledAssemblyPaths(
                         CompilationPipeline.PrecompiledAssemblySources.UserAssembly))
            {
                if (!playerPlugins.Contains(FullPath(path))) continue;
                var relative = ToProjectRelative(path);
                var package = PackageNameFromPath(relative);
                if (package != null || relative.StartsWith("Assets/", StringComparison.Ordinal))
                    Consider(AssemblyNameOf(path), package, true);
            }

            var result = new List<Assembly>();
            foreach (var name in wanted)
            {
                var loaded = ReflectionUtil.FindLoadedAssembly(name);
                if (loaded == null || ReflectionUtil.IsTestAssembly(loaded)) continue;
                result.Add(loaded);
                report.ScannedAssemblies.Add(name);
                report.AssemblyOrigins[name] = origins[name];
            }

            return result;

            void Consider(string name, string package, bool precompiled)
            {
                if (settings.excludedAssemblies.Contains(name)) return;
                if (package != null && settings.excludedPackages.Contains(package)) return;
                if (plan.IsSelfHandled(name) || (package != null && plan.IsSelfHandled(package)))
                {
                    if (!report.SelfHandledAssemblies.Contains(name)) report.SelfHandledAssemblies.Add(name);
                    return;
                }

                wanted.Add(name);
                origins[name] = Origin(package, precompiled);
            }
        }

        /// <summary>报告分组用的来源：Assets 源码、包源码或预编译 DLL。</summary>
        internal static string Origin(string package, bool precompiled)
        {
            var location = package == null ? "Assets" : "包 " + package;
            return precompiled ? "DLL（" + location + "）" : location;
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

        /// <summary>对当前构建目标可用的托管插件（物理路径）。</summary>
        static HashSet<string> PlayerPluginPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var importer in PluginImporter.GetImporters(EditorUserBuildSettings.activeBuildTarget))
            {
                if (!importer.isNativePlugin) paths.Add(FullPath(FileUtil.GetPhysicalPath(importer.assetPath)));
            }

            return paths;
        }

        static string FullPath(string path) => Path.GetFullPath(path).Replace('\\', '/');

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
