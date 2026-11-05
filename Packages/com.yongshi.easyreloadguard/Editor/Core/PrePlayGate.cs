using System;
using System.Linq;
using UnityEngine;

namespace EasyReloadGuard.Editor
{
    /// <summary>ExitingEditMode 时的关口：先处理强制重载，再按设置执行检查。仅关闭 Domain Reload 时生效。</summary>
    internal static class PrePlayGate
    {
        /// <summary>返回 false 表示本次进入 Play 已取消（改为先重新加载脚本，或检查不通过被阻止）。</summary>
        internal static bool OnExitingEditMode()
        {
            if (EditorCompat.PlayModeOptionsMismatch) Log.Warning(EditorCompat.MismatchHint + "。");
            if (!EditorCompat.DomainReloadDisabled) return true;

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
                    return false;
                }

                return CheckAudit();
            }
            catch (Exception e)
            {
                // 工具自身故障不阻断 Play，但必须可见
                Log.Exception("进入 Play 前的关口执行失败，已放行。", e);
                return true;
            }
        }

        static bool CheckAudit()
        {
            var mode = ReloadGuardSettings.instance.prePlayCheck;
            if (mode == PrePlayCheck.Off) return true;

            var report = ReloadAudit.Cached;
            if (report.BlockingCount == 0) return true;

            var message = $"检查发现 {report.BlockingCount} 个不符合规范的问题不在暂缓清单中（{report.Summary()}）。";
            if (mode == PrePlayCheck.Block)
            {
                EditorCompat.CancelEnterPlayMode();
                Log.Error(message + "已阻止进入 Play：补上处理方式，或在设置页列为暂缓（仍不符合规范），然后重试。\n" + report.TopLines(20));
                if (!Application.isBatchMode) ReloadGuardSettingsProvider.Open();
                return false;
            }

            Log.Warning(message + "详见 Project Settings/Easy Reload Guard。\n" + report.TopLines(10));
            return true;
        }

        static string Append(string reasons, string reason) => reasons == null ? reason : reasons + "；" + reason;
    }
}
