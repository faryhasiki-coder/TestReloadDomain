using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 批处理入口（CI/离线检查）：
    /// Unity -batchmode -nographics -projectPath &lt;项目&gt; -executeMethod EasyReloadGuard.Editor.ReloadGuardCli.Audit
    ///       [-easyReloadGuardReport &lt;报告路径&gt;] [-easyReloadGuardFailOn pending|violation]
    /// pending（默认）：存在不在暂缓清单中的不符合项时失败；violation：存在任何不符合项（含暂缓）时失败，即要求符合规范。
    /// 退出码：0 通过，1 按失败条件存在不符合项，2 检查自身失败。不要同时传 -quit。
    /// </summary>
    public static class ReloadGuardCli
    {
        public static void Audit()
        {
            int exitCode;
            try
            {
                var args = Environment.GetCommandLineArgs();
                var reportPath = ArgValue(args, "-easyReloadGuardReport") ?? ReloadAudit.DefaultReportPath;
                var failOn = ArgValue(args, "-easyReloadGuardFailOn");
                var failOnAnyViolation = FailOnAnyViolation(failOn);

                var report = ReloadAudit.Run();
                ReloadAudit.WriteReport(report, reportPath);
                var failing = failOnAnyViolation ? report.ViolationCount : report.BlockingCount;
                Debug.Log($"[Easy Reload Guard] 检查完成。{report.Summary()}。按失败条件计 {failing} 项。报告：{reportPath}");
                exitCode = failing > 0 ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 2;
            }

            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        internal static bool FailOnAnyViolation(string value)
        {
            if (value == null || string.Equals(value, "pending", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(value, "violation", StringComparison.OrdinalIgnoreCase)) return true;
            throw new ArgumentException("-easyReloadGuardFailOn 只接受 pending 或 violation；0.4 的 error/warning 已删除。", nameof(value));
        }

        static string ArgValue(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
