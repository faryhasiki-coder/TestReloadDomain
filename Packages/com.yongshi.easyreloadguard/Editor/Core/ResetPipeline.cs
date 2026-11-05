using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace EasyReloadGuard.Editor
{
    /// <summary>
    /// 执行顺序（关闭 Domain Reload 时）：
    /// 进入 Play（全部编辑场景关闭后，后备为 SubsystemRegistration，见 <see cref="EnterTiming"/>）：RegisterReset/RegisterPersistentReset 登记项
    ///   → [ResetStatic] 字段 → [ResetMethod]/适配器重置（按 Order）。每次 Play 只执行一次。
    /// 退出 Play（ExitingPlayMode，始终执行）：[OnExitPlayMode]/适配器释放（按 Order）。
    /// 单步失败不中断后续步骤。进入 Play 的重置有失败时本次 Play 作废：退出、重新加载脚本后自动重新进入，重新进入后再次失败只退出。
    /// </summary>
    internal static class ResetPipeline
    {
        const string k_RetryKey = "EasyReloadGuard.EnterResetRetried";

        static bool s_EnterDone;
        static string s_EnterFailure;
        static readonly List<string> s_InitialFailures = new List<string>();

        /// <summary>本次 Play 的进入重置已执行。</summary>
        internal static bool EnterDone => s_EnterDone;

        /// <summary>ExitingEditMode 时调用：开始新的一次进入。</summary>
        internal static void BeginEnteringPlay()
        {
            s_EnterDone = false;
            s_EnterFailure = null;
            s_InitialFailures.Clear();
            PlaySession.EndPlay();
        }

        internal static void RunEnter()
        {
            if (s_EnterDone) return;
            s_EnterDone = true;
            PlaySession.BeginPlay();
            DomainState.MarkPlayed();
            var failures = new List<string>(s_InitialFailures);
            var registered = PlaySession.Resets.TakeForEnter();
            if (!EditorCompat.DomainReloadDisabled) return;

            if (!TryGetPlan(out var plan))
            {
                s_EnterFailure = "进入 Play 的重置计划构建失败";
                return;
            }
            var stopwatch = Stopwatch.StartNew();
            for (var i = 0; i < registered.Count; i++)
                Execute($"RegisterReset 登记的第 {i + 1} 项", registered[i], failures);
            foreach (var step in plan.FieldSteps) Execute(step.Name, step.Execute, failures);
            foreach (var step in plan.EnterSteps) Execute(step.Name, step.Execute, failures);
            stopwatch.Stop();

            var total = registered.Count + plan.FieldSteps.Count + plan.EnterSteps.Count;
            if (failures.Count == 0)
            {
                SessionState.EraseInt(k_RetryKey);
                Log.Verbose($"第 {PlaySession.Id} 次 Play 进入 Play 重置：{total} 项完成，用时 {stopwatch.Elapsed.TotalMilliseconds:F1} ms。");
                return;
            }

            s_EnterFailure = $"第 {PlaySession.Id} 次 Play 进入 Play 重置：{total} 个重置步骤，发生 {failures.Count} 处失败（含初值记录：{string.Join("、", failures)}）";
        }

        /// <summary>
        /// 在 SubsystemRegistration 钩子中处理进入重置的失败（6000.3.1f1 实测在这个时机退出并重新加载可靠）。
        /// 请求退出后本次 Play 仍会执行到首帧才真正退出，期间在已被污染的状态上运行，所以明确标为作废；
        /// 随后必定重新加载脚本，托管状态不会带到下一次，外部副作用（文件、网络、原生资源等）不在保证范围内。
        /// </summary>
        internal static void HandleEnterFailure()
        {
            if (s_EnterFailure == null) return;
            var failure = s_EnterFailure;
            s_EnterFailure = null;

            if (!ReloadGuardSettings.instance.reloadOnResetFailure)
            {
                SessionState.EraseInt(k_RetryKey);
                ReportEnterFailure(failure + "。本次 Play 已作废并退出（设置“重置失败后重新加载脚本”已关闭，不自动重新进入）；点主工具栏的“重载并 Play”恢复。");
                EditorApplication.ExitPlaymode();
                return;
            }

            if (SessionState.GetInt(k_RetryKey, 0) == 0)
            {
                SessionState.SetInt(k_RetryKey, 1);
                ReportEnterFailure(failure + "。本次 Play 已作废：正在退出、重新加载脚本并重新进入。" +
                          "退出生效前本次 Play 仍会执行到首帧，期间产生的外部副作用不在保证范围内。");
                ForceReload.ReloadAndPlay("进入 Play 重置失败");
                return;
            }

            SessionState.EraseInt(k_RetryKey);
            ForceReload.RequestBeforeNextPlay("进入 Play 重置失败");
            ReportEnterFailure(failure + "。重新加载脚本后重置仍然失败，本次 Play 已作废并退出，不再自动重新进入。修复后再进入 Play（进入前会先重新加载脚本）。");
            EditorApplication.ExitPlaymode();
        }

        static void ReportEnterFailure(string message)
        {
            Log.Error(message);
            try
            {
                Directory.CreateDirectory("Library/EasyReloadGuard");
                File.WriteAllText("Library/EasyReloadGuard/ResetFailureReport.md",
                    $"# Easy Reload Guard 最近一次进入 Play 重置失败\n\n- 时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                    $"- Unity：{UnityEngine.Application.unityVersion}\n- 配置：{EditorCompat.DescribePlayModeOptions()}\n\n{message}\n" +
                    "\n请求退出不保证立即停止业务回调；文件、网络和原生资源等外部副作用需要单独确认。\n");
            }
            catch (Exception e)
            {
                Log.Exception("写入重置失败报告失败；退出和重新加载流程继续。", e);
            }
        }

        /// <summary>
        /// 脚本加载后第一次 Play 的 ExitingEditMode 时提前记录 InitialValue 初值：早于业务 [InitializeOnEnterPlayMode] 与编辑模式对象的退出回调。
        /// 读取字段会触发静态构造，只对静态构造为纯初始化的类型提前记录；其余类型仍在第一次重置时记录，并提示初值可能不干净。
        /// </summary>
        internal static void ObserveInitialValues()
        {
            if (!EditorCompat.DomainReloadDisabled || !DomainState.IsFresh || !TryGetPlan(out var plan)) return;
            var late = new List<string>();
            foreach (var step in plan.FieldSteps)
            {
                if (step.Observed || step.Mode == ResetMode.Clear) continue;
                if (InitializerPurity.IsPure(step.Field.DeclaringType)) Execute(step.Name + " 初值记录", step.Observe, s_InitialFailures);
                else if (step.Mode == ResetMode.InitialValue) late.Add(step.Name);
            }

            if (late.Count > 0)
            {
                Log.Warning("以下 InitialValue 字段所在类型的静态构造不是纯初始化，只能在第一次重置时记录初值，" +
                            "记录前可能已被 [InitializeOnEnterPlayMode] 或编辑模式对象的回调修改：" + string.Join("、", late.Take(20)) +
                            (late.Count > 20 ? $" 等 {late.Count} 个" : string.Empty));
            }
        }

        internal static void RunExit()
        {
            if (TryGetPlan(out var plan)) RunExit(plan);
        }

        internal static void RunExit(ResetPlan plan)
        {
            var failures = new List<string>();
            foreach (var step in plan.ExitSteps) Execute(step.Name, step.Execute, failures);
            if (failures.Count == 0)
            {
                Log.Verbose($"第 {PlaySession.Id} 次 Play 退出 Play 释放：{plan.ExitSteps.Count} 项完成。");
                return;
            }

            var message = $"第 {PlaySession.Id} 次 Play 退出 Play 释放：{plan.ExitSteps.Count} 项中 {failures.Count} 项失败（{string.Join("、", failures)}），可能还留着上一次 Play 的资源。";
            if (ReloadGuardSettings.instance.reloadOnResetFailure)
            {
                ForceReload.RequestBeforeNextPlay("退出 Play 释放失败");
                message += "下次进入 Play 前会自动重新加载脚本；需要立即恢复请点主工具栏的“重载并 Play”。";
            }
            else
            {
                message += "可点主工具栏的“重载并 Play”恢复。";
            }

            Log.Error(message);
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
                Fail(name, e, failures);
            }
        }

        static void Fail(string name, Exception exception, List<string> failures)
        {
            var cause = exception is TargetInvocationException && exception.InnerException != null ? exception.InnerException : exception;
            failures.Add(name);
            Log.Exception($"{name} 执行失败，后续步骤继续执行。", cause);
        }
    }
}
