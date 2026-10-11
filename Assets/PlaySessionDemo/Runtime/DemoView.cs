using System;
using Faryhasiki.PlaySessions;
using UnityEngine;

namespace PlaySessionDemo
{
    public sealed class DemoView : MonoBehaviour
    {
        private Font font;
        private GUIStyle title;
        private GUIStyle body;
        private GUIStyle small;
        private GUIStyle number;
        private GUIStyle button;

        private void Awake() => font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 20);
        private void OnDestroy() { if (font != null) Destroy(font); }

        private void OnGUI()
        {
            InitializeStyles();
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.045f, 0.065f, 0.10f));
            var scale = Mathf.Min(Screen.width / 1080f, Screen.height / 660f);
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1080 * scale) / 2, (Screen.height - 660 * scale) / 2), Quaternion.identity, Vector3.one * scale);
            var host = PlaySessionRuntime.Current;
            var model = DemoState.Current;
            var ready = PlaySessionRuntime.IsReady && model != null;

            Label(36, 25, 800, 24, "LIFECYCLE LAB  /  PLAY SESSIONS 0.1", small, new Color(0.40f, 0.72f, 0.80f));
            Label(36, 60, 800, 52, "每次 Play，明确开始与结束", title, Color.white);
            Label(36, 118, 980, 30, "关闭 Domain Reload 后，对照静态状态、资源释放与工具请求的会话边界。", body, new Color(0.66f, 0.73f, 0.81f));
            Fill(new Rect(895, 39, 145, 43), ready ? new Color(0.08f, 0.31f, 0.27f) : new Color(0.38f, 0.14f, 0.14f));
            Label(908, 47, 130, 30, host?.Status.ToString().ToUpperInvariant() ?? "NO SESSION", body, Color.white);

            Card(36, "跨会话保留的对照值", DemoRaw.Counter.ToString(), "未重置：再次 Play 会继续累加", new Color(1f, 0.69f, 0.40f));
            Card(556, "会话拥有的状态", DemoState.Counter.ToString(), "每次 Play 从 0 开始；每个事件只加 1", new Color(0.37f, 0.87f, 0.70f));

            Metric(36, "事件订阅", DemoSignals.SubscriberCount.ToString(), "运行 1 / 停止 0");
            Metric(296, "存活资源", DemoLease.LiveCount.ToString(), "运行 1 / 停止 0");
            Metric(556, "后台任务", (host?.PendingTasks ?? 0).ToString(), "退出时取消并等待收尾");
            Metric(816, "异步提交", (model?.CompletedLoads ?? 0).ToString(), "只提交到有效会话");

            GUI.enabled = ready;
            if (Button(36, "发送一个事件")) DemoSignals.Raise();
            if (Button(296, "启动 2 秒后台任务")) model.LoadLater();
            if (Button(556, "发送有效工具请求")) model.SendToolRequest(host.Id);
            if (Button(816, "发送过期工具请求")) model.SendToolRequest(Guid.Empty);
            GUI.enabled = true;
            Label(36, 510, 1010, 32, model?.LastAction ?? "会话未就绪，请打开 Tools / Play Sessions / Monitor 查看原因。", body, new Color(0.78f, 0.84f, 0.91f));
            Label(36, 549, 1010, 27, $"工具请求  通过 {model?.AcceptedRequests ?? 0}  /  拒绝 {model?.RejectedRequests ?? 0}     会话 {host?.Id.ToString().Substring(0, 8) ?? "—"}", small, new Color(0.52f, 0.64f, 0.74f));
            Fill(new Rect(36, 596, 1008, 1), new Color(0.16f, 0.23f, 0.30f));
            Label(36, 609, 1010, 32, "操作：发送事件 → 停止 → 再次 Play。工具按钮为协议演示；不代表外部 package 已完成适配。", small, new Color(0.57f, 0.65f, 0.73f));
            GUI.matrix = previousMatrix;
        }

        private void InitializeStyles()
        {
            if (title != null) return;
            title = Style(34, FontStyle.Bold);
            body = Style(18);
            small = Style(15);
            number = Style(72, FontStyle.Bold);
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 17, fixedHeight = 46 };
            button.normal.textColor = Color.white;
        }

        private GUIStyle Style(int size, FontStyle weight = FontStyle.Normal)
            => new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = weight, wordWrap = true };

        private void Card(float x, string caption, string value, string note, Color accent)
        {
            Fill(new Rect(x, 164, 488, 200), new Color(0.08f, 0.115f, 0.165f));
            Fill(new Rect(x, 164, 488, 3), accent);
            Label(x + 22, 184, 455, 30, caption, body, new Color(0.75f, 0.82f, 0.89f));
            Label(x + 22, 216, 455, 99, value, number, accent);
            Label(x + 22, 324, 455, 29, note, small, new Color(0.58f, 0.68f, 0.77f));
        }

        private void Metric(float x, string caption, string value, string note)
        {
            Label(x, 385, 130, 26, caption, body, new Color(0.68f, 0.75f, 0.83f));
            Label(x + 155, 380, 70, 37, value, title, Color.white);
            Label(x, 421, 242, 25, note, small, new Color(0.48f, 0.59f, 0.71f));
        }

        private bool Button(float x, string caption)
        {
            GUI.backgroundColor = new Color(0.20f, 0.36f, 0.48f);
            var pressed = GUI.Button(new Rect(x, 456, 228, 46), caption, button);
            GUI.backgroundColor = Color.white;
            return pressed;
        }

        private static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void Label(float x, float y, float width, float height, string text, GUIStyle style, Color color)
        {
            style.normal.textColor = color;
            GUI.Label(new Rect(x, y, width, height), text, style);
        }
    }
}
