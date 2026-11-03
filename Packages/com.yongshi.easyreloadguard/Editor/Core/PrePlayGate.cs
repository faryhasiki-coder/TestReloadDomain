using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EasyReloadGuard.Editor
{
    /// <summary>ExitingEditMode 时的关口：先处理强制重载，再按设置执行检查。仅关闭 Domain Reload 时生效。</summary>
    internal static class PrePlayGate
    {
        internal static void OnExitingEditMode()
        {
            if (!EditorCompat.DomainReloadDisabled) return;

            try
            {
                var plan = ResetPlan.Current;
                var reason = ForceReload.TakePendingReason(plan);
                if (!DomainState.IsFresh && plan.FieldSteps.Any(s => s.NeedsCleanBaseline))
                    reason = Append(reason, "本次脚本加载后已运行过 Play，InitialValue 字段需要在重新加载后的干净状态下记录初值");

                if (reason != null && !DomainState.IsFresh)
                {
                    EditorCompat.CancelEnterPlayMode();
                    Log.Warning($"本次进入 Play 改为先重新加载脚本：{reason}");
                    EditorCompat.RunOnNextUpdate(() => ForceReload.ReloadAndPlay(reason));
                    return;
                }

                CheckAudit();
            }
            catch (Exception e)
            {
                // 工具自身故障不阻断 Play，但必须可见
                Log.Exception("进入 Play 前的关口执行失败，已放行。", e);
            }
        }

        static void CheckAudit()
        {
            var mode = ReloadGuardSettings.instance.prePlayCheck;
            if (mode == PrePlayCheck.Off) return;

            var report = ReloadAudit.Cached;
            if (report.BlockingCount == 0) return;

            var message = $"检查发现 {report.BlockingCount} 个错误不在遗留问题清单中（{report.Summary()}）。";
            if (mode == PrePlayCheck.Block)
            {
                EditorCompat.CancelEnterPlayMode();
                Log.Error(message + "已阻止进入 Play：修复、用 [ReloadSafe] 说明保留原因，或确认后记为已知遗留，然后重试。\n" + report.TopLines(20));
                if (!Application.isBatchMode) ReloadGuardSettingsProvider.Open();
            }
            else
            {
                Log.Warning(message + "详见 Project Settings/Easy Reload Guard。\n" + report.TopLines(10));
            }
        }

        static string Append(string reasons, string reason) => reasons == null ? reason : reasons + "；" + reason;
    }
}
