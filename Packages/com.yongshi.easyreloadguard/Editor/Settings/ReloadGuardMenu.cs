using UnityEditor;

namespace EasyReloadGuard.Editor
{
    internal static class ReloadGuardMenu
    {
        const string k_Menu = "Tools/Easy Reload Guard/";

        [MenuItem(k_Menu + "运行检查", priority = 0)]
        public static void RunAudit()
        {
            var report = ReloadAudit.Run();
            var message = $"检查完成：{report.Summary()}；阻断 {report.BlockingCount}。{EditorCompat.DescribePlayModeOptions()}。报告：{ReloadAudit.DefaultReportPath}";
            if (report.BlockingCount > 0) Log.Error(message + "\n" + report.TopLines(20));
            else if (report.Count(Severity.Warning) > 0) Log.Warning(message + "\n" + report.TopLines(20));
            else Log.Notice(message);
            ReloadGuardSettingsProvider.Open();
        }

        [MenuItem(k_Menu + "设置与报告", priority = 1)]
        static void OpenSettings() => ReloadGuardSettingsProvider.Open();

        [MenuItem(k_Menu + "将当前遗留问题写入基线", priority = 40)]
        public static void WriteBaseline()
        {
            if (!EditorUtility.DisplayDialog("写入基线",
                    $"把当前所有遗留静态状态问题（警告/错误）写入 {Baseline.FilePath}。\n" +
                    "基线内问题不再阻断 Play，但仍出现在报告中；声明错误与适配器问题不会写入。\n已有基线将被覆盖。",
                    "写入", "取消"))
                return;

            var count = Baseline.Write(ReloadAudit.Run());
            ReloadAudit.Run(); // 以新基线刷新结果
            Log.Notice($"已写入 {count} 条基线：{Baseline.FilePath}。请纳入版本管理。");
        }
    }
}
