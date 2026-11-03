using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace EasyReloadGuard.Editor
{
    /// <summary>主工具栏 Play 按钮旁的“重新加载脚本并进入 Play”。右键可只重新加载脚本或打开设置页。</summary>
    internal static class ReloadGuardToolbar
    {
        const string k_Path = "Easy Reload Guard/重新加载脚本并进入 Play";

        /// <summary>保存在 UserSettings（本机、本工程）：只自动显示一次，之后使用者在工具栏中隐藏不会被改回。</summary>
        const string k_ShownKey = "EasyReloadGuard.ToolbarButtonShown";

        [InitializeOnLoadMethod]
        static void ShowOnce()
        {
            if (Application.isBatchMode || EditorUserSettings.GetConfigValue(k_ShownKey) != null) return;
            EditorCompat.RunOnNextUpdate(() =>
            {
                // 工具栏窗口还没创建时不记标记，下次脚本加载再试
                if (EditorCompat.TryShowMainToolbarElement(k_Path)) EditorUserSettings.SetConfigValue(k_ShownKey, "1");
            });
        }

        [MainToolbarElement(k_Path, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement Create()
        {
            var content = new MainToolbarContent("重载并 Play", ReloadGuardStyles.Icon("Refresh"),
                "重新加载脚本后进入 Play（Play 中会先停止）。不编译代码，用于丢弃上一次 Play 留在静态状态里的数据。\n右键：只重新加载脚本、打开设置。");
            return new MainToolbarButton(content, () => ForceReload.ReloadAndPlay("工具栏按钮"))
            {
                populateContextMenu = PopulateContextMenu,
            };
        }

        static void PopulateContextMenu(DropdownMenu menu)
        {
            menu.AppendAction("只重新加载脚本", _ => ForceReload.ReloadNow());
            menu.AppendAction("打开 Easy Reload Guard 设置", _ => ReloadGuardSettingsProvider.Open());
        }
    }
}
