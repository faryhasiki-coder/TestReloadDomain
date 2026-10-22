using System;
using Faryhasiki.PlaySessions;
using UnityEngine;
using BeforeWallet = PlaySessionDemo.CodeComparison.Before.CoinWallet;
using AfterWallet = PlaySessionDemo.CodeComparison.After.CoinWallet;

namespace PlaySessionDemo.CodeComparison
{
    // 展示层：两边调用完全相同的业务动作，不在 UI 中重置任何计数。
    public sealed class CoinComparisonView : MonoBehaviour
    {
        private Font font;
        private Font codeFont;
        private GUIStyle title, body, small, noteStyle, number, code, button;

        private const string BeforeCode =
            "public static class CoinWallet\n{\n" +
            "    public static int Coins { get; private set; }\n\n" +
            "    public static void CollectCoin() => Coins++;\n}";
        private const string AfterCode =
            "public static class CoinWallet\n{\n" +
            "    [field: SessionOwned(\"sample.wallet\")]\n" +
            "    public static int Coins { get; private set; }\n\n" +
            "    public static void CollectCoin() => Coins++;\n" +
            "    internal static void ResetForPlay() => Coins = 0;\n}";

        private void Awake()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 20);
            codeFont = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New" }, 14);
        }

        private void OnDestroy()
        {
            if (font != null) Destroy(font);
            if (codeFont != null) Destroy(codeFont);
        }

        public void CollectCoin()
        {
            if (!PlaySessionRuntime.IsReady) throw new InvalidOperationException("Play session is not ready.");
            BeforeWallet.CollectCoin();
            AfterWallet.CollectCoin();
        }

        private void OnGUI()
        {
            if (title == null)
            {
                title = Style(32, FontStyle.Bold);
                body = Style(20);
                small = Style(16);
                noteStyle = Style(13);
                number = Style(58, FontStyle.Bold);
                code = new GUIStyle(Style(14)) { font = codeFont, wordWrap = false };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 21 };
            }

            var previousMatrix = GUI.matrix;
            var previousEnabled = GUI.enabled;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.045f, 0.065f, 0.10f));
            var scale = Mathf.Min(Screen.width / 1080f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1080 * scale) / 2, (Screen.height - 720 * scale) / 2), Quaternion.identity, Vector3.one * scale);

            Label(32, 20, 1016, 24, "CODE COMPARISON  /  PLAY SESSIONS", small);
            Label(32, 54, 1016, 48, "同一段捡金币代码，接入前后差在哪？", title);
            Label(32, 106, 1016, 32, "先拾取几枚金币 → 停止 → 再次 Play；关闭 Domain Reload 时观察两边。", body);

            Card(32, "接入前 · 普通静态状态", BeforeWallet.Coins, BeforeCode,
                "再次 Play 保留上次值；脚本重编译后才重新初始化。", new Color(1f, 0.69f, 0.40f));
            Card(556, "接入后 · 声明归属 + 注册重置", AfterWallet.Coins, AfterCode,
                "每次 Play 从 0 开始；CollectCoin 的业务逻辑相同。", new Color(0.37f, 0.87f, 0.70f));

            Label(32, 476, 1016, 29, "还需在现有 DemoComposition.Configure 中注册（本样例只管理数值）：", small);
            Label(32, 511, 1016, 29, "plan.Add(\"sample.wallet\", _ => { }, reset: CodeComparison.After.CoinWallet.ResetForPlay);", code);
            GUI.enabled = previousEnabled && PlaySessionRuntime.IsReady;
            if (GUI.Button(new Rect(32, 557, 1016, 56), "拾取 1 枚金币（两边同时 +1）", button)) CollectCoin();
            GUI.enabled = previousEnabled;
            Label(32, 629, 1016, 30, PlaySessionRuntime.IsReady
                ? "插件调用你注册的重置函数；仅安装插件或仅加属性，都不会自动清空字段。"
                : "会话未就绪，请打开 Tools > Play Sessions > Monitor 查看原因。", small);
            Label(32, 666, 1016, 30, "源码：Runtime/CodeComparison/Before 与 After 目录。完整差异见 CodeComparison/README.md。", small);
            GUI.matrix = previousMatrix;
        }

        private GUIStyle Style(int size, FontStyle weight = FontStyle.Normal)
            => new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = weight, wordWrap = true, normal = { textColor = Color.white } };

        private void Card(float x, string caption, int coins, string source, string note, Color accent)
        {
            Fill(new Rect(x, 156, 492, 302), new Color(0.08f, 0.115f, 0.165f));
            Fill(new Rect(x, 156, 492, 3), accent);
            Label(x + 16, 170, 460, 32, caption, body);
            number.normal.textColor = accent;
            Label(x + 16, 204, 460, 76, coins.ToString(), number);
            Label(x + 16, 286, 460, 154, source, code);
            Label(x + 16, 433, 460, 24, note, noteStyle);
        }

        private static void Label(float x, float y, float width, float height, string text, GUIStyle style)
            => GUI.Label(new Rect(x, y, width, height), text, style);

        private static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
