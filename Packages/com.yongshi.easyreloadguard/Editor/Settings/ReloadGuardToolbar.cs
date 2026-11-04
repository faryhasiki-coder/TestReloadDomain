using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 主工具栏 Play 按钮旁的两个元素：
    /// Domain Reload 状态图标（绿色闪电：已关闭，进入 Play 快；黄色闪电：已开启，常规），
    /// 以及“重新加载脚本并进入 Play”按钮（右键可只重新加载脚本或打开设置页）。
    /// </summary>
    internal static class ReloadGuardToolbar
    {
        const string k_ReloadPath = "Easy Reload Guard/重新加载脚本并进入 Play";
        const string k_StatusPath = "Easy Reload Guard/Domain Reload 状态";
        const string k_EditorSettingsPath = "Project/Editor";

        /// <summary>保存在 UserSettings（本机、本工程）：每个元素只自动显示一次，之后使用者在工具栏中隐藏不会被改回。</summary>
        const string k_ReloadShownKey = "EasyReloadGuard.ToolbarButtonShown";
        const string k_StatusShownKey = "EasyReloadGuard.ToolbarStatusShown";

        /// <summary>Enter Play Mode 设置没有变化事件，按这个间隔比对一次，变化时刷新图标。</summary>
        const double k_PollInterval = 1.0;

        static readonly Color k_FastColor = new Color(0.30f, 0.80f, 0.36f);
        static readonly Color k_NormalColor = new Color(0.96f, 0.76f, 0.20f);

        static Texture2D s_FastIcon;
        static Texture2D s_NormalIcon;
        static bool s_LastFast;
        static bool s_LastMismatch;
        static double s_NextPoll;

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            if (Application.isBatchMode) return;
            ShowOnce(k_ReloadPath, k_ReloadShownKey);
            ShowOnce(k_StatusPath, k_StatusShownKey);

            s_LastFast = EditorCompat.DomainReloadDisabled;
            s_LastMismatch = EditorCompat.PlayModeOptionsMismatch;
            EditorApplication.update += PollStatus;
            // 图标是编辑器内存中的纹理，脚本重新加载前销毁，避免每次加载都留下一份
            AssemblyReloadEvents.beforeAssemblyReload += DestroyIcons;
        }

        static void ShowOnce(string path, string key)
        {
            if (EditorUserSettings.GetConfigValue(key) != null) return;
            EditorCompat.RunOnNextUpdate(() =>
            {
                // 工具栏窗口还没创建时不记标记，下次脚本加载再试
                if (EditorCompat.TryShowMainToolbarElement(path)) EditorUserSettings.SetConfigValue(key, "1");
            });
        }

        static void PollStatus()
        {
            if (EditorApplication.timeSinceStartup < s_NextPoll) return;
            s_NextPoll = EditorApplication.timeSinceStartup + k_PollInterval;

            var fast = EditorCompat.DomainReloadDisabled;
            var mismatch = EditorCompat.PlayModeOptionsMismatch;
            if (fast == s_LastFast && mismatch == s_LastMismatch) return;
            s_LastFast = fast;
            s_LastMismatch = mismatch;
            MainToolbar.Refresh(k_StatusPath);
        }

        [MainToolbarElement(k_StatusPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement CreateStatus()
        {
            var fast = EditorCompat.DomainReloadDisabled;
            var tooltip = fast
                ? "Domain Reload 已关闭（快速）：进入 Play 不重新加载脚本，静态状态由 Easy Reload Guard 按声明重置。"
                : "Domain Reload 已开启（常规）：每次进入 Play 都重新加载脚本。";
            if (EditorCompat.PlayModeOptionsMismatch) tooltip += "\n" + EditorCompat.MismatchHint + "。";
            tooltip += "\n点击打开 Enter Play Mode 设置；右键打开 Easy Reload Guard 设置。";

            var content = new MainToolbarContent(fast ? GetIcon(ref s_FastIcon, k_FastColor) : GetIcon(ref s_NormalIcon, k_NormalColor), tooltip);
            return new MainToolbarButton(content, () => SettingsService.OpenProjectSettings(k_EditorSettingsPath))
            {
                populateContextMenu = menu => menu.AppendAction("打开 Easy Reload Guard 设置", _ => ReloadGuardSettingsProvider.Open()),
            };
        }

        [MainToolbarElement(k_ReloadPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        static MainToolbarElement CreateReload()
        {
            var content = new MainToolbarContent("重载并 Play", ReloadGuardStyles.Icon("Refresh"),
                "重新加载脚本后进入 Play（Play 中会先停止）。不编译代码，用于丢弃上一次 Play 留在静态状态里的数据。\n右键：只重新加载脚本、打开设置。");
            return new MainToolbarButton(content, () => ForceReload.ReloadAndPlay("工具栏按钮"))
            {
                populateContextMenu = PopulateReloadMenu,
            };
        }

        static void PopulateReloadMenu(DropdownMenu menu)
        {
            menu.AppendAction("只重新加载脚本", _ => ForceReload.ReloadNow());
            menu.AppendAction("打开 Easy Reload Guard 设置", _ => ReloadGuardSettingsProvider.Open());
        }

        static Texture2D GetIcon(ref Texture2D cache, Color color)
        {
            if (cache == null) cache = CreateBoltIcon(color);
            return cache;
        }

        static void DestroyIcons()
        {
            if (s_FastIcon != null) Object.DestroyImmediate(s_FastIcon);
            if (s_NormalIcon != null) Object.DestroyImmediate(s_NormalIcon);
        }

        // 闪电轮廓，32×32、原点在左上角；按此多边形逐像素 4×4 采样得到抗锯齿的透明度
        static readonly Vector2[] k_Bolt =
        {
            new Vector2(20f, 2f), new Vector2(7f, 18f), new Vector2(15f, 18f),
            new Vector2(12f, 30f), new Vector2(25f, 13f), new Vector2(17f, 13f),
        };

        static Texture2D CreateBoltIcon(Color color)
        {
            const int size = 32;
            const int samples = 4;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var hits = 0;
                for (var sy = 0; sy < samples; sy++)
                for (var sx = 0; sx < samples; sx++)
                {
                    var point = new Vector2(x + (sx + 0.5f) / samples, y + (sy + 0.5f) / samples);
                    if (Inside(point)) hits++;
                }

                var c = color;
                c.a = (float)hits / (samples * samples);
                pixels[(size - 1 - y) * size + x] = c; // 纹理第 0 行在底部
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        static bool Inside(Vector2 point)
        {
            var inside = false;
            for (int i = 0, j = k_Bolt.Length - 1; i < k_Bolt.Length; j = i++)
            {
                var a = k_Bolt[i];
                var b = k_Bolt[j];
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }

            return inside;
        }
    }
}
