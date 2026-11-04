using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 把包内 Skills~/easy-reload-guard 安装到 Claude Code 与 Codex 的用户 Skill 目录（~/.claude/skills、~/.codex/skills），
    /// 让写代码的 Agent 了解本包的约束。做法参考 Unity MCP 的“Install Skills”，但直接从已安装的包复制，Skill 始终与包版本一致。
    /// 目标目录中的标记文件记录安装时 Skill 内容的哈希（以及包版本，仅供阅读）：用 tag 或提交号引用本包时 package.json 版本未必变化，
    /// 按内容判断才不会漏掉更新。没有标记的目录（用户自己的同名 Skill）或目录联接不覆盖。
    /// </summary>
    internal static class ReloadGuardSkillInstaller
    {
        internal const string SkillName = "easy-reload-guard";
        internal const string MarkerFileName = ".easyreloadguard-skill";
        const string k_Menu = "Tools/Easy Reload Guard/";
        const string k_NoticeKeyPrefix = "EasyReloadGuard.SkillNotice.";

        internal enum SkillStatus
        {
            NotInstalled,
            Outdated,
            UpToDate,
            /// <summary>目录已存在但不是本包安装的，不覆盖。</summary>
            NotManaged,
        }

        internal readonly struct SkillTarget
        {
            public SkillTarget(string name, string toolDirectory)
            {
                Name = name;
                ToolDirectory = toolDirectory;
            }

            public string Name { get; }

            /// <summary>Agent 工具的用户目录，如 ~/.claude。</summary>
            public string ToolDirectory { get; }

            public string Directory => Path.Combine(ToolDirectory, "skills", SkillName);

            /// <summary>本机用过这个工具（用户目录存在）。</summary>
            public bool ToolPresent => System.IO.Directory.Exists(ToolDirectory);
        }

        internal static SkillTarget[] Targets
        {
            get
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return new[]
                {
                    new SkillTarget("Claude Code", Path.Combine(home, ".claude")),
                    new SkillTarget("Codex", Path.Combine(home, ".codex")),
                };
            }
        }

        static UnityEditor.PackageManager.PackageInfo Package =>
            UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ReloadGuardSkillInstaller).Assembly);

        internal static string PackageVersion => Package?.version ?? string.Empty;

        /// <summary>包内 Skill 源目录（目录名以 ~ 结尾，Unity 不导入）。</summary>
        internal static string SourceDirectory
        {
            get
            {
                var package = Package;
                return package == null ? null : Path.Combine(package.resolvedPath, "Skills~", SkillName);
            }
        }

        static string s_SourceHash;

        /// <summary>包内 Skill 内容的哈希，读不到源目录时为空字符串。按脚本加载缓存：设置页每次重绘都会用到。</summary>
        internal static string SourceHash
        {
            get
            {
                if (s_SourceHash != null) return s_SourceHash;
                var source = SourceDirectory;
                return s_SourceHash = source != null && Directory.Exists(source) ? ComputeHash(source) : string.Empty;
            }
        }

        internal static SkillStatus GetStatus(SkillTarget target) => GetStatus(target.Directory, SourceHash);

        /// <param name="hash">当前 Skill 内容的哈希，见 <see cref="ComputeHash"/>。</param>
        internal static SkillStatus GetStatus(string directory, string hash)
        {
            if (!Directory.Exists(directory)) return SkillStatus.NotInstalled;
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return SkillStatus.NotManaged;
            var marker = Path.Combine(directory, MarkerFileName);
            if (!File.Exists(marker))
                return Directory.EnumerateFileSystemEntries(directory).Any() ? SkillStatus.NotManaged : SkillStatus.NotInstalled;
            var installed = File.ReadAllLines(marker).FirstOrDefault()?.Trim();
            return string.Equals(installed, hash, StringComparison.Ordinal)
                ? SkillStatus.UpToDate
                : SkillStatus.Outdated;
        }

        /// <summary>
        /// 用源目录完整替换目标目录，并写入标记：第一行为内容哈希，第二行为包版本。目标不是本包安装的时抛出异常，不做任何修改。
        /// </summary>
        internal static void Install(string source, string directory, string version)
        {
            if (string.IsNullOrEmpty(source) || !Directory.Exists(source))
                throw new DirectoryNotFoundException($"包内没有 Skill 源目录：{source}");
            var hash = ComputeHash(source);
            if (GetStatus(directory, hash) == SkillStatus.NotManaged)
                throw new InvalidOperationException($"{directory} 已存在，但不是本包安装的，没有覆盖。确认后手动删除再安装。");

            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            CopyDirectory(source, directory);
            File.WriteAllLines(Path.Combine(directory, MarkerFileName), new[] { hash, version });
        }

        /// <summary>按相对路径排序，对每个文件的“路径 + 内容”计算 SHA-256。</summary>
        internal static string ComputeHash(string directory)
        {
            var root = Path.GetFullPath(directory);
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => (Relative: f.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/'), Full: f))
                .Where(f => f.Relative != MarkerFileName)
                .OrderBy(f => f.Relative, StringComparer.Ordinal);
            using (var sha = SHA256.Create())
            {
                foreach (var (relative, full) in files)
                {
                    var name = Encoding.UTF8.GetBytes(relative + "\n");
                    sha.TransformBlock(name, 0, name.Length, null, 0);
                    var content = File.ReadAllBytes(full);
                    sha.TransformBlock(content, 0, content.Length, null, 0);
                }

                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return BitConverter.ToString(sha.Hash).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary>安装到一个目标，结果写入 Console。返回是否成功。</summary>
        internal static bool Install(SkillTarget target)
        {
            try
            {
                Install(SourceDirectory, target.Directory, PackageVersion);
                Log.Notice($"已为 {target.Name} 安装 Agent Skill {SkillName}（{PackageVersion}）：{target.Directory}");
                return true;
            }
            catch (Exception exception)
            {
                Log.Error($"为 {target.Name} 安装 Agent Skill 失败：{exception.Message}");
                return false;
            }
        }

        [MenuItem(k_Menu + "安装或更新 Agent Skill", priority = 2)]
        static void InstallFromMenu()
        {
            // 本机两个工具都没用过时两处都装；否则只装用过的
            var targets = Targets.Where(t => t.ToolPresent).ToArray();
            if (targets.Length == 0) targets = Targets;

            var lines = targets.Select(t => $"{t.Name}：{(Install(t) ? "已安装" : "失败，见 Console")}\n{t.Directory}");
            EditorUtility.DisplayDialog("安装 Agent Skill", string.Join("\n\n", lines), "确定");
        }

        /// <summary>
        /// 本机用过的工具还没有当前内容的 Skill 时，在 Console 提示一次（每份 Skill 内容每台机器一次）。
        /// 不用模态对话框：编辑器可能正由外部工具自动驱动，对话框会卡住它。
        /// </summary>
        [InitializeOnLoadMethod]
        static void NotifyIfNeeded()
        {
            if (Application.isBatchMode) return;
            try
            {
                var hash = SourceHash;
                if (hash.Length == 0 || EditorPrefs.GetBool(k_NoticeKeyPrefix + hash)) return;

                var pending = Targets.Where(t => t.ToolPresent)
                    .Where(t => GetStatus(t) is SkillStatus.NotInstalled or SkillStatus.Outdated)
                    .Select(t => t.Name).ToArray();
                if (pending.Length > 0)
                    Log.Warning($"{string.Join("、", pending)} 还没有安装最新的 Agent Skill（{SkillName}），写代码的 Agent 会不了解本包的约束。" +
                                $"在 Project Settings/Easy Reload Guard 的“Agent Skill”中安装，或使用菜单 {k_Menu}安装或更新 Agent Skill。");
                EditorPrefs.SetBool(k_NoticeKeyPrefix + hash, true);
            }
            catch (Exception exception)
            {
                Log.Verbose($"检查 Agent Skill 安装状态失败：{exception.Message}");
            }
        }

        static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
