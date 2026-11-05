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
            else if (!report.IsConformant) Log.Warning(message + "暂缓的不符合项见报告。");
            else Log.Notice(message);
            ReloadGuardSettingsProvider.Open();
        }

        [MenuItem(k_Menu + "打开设置", priority = 1)]
        static void OpenSettings() => ReloadGuardSettingsProvider.Open();

        /// <summary>只从设置页调用：需要先看过检查结果再决定，不放在菜单中。</summary>
        public static void WriteBaseline()
        {
            if (!EditorUtility.DisplayDialog("列为暂缓",
                    $"把当前没有处理方式的不符合项写入暂缓清单 {Baseline.FilePath}。\n" +
                    "暂缓不等于符合规范：清单中的问题不再阻止进入 Play，但报告结论仍是“不符合规范”。\n" +
                    "特性用错、适配器问题、[ReloadSafe] 证明不了的字段不会写入。现有清单将被覆盖。",
                    "写入", "取消"))
                return;

            var count = Baseline.Write(ReloadAudit.Run());
            ReloadAudit.Run(); // 按新清单刷新结果
            Log.Notice($"已把 {count} 行写入暂缓清单：{Baseline.FilePath}。请纳入版本管理。");
        }
    }
}
