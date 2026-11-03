using UnityEditor;
using UnityEngine;

namespace EasyReloadGuard.Editor
{
    /// <summary>设置页与工具栏共用的样式、颜色、图标和小部件。样式延迟创建：静态构造时 EditorStyles 可能尚未就绪。</summary>
    internal static class ReloadGuardStyles
    {
        static GUIStyle s_Title;
        static GUIStyle s_Card;
        static GUIStyle s_Pill;
        static GUIStyle s_GroupFoldout;
        static GUIStyle s_RowSubject;
        static GUIStyle s_RowMessage;
        static GUIStyle s_RowDetail;

        public static GUIStyle Title => s_Title ?? (s_Title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15 });

        public static GUIStyle Card => s_Card ?? (s_Card = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(12, 10, 8, 10),
            margin = new RectOffset(0, 0, 4, 6),
        });

        public static GUIStyle Pill => s_Pill ?? (s_Pill = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            padding = new RectOffset(8, 8, 2, 2),
            margin = new RectOffset(0, 6, 2, 2),
            normal = { textColor = Color.white },
        });

        public static GUIStyle GroupFoldout => s_GroupFoldout ?? (s_GroupFoldout = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold });

        public static GUIStyle RowSubject => s_RowSubject ?? (s_RowSubject = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true });

        public static GUIStyle RowMessage => s_RowMessage ?? (s_RowMessage = new GUIStyle(EditorStyles.wordWrappedLabel));

        public static GUIStyle RowDetail => s_RowDetail ?? (s_RowDetail = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
        {
            normal = { textColor = Muted },
        });

        static bool Pro => EditorGUIUtility.isProSkin;

        public static Color Good => Pro ? new Color(0.27f, 0.58f, 0.33f) : new Color(0.20f, 0.52f, 0.27f);
        public static Color Caution => Pro ? new Color(0.78f, 0.55f, 0.16f) : new Color(0.80f, 0.52f, 0.06f);
        public static Color Bad => Pro ? new Color(0.78f, 0.27f, 0.25f) : new Color(0.75f, 0.20f, 0.18f);
        public static Color Neutral => Pro ? new Color(0.36f, 0.36f, 0.36f) : new Color(0.55f, 0.55f, 0.55f);
        public static Color Accent => Pro ? new Color(0.24f, 0.49f, 0.84f) : new Color(0.16f, 0.42f, 0.80f);
        public static Color Muted => Pro ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.38f, 0.38f, 0.38f);
        public static Color RowStripe => Pro ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.04f);

        /// <summary>内置编辑器图标；深色皮肤优先取 d_ 版本。找不到时返回 null，不输出错误日志。</summary>
        public static Texture2D Icon(string name) =>
            (Pro ? EditorGUIUtility.FindTexture("d_" + name) : null) ?? EditorGUIUtility.FindTexture(name);

        public static GUIContent Content(string text, string icon, string tooltip = null) => new GUIContent(text, Icon(icon), tooltip);

        public static Texture2D SeverityIcon(Severity severity)
        {
            switch (severity)
            {
                case Severity.Error: return Icon("console.erroricon.sml");
                case Severity.Warning: return Icon("console.warnicon.sml");
                default: return Icon("console.infoicon.sml");
            }
        }

        public static Color SeverityColor(Severity severity)
        {
            switch (severity)
            {
                case Severity.Error: return Bad;
                case Severity.Warning: return Caution;
                default: return Accent;
            }
        }

        /// <summary>圆角色块标签，宽度随文字。</summary>
        public static void DrawPill(string text, Color color, string tooltip = null)
        {
            var content = new GUIContent(text, tooltip);
            var rect = GUILayoutUtility.GetRect(content, Pill, GUILayout.ExpandWidth(false));
            if (Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(rect, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0, color, 0, 4);
            Pill.Draw(rect, content, false, false, false, false);
        }

        /// <summary>在上一个布局块左侧画一条强调色竖线，用于卡片的状态提示。</summary>
        public static void DrawLeftAccent(Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            var rect = GUILayoutUtility.GetLastRect();
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 1, 3, rect.height - 2), color);
        }

        public static void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(1, 9, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + 4, rect.width, 1), new Color(Neutral.r, Neutral.g, Neutral.b, 0.5f));
        }
    }
}
