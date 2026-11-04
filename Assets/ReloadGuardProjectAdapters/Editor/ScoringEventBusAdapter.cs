using EasyReloadGuard.Editor;

namespace ReloadGuardProjectAdapters.Editor
{
    /// <summary>补充业务直接使用、无法通过派生类枚举发现的闭合泛型事件总线。</summary>
    internal sealed class ScoringEventBusAdapter : ReloadAdapter
    {
        public override string Target => "ReloadIntegrationSamples";
        public override AdapterTargetKind TargetKind => AdapterTargetKind.Assembly;

        public override void Configure(ReloadAdapterBuilder builder)
        {
            builder.ResetField(
                "ReloadIntegrationSamples.EventBus`1[[ReloadIntegrationSamples.EnemyKilled, ReloadIntegrationSamples]], ReloadIntegrationSamples",
                "s_Handlers");
        }
    }
}
