namespace EasyReloadGuard.Editor
{
    // 内置适配器只登记“目标已自行处理”的结论与已复核版本；目标升级后检查器提示重新复核。

    /// <summary>
    /// 依据 2.5.11 源码 Runtime/PlayerLoopHelper.cs Init：检测 DisableDomainReload 后重建 PlayerLoop runner 与 continuation 队列。
    /// </summary>
    internal sealed class UniTaskAdapter : ReloadAdapter
    {
        public override string Target => "com.cysharp.unitask";

        public override string[] VerifiedVersions => new[] { "2.5.11" };

        public override void Configure(ReloadAdapterBuilder builder) =>
            builder.SelfHandled("PlayerLoopHelper.Init 在关闭 Domain Reload 时重建 runner 与 continuation 队列；" +
                                "这只移除调度引用，业务 await 仍需传递取消令牌（可用 PlaySession.Token）。");
    }

    /// <summary>
    /// 依据 1.17.0 源码 InputSystem.OnPlayModeChange：在 Play 边界同步设备、重置 InputUser 与 ActionState。
    /// </summary>
    internal sealed class InputSystemAdapter : ReloadAdapter
    {
        public override string Target => "com.unity.inputsystem";

        public override string[] VerifiedVersions => new[] { "1.17.0" };

        public override void Configure(ReloadAdapterBuilder builder) =>
            builder.SelfHandled("InputSystem.OnPlayModeChange 在 Play 边界同步设备并重置 InputUser/ActionState；" +
                                "不清除 InputAction 的 started/performed/canceled 回调，业务订阅仍须由业务 owner 解除。");
    }
}
