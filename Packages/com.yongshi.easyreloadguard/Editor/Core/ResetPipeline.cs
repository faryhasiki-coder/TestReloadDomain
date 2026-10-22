using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 执行顺序（关闭 Domain Reload 时）：
    /// 进入 Play（SubsystemRegistration）：PlaySession.Begin → RegisterReset 登记项 → [ResetStatic] 字段 → [ResetMethod]/适配器重置（按 Order）。
    /// 退出 Play（ExitingPlayMode，始终执行）：PlaySession.Token 取消 → [OnExitPlayMode]/适配器释放（按 Order）。
    /// 单步失败不中断后续步骤，汇总后按设置登记强制重载。
    /// </summary>
    internal static class ResetPipeline
    {
        internal static void RunEnter()
        {
            DomainState.MarkPlayed();
            var registered = PlaySession.DrainRegisteredResets();
            if (!EditorCompat.DomainReloadDisabled) return;

            if (!TryGetPlan(out var plan)) return;
            var stopwatch = Stopwatch.StartNew();
            var failures = new List<string>();
            for (var i = 0; i < registered.Count; i++)
                Execute($"PlaySession.RegisterReset 登记项 #{i + 1}", registered[i], failures);
            foreach (var step in plan.FieldSteps) Execute(step.Name, step.Execute, failures);
            foreach (var step in plan.EnterSteps) Execute(step.Name, step.Execute, failures);
            stopwatch.Stop();

            var total = registered.Count + plan.FieldSteps.Count + plan.EnterSteps.Count;
            Finish("进入 Play 重置", total, failures, $"，用时 {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
        }

        internal static void RunExit()
        {
            var failures = new List<string>();
            PlaySession.End();
            if (!TryGetPlan(out var plan)) return;
            foreach (var step in plan.ExitSteps) Execute(step.Name, step.Execute, failures);
            Finish("退出 Play 释放", plan.ExitSteps.Count, failures, string.Empty);
        }

        static bool TryGetPlan(out ResetPlan plan)
        {
            try
            {
                plan = ResetPlan.Current;
                return true;
            }
            catch (Exception e)
            {
                plan = null;
                Log.Exception("构建重置计划失败，本次未执行重置/释放。", e);
                ForceReload.RequestBeforeNextPlay("重置计划构建失败");
                return false;
            }
        }

        static void Execute(string name, Action action, List<string> failures)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                var cause = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                failures.Add(name);
                Log.Exception($"{name} 执行失败，后续步骤继续执行。", cause);
            }
        }

        static void Finish(string phase, int total, List<string> failures, string detail)
        {
            if (failures.Count == 0)
            {
                Log.Verbose($"会话 #{PlaySession.Id} {phase}：{total} 项完成{detail}。");
                return;
            }

            var message = $"会话 #{PlaySession.Id} {phase}：{failures.Count}/{total} 项失败（{string.Join("、", failures)}），状态可能残留上一会话数据。";
            if (ReloadGuardSettings.instance.reloadOnResetFailure)
            {
                ForceReload.RequestBeforeNextPlay($"{phase}失败");
                message += "下次进入 Play 前将自动重载域；需要立即恢复请用 Tools/Easy Reload Guard/重载域并进入 Play。";
            }
            else
            {
                message += "可用 Tools/Easy Reload Guard/重载域并进入 Play 恢复。";
            }

            Log.Error(message);
        }
    }
}
