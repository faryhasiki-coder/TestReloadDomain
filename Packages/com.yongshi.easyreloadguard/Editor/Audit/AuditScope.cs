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
    /// 设置中排除的包/程序集、适配器声明 SelfHandled 的包/程序集、所在目录放了排除标记的程序集也不扫描。
    /// </summary>
    internal static class AuditScope
    {
        /// <summary>
        /// 目录排除标记的文件名。放在某个目录中，该目录及子目录里的程序集都不检查；文件内容（可选）是原因。
        /// 以点开头，Unity 不导入、不生成 .meta，适合放在只存在于个人本机、不随项目提交的插件目录里。
        /// </summary>
        internal const string ExcludeMarkerFileName = ".easyreloadguard-exclude";

        public static List<Assembly> Resolve(ReloadGuardSettings settings, ResetPlan plan, AuditReport report)
        {
            var wanted = new SortedSet<string>(StringComparer.Ordinal);
            var origins = new Dictionary<string, string>(StringComparer.Ordinal);
            var markers = new ExcludeMarkers(Path.GetFullPath("."));

            foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies))
            {
                // 只按 asmdef 位置查找：没有 asmdef 的代码都编进 Assembly-CSharp 等预定义程序集，不能按某个源文件整体跳过
                var asmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.name);
                if (asmdef != null && markers.TryFind(asmdef, out var marker))
                {
                    report.MarkerExcludedAssemblies[assembly.name] = marker;
                    continue;
                }

                var package = assembly.sourceFiles.Length > 0 ? PackageNameFromPath(ToProjectRelative(assembly.sourceFiles[0])) : null;
                Consider(assembly.name, package, false);
            }

            var playerPlugins = PlayerPluginPaths();
            foreach (var path in CompilationPipeline.GetPrecompiledAssemblyPaths(
                         CompilationPipeline.PrecompiledAssemblySources.UserAssembly))
            {
                if (!playerPlugins.Contains(FullPath(path))) continue;
                if (markers.TryFind(path, out var marker))
                {
                    report.MarkerExcludedAssemblies[AssemblyNameOf(path)] = marker;
                    continue;
                }

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
                // 本包运行时程序集的 Play 计数与登记表由本包在 Play 开始/结束时自行管理（只在 Editor 中存在），由包自身的测试覆盖，不进入项目报告与遗留清单
                if (loaded == typeof(PlaySession).Assembly) continue;
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
                    var handler = plan.ProjectSelfHandler(name) ?? (package != null ? plan.ProjectSelfHandler(package) : null);
                    if (handler != null) report.ReviewedAssemblies[name] = handler;
                    return;
                }

                wanted.Add(name);
                origins[name] = Origin(package, precompiled);
            }
        }

        /// <summary>
        /// 从程序集的位置（asmdef 或 DLL）所在目录逐级向上查找排除标记，直到包根目录（含 package.json）或工程根目录为止，
        /// 工程根目录本身不查。按目录缓存结果，一次检查中每个目录只读一次磁盘。
        /// </summary>
        internal sealed class ExcludeMarkers
        {
            readonly string m_ProjectRoot;
            readonly Dictionary<string, string> m_ByDirectory = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public ExcludeMarkers(string projectRoot)
            {
                m_ProjectRoot = Normalize(projectRoot);
            }

            /// <summary>找到标记时返回“标记所在目录（工程相对路径）：原因”。</summary>
            public bool TryFind(string path, out string marker)
            {
                var full = Normalize(Path.IsPathRooted(path) ? path : Path.Combine(m_ProjectRoot, path));
                marker = Find(Path.GetDirectoryName(full));
                return marker != null;
            }

            string Find(string directory)
            {
                if (string.IsNullOrEmpty(directory)) return null;
                directory = Normalize(directory);
                if (string.Equals(directory, m_ProjectRoot, StringComparison.OrdinalIgnoreCase)) return null;
                if (m_ByDirectory.TryGetValue(directory, out var cached)) return cached;

                string result;
                var file = Path.Combine(directory, ExcludeMarkerFileName);
                if (File.Exists(file)) result = Describe(directory, file);
                else if (File.Exists(Path.Combine(directory, "package.json"))) result = null;
                else result = Find(Path.GetDirectoryName(directory));

                m_ByDirectory[directory] = result;
                return result;
            }

            string Describe(string directory, string file)
            {
                string reason;
                try
                {
                    reason = string.Join(" ", File.ReadAllText(file)
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
                }
                catch (IOException)
                {
                    reason = string.Empty;
                }

                var relative = directory.StartsWith(m_ProjectRoot + "/", StringComparison.OrdinalIgnoreCase)
                    ? directory.Substring(m_ProjectRoot.Length + 1)
                    : directory;
                return $"{relative}/：{(reason.Length > 0 ? reason : "未说明原因")}";
            }

            static string Normalize(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
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
