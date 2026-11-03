using UnityEditor;

namespace EasyReloadGuard.Editor
{
    internal static class ReloadGuardMenu
    {
        const string k_Menu = "Tools/Easy Reload Guard/";

        [MenuItem(k_Menu + "检查静态变量并查看结果", priority = 0)]
        public static void RunAudit()
        {
            var report = ReloadAudit.Run();
            var message = $"检查完成。{report.Summary()}。{EditorCompat.DescribePlayModeOptions()}。报告：{ReloadAudit.DefaultReportPath}";
            if (report.BlockingCount > 0) Log.Error(message + "\n" + report.TopLines(20));
            else if (report.Count(Severity.Warning) > 0) Log.Warning(message + "\n" + report.TopLines(20));
            else Log.Notice(message);
            ReloadGuardSettingsProvider.Open();
        }

        [MenuItem(k_Menu + "打开设置", priority = 1)]
        static void OpenSettings() => ReloadGuardSettingsProvider.Open();

        /// <summary>只从设置页调用：需要先看过检查结果再决定，不放在菜单中。</summary>
        public static void WriteBaseline()
        {
            if (!EditorUtility.DisplayDialog("记为已知遗留",
                    $"把当前所有静态状态问题（警告和错误）写入遗留问题清单 {Baseline.FilePath}。\n" +
                    "清单中的问题不再阻止进入 Play，但仍列在报告中；特性用错和适配器问题不会写入。\n现有清单将被覆盖。",
                    "写入", "取消"))
                return;

            var count = Baseline.Write(ReloadAudit.Run());
            ReloadAudit.Run(); // 按新清单刷新结果
            Log.Notice($"已把 {count} 个问题记为已知遗留：{Baseline.FilePath}。请纳入版本管理。");
        }
    }
}
