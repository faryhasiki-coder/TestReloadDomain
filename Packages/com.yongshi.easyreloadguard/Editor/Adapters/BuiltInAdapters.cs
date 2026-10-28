namespace EasyReloadGuard.Editor
{
    // 内置适配器登记已复核版本的结论：SelfHandled 表示该版本包内静态状态经逐条源码复核，关闭 Domain Reload 时不影响 Play 运行逻辑
    // （确定性缓存/常量、仅编辑器路径、包自身在 Play 边界或 owner 生命周期中重置、使用前判空重建、有意保留的设置）；
    // 复核出的例外用 KnownIssue 继续报告。目标升级后检查器报 RG021，需重新复核。
    // 忽略只覆盖包自身：业务代码对包公开静态事件/全局钩子的订阅仍由业务 owner 成对解除，检查器看不到这类订阅。
    // UniTask 2.5.11 不在此列：PlayerLoopHelper.InsertRunner 的 Editor 回调会累积（见 README“已知限制”），应在 UniTask 源码中修复。

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

    /// <summary>依据 17.3.0 源码复核：静态资源随渲染管线创建/Dispose 管理，不随 Play 边界变化；Unity 对象使用前判空重建。</summary>
    internal sealed class CoreRenderPipelineAdapter : ReloadAdapter
    {
        public override string Target => "com.unity.render-pipelines.core";

        public override string[] VerifiedVersions => new[] { "17.3.0" };

        public override void Configure(ReloadAdapterBuilder builder)
        {
            builder.SelfHandled("静态资源（Blitter/XR 材质、CoreUtils/TextureXR 纹理）由管线 Initialize/Cleanup 成对管理或使用前判空重建；" +
                                "RenderGraph/DebugSession 事件成对订阅；其余为 Shader ID、反射缓存与设置。" +
                                "CameraCaptureBridge 在相机已销毁时不移除条目，仅为托管内存增长，不改变渲染结果。");
            builder.KnownIssue("UnityEngine.Rendering.DebugUpdater, Unity.RenderPipelines.Core.Runtime", "s_Instance",
                "启用运行时调试 UI 且安装 Input System 时，每次进入 Play 重建 [Debug Updater] 并调用 EnhancedTouchSupport.Enable()，" +
                "从不调用 Disable；Input System 的启用计数逐会话累加，业务调用一次 Disable() 无法关闭 EnhancedTouch。");
        }
    }

    /// <summary>依据 17.3.0 源码复核：委托/事件成对订阅或由管线构造/Dispose 设置；Unity 对象使用前判空重建或为持久资产。</summary>
    internal sealed class UniversalRenderPipelineAdapter : ReloadAdapter
    {
        public override string Target => "com.unity.render-pipelines.universal";

        public override string[] VerifiedVersions => new[] { "17.3.0" };

        public override void Configure(ReloadAdapterBuilder builder) =>
            builder.SelfHandled("DecalProjector 事件由 SharedDecalEntityManager 按引用计数成对订阅；lightsDelegate 随管线构造/Dispose 设置；" +
                                "静态材质/网格/查找贴图使用前判空重建或随 Renderer2D 构造/Dispose 管理；其余为常量、每帧覆写的缓冲与管线状态。" +
                                "CameraMetadataCache 以相机实例 ID 为键只增不删，仅为托管内存增长，不改变渲染结果。");
    }

    /// <summary>依据 2.0.0 源码复核（UnityEngine.UI 与 TextMeshPro）。</summary>
    internal sealed class UGuiAdapter : ReloadAdapter
    {
        const string k_TextMeshPro = "Unity.TextMeshPro";
        const string k_UGui = "UnityEngine.UI";

        public override string Target => "com.unity.ugui";

        public override string[] VerifiedVersions => new[] { "2.0.0" };

        public override void Configure(ReloadAdapterBuilder builder)
        {
            builder.SelfHandled("单例与静态构造只订阅一次 Canvas.willRenderCanvases；内置材质/纹理/Shader/Settings 使用前判空重建；" +
                                "注册表随组件 OnEnable/OnDisable 成对维护；其余为确定性缓存与常量。" +
                                "TMP_ResourceManager 的字体字典只增不删，查询方法在包内无调用方，仅为内存增长。" +
                                "TMP_Text.OnFontAssetRequest/OnSpriteAssetRequest/OnMissingCharacter 与 TMPro_EventManager 是公开扩展点，" +
                                "业务订阅须成对解除。");
            builder.KnownIssue($"TMPro.MaterialReferenceManager, {k_TextMeshPro}", "s_Instance",
                "富文本 <sprite>/<material>/<font> 引用的资产缓存跨会话保留且无重置：引用的是会话内加载后卸载（AssetBundle/Addressables）" +
                "或运行时创建后销毁的资产时，下一会话直接取到已销毁对象，请求回调不再被调用（字体条目也不会被替换）。持久资产不受影响。");
            builder.KnownIssue($"UnityEngine.EventSystems.EventSystem, {k_UGui}", "s_UIToolkitOverrideConfigOld",
                "调用已废弃的 EventSystem.SetUITookitEventSystemOverride 后，覆盖配置跨会话保留，下一会话的 EventSystem 在 OnEnable 中沿用。");
            builder.KnownIssue($"UnityEngine.UIElements.PanelEventHandler, {k_UGui}", "s_Modifiers",
                "UGUI EventSystem 驱动 UI Toolkit 面板时，上一会话最后的修饰键（如 Ctrl+P 停止）会带到下一会话的第一个指针/导航事件，约 1 帧后恢复。");
            builder.KnownIssue($"UnityEngine.UIElements.PanelRaycaster, {k_UGui}", "panelPicker",
                "ScreenOverlayPanelPicker 实现在引擎内，未能确认无跨会话状态。");
            builder.KnownIssue($"UnityEngine.UIElements.WorldDocumentRaycaster, {k_UGui}", "worldPicker",
                "PhysicsDocumentPicker 实现在引擎内，未能确认无跨会话状态。");
        }
    }

    /// <summary>依据 1.9.9 源码复核：EventBus/GraphInstances 随 Machine 生命周期成对维护，变量资产每次 Play 重新克隆。</summary>
    internal sealed class VisualScriptingAdapter : ReloadAdapter
    {
        public override string Target => "com.unity.visualscripting";

        public override string[] VerifiedVersions => new[] { "1.9.9" };

        public override void Configure(ReloadAdapterBuilder builder) =>
            builder.SelfHandled("EventBus 由 ScriptMachine OnEnable/OnDisable 成对注册；GraphInstances 随 Machine Awake/OnDestroy；" +
                                "Saved/Application 变量判空重建并每次 Play 由 RuntimeInitialize 重新克隆；其余为编辑器绑定、反射缓存与常量。" +
                                "ReferenceCollector 每次 Play 向 SceneManager.sceneUnloaded 追加一个幂等处理器、Singleton.awoken 只增不删，仅为内存增长。" +
                                "业务直接调用 EventBus.Register 时须成对 Unregister。");
    }

    /// <summary>依据 1.8.9 源码复核：静态事件仅对编辑器可见并成对解除，预览对象仅编辑器使用并判空重建。</summary>
    internal sealed class TimelineAdapter : ReloadAdapter
    {
        public override string Target => "com.unity.timeline";

        public override string[] VerifiedVersions => new[] { "1.8.9" };

        public override void Configure(ReloadAdapterBuilder builder) =>
            builder.SelfHandled("internal 静态事件只由编辑器侧 SignalManager/TimelineWindow 成对订阅；预览驱动与默认动画仅编辑器使用并判空重建；其余为常量与缓存。");
    }

    /// <summary>依据 1.8.26 源码复核：编辑器编译事件在 InitializeOnLoad 中订阅一次，其余为函数指针与常量缓存。</summary>
    internal sealed class BurstAdapter : ReloadAdapter
    {
        public override string Target => "com.unity.burst";

        public override string[] VerifiedVersions => new[] { "1.8.26" };

        public override void Configure(ReloadAdapterBuilder builder) =>
            builder.SelfHandled("编译进度/性能事件只在 BurstLoader 静态构造中订阅一次；Cecil 依赖仅编辑器可用；其余为函数指针缓存、常量与设置。");
    }

    /// <summary>依据 2.6.2 源码复核：自定义分配器委托由 owner 成对 Register/Unregister，其余为 Burst 函数指针与查找表。</summary>
    internal sealed class CollectionsAdapter : ReloadAdapter
    {
        public override string Target => "com.unity.collections";

        public override string[] VerifiedVersions => new[] { "2.6.2" };

        public override void Configure(ReloadAdapterBuilder builder) =>
            builder.SelfHandled("AllocatorManager 的 TryFunctionDelegates 与原生表由分配器 owner 成对 Register/Unregister（业务自定义分配器须自行 Unregister）；" +
                                "其余为 Burst 惰性函数指针缓存与只读查找表。");
    }

    /// <summary>依据 2.0.9 源码复核：组件注册表 OnEnable/OnDisable 成对维护，并在 BeforeSceneLoad 清空。</summary>
    internal sealed class AiNavigationAdapter : ReloadAdapter
    {
        public override string Target => "com.unity.ai.navigation";

        public override string[] VerifiedVersions => new[] { "2.0.9" };

        public override void Configure(ReloadAdapterBuilder builder) =>
            builder.SelfHandled("NavMeshSurface/Modifier/ModifierVolume/Link 的注册表 OnEnable/OnDisable 成对维护，" +
                                "并由 RuntimeInitializeOnLoadMethod(BeforeSceneLoad) 清空。");
    }

    /// <summary>依据 3.2.2（Json.NET 13.0.2）上游源码复核：静态字段为惰性反射缓存与只读表。</summary>
    internal sealed class NewtonsoftJsonAdapter : ReloadAdapter
    {
        public override string Target => "com.unity.nuget.newtonsoft-json";

        public override string[] VerifiedVersions => new[] { "3.2.2" };

        public override void Configure(ReloadAdapterBuilder builder) =>
            builder.SelfHandled("静态字段为 _init 守卫下的惰性反射缓存与只读表；JsonConvert.DefaultSettings 是全局配置钩子，库内只读，业务写入时由业务负责。");
    }

    /// <summary>依据 10.0.0 源码复核：Runtime 程序集只被包内 Editor 工具调用，字段为一次性反射探测缓存。</summary>
    internal sealed class UnityMcpAdapter : ReloadAdapter
    {
        public override string Target => "com.coplaydev.unity-mcp";

        public override string[] VerifiedVersions => new[] { "10.0.0" };

        public override void Configure(ReloadAdapterBuilder builder) =>
            builder.SelfHandled("MCPForUnity.Runtime 只被包内 Editor 工具调用，不参与游戏 Play 逻辑；静态字段为一次性反射探测缓存。");
    }
}
