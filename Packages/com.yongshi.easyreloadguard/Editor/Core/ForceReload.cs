using System;
using System.Collections.Generic;
using UnityEditor;

namespace EasyReloadGuard.Editor
{
    /// <summary>强制重载入口：手动菜单、重置失败兜底、运行时请求、适配器 RequireDomainReload。</summary>
    public static class ForceReload
    {
        const string k_EnterPlayAfterReloadKey = "EasyReloadGuard.EnterPlayAfterReload";
        const string k_Menu = "Tools/Easy Reload Guard/";

        // 静态字段随域重载清空：重载本身就满足了请求
        static string s_PendingReason;
        static bool s_ReloadAfterExit;

        /// <summary>登记：下次进入 Play 前先重载域（仅关闭 Domain Reload 且本域已进入过 Play 时生效）。</summary>
        public static void RequestBeforeNextPlay(string reason)
        {
            s_PendingReason = string.IsNullOrWhiteSpace(reason) ? "未说明原因" : reason;
            Log.Warning($"已登记：下次进入 Play 前重载域（{s_PendingReason}）。");
        }

        [MenuItem(k_Menu + "立即重载域", priority = 20)]
        public static void ReloadNow()
        {
            if (EditorApplication.isPlaying)
            {
                s_ReloadAfterExit = true;
                EditorApplication.ExitPlaymode();
                return;
            }

            Log.Notice("请求重载脚本域。");
            EditorCompat.RequestDomainReload();
        }

        [MenuItem(k_Menu + "重载域并进入 Play", priority = 21)]
        static void ReloadAndPlayMenu() => ReloadAndPlay("手动请求");

        public static void ReloadAndPlay(string reason)
        {
            SessionState.SetBool(k_EnterPlayAfterReloadKey, true);
            Log.Notice($"重载脚本域后进入 Play（{reason}）。");
            if (EditorApplication.isPlaying)
            {
                s_ReloadAfterExit = true;
                EditorApplication.ExitPlaymode();
                return;
            }

            EditorCompat.RequestDomainReload();
        }

        /// <summary>取出并清空全部待处理的重载原因；没有则返回 null。</summary>
        internal static string TakePendingReason(ResetPlan plan)
        {
            var reasons = new List<string>();
            if (s_PendingReason != null) reasons.Add(s_PendingReason);
            if (PlaySession.PendingReloadReason != null) reasons.Add("运行时请求：" + PlaySession.PendingReloadReason);
            reasons.AddRange(plan.RequiredReloadReasons);
            s_PendingReason = null;
            PlaySession.PendingReloadReason = null;
            return reasons.Count == 0 ? null : string.Join("；", reasons);
        }

        internal static void ResumeAfterReload()
        {
            if (!SessionState.GetBool(k_EnterPlayAfterReloadKey, false)) return;
            SessionState.EraseBool(k_EnterPlayAfterReloadKey);
            EditorCompat.RunOnNextUpdate(EnterPlayIfIdle);
        }

        internal static void OnEnteredEditMode()
        {
            if (!s_ReloadAfterExit) return;
            s_ReloadAfterExit = false;
            EditorCompat.RequestDomainReload();
        }

        static void EnterPlayIfIdle()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.EnterPlaymode();
        }
    }
}
