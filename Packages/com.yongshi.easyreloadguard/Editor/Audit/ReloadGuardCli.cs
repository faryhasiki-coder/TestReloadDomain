using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 批处理入口（CI/离线检查）：
    /// Unity -batchmode -nographics -projectPath &lt;项目&gt; -executeMethod EasyReloadGuard.Editor.ReloadGuardCli.Audit
    ///       [-easyReloadGuardReport &lt;报告路径&gt;] [-easyReloadGuardFailOn error|warning]
    /// 退出码：0 通过，1 存在未基线问题，2 检查自身失败。不要同时传 -quit。
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
                var failOnWarning = string.Equals(ArgValue(args, "-easyReloadGuardFailOn"), "warning", StringComparison.OrdinalIgnoreCase);

                var report = ReloadAudit.Run();
                ReloadAudit.WriteReport(report, reportPath);
                var failing = failOnWarning
                    ? report.Findings.Count(f => !f.Baselined && f.Severity != Severity.Info)
                    : report.BlockingCount;
                Debug.Log($"[Easy Reload Guard] 检查完成：{report.Summary()}；失败条件命中 {failing} 项。报告：{reportPath}");
                exitCode = failing > 0 ? 1 : 0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 2;
            }

            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        static string ArgValue(string[] args, string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
