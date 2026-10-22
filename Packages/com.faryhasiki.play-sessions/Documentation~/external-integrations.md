# UPM 与 DLL 的接入边界

## 独立适配程序集

把适配器放在项目 `Assets/Integrations/Vendor/`，或自行维护的 integration UPM 包。其 asmdef 只引用本包 Core 和供应商运行时程序集。供应商不存在时，移除 integration 程序集，或用 asmdef `versionDefines` + `defineConstraints` 控制编译；仅设置 `Optional = true` 不能解决缺失的 C# 程序集引用。

```csharp
[assembly: PlaySessionAdapter(typeof(VendorAdapter))]
[assembly: PlaySessionCompatibility(
    DependencyKind.Package, "com.vendor.sdk", "2.4.1",
    DependencyHandling.Adapter,
    "Vendor public Reset/Shutdown API; repeated Play regression 2026-10-22.",
    ModuleId = "vendor.sdk")]
```

适配器通过 `plan.Add("vendor.sdk", reset, start, validate)` 注册，版本不符、缺少模块、重复声明、缺少证据时阻断 Fast Play。版本号需精确匹配，升级后重新测试再修改声明。对于 Git/local/embedded 依赖，同版本源码仍可能变化，应额外在 validate 中校验 API/内容或锁定提交；只检查版本不能捕捉所有变更。

供应商已正确支持无 Domain Reload 时，用 `SelfManaged` 并写下版本和代码/文档依据，不再重复清理供应商内部状态。**你注册到供应商的回调、你创建的实例仍由项目负责释放。**

本工程使用已有 Input System 1.20.1 验证这个边界：包的 `InputSystemEditorInitializer` 已包含无 Domain Reload 初始化、离开 Play 和恢复 Edit 状态的处理；项目示例仍单独拥有 InputAction、解绑回调并 Dispose。当前测试不构成 Input System 全 API 兼容性认证。

## 托管 DLL

优先级依次是：公开 Reset/Dispose/Shutdown API → 供应商支持的可重建实例 → 要求完整 Domain Reload。不得自动改私有 readonly 字段或伪造静态构造器重跑。

```csharp
[assembly: PlaySessionCompatibility(
    DependencyKind.ManagedAssembly, "Vendor.Runtime", "1.0.0.0",
    DependencyHandling.Adapter, "Public Reset and Shutdown verified.",
    ModuleId = "vendor.dll", Sha256 = "replace-with-reviewed-file-sha256")]
```

对 DLL 同时检查 AssemblyVersion 与文件 SHA256，避免程序集版本没变但二进制已换。可用 `Get-FileHash -Algorithm SHA256` 获取本地二进制哈希；修改该值表示一次重新评审，不应自动跟随文件变化。

如果无法接管状态，声明 `FullReloadRequired`；Fast Play 会阻断，引导启用 Reload Domain。单次脚本重载只能清理已有状态，不能让之后的连续 Fast Play 自动变安全。

Tests 中包含真实编译的 `PlaySessions.LegacyFixture.dll`：只有标准库引用，源文件和重建脚本放在 `Tests/Fixtures~/`。测试通过公开 API 做外部适配，连续运行 20 个核心会话。它是受控夹具，不是对未知商业 DLL 的承诺。测试 DLL 仅 Editor、显式引用，不进入 Player。

## 原生 DLL / 混合 SDK

域重载不会重启进程，也不能保证原生全局变量、设备句柄、线程和回调被重建。使用 SDK 自己的 Initialize/Shutdown，严格验证调用线程、顺序和返回值。没有可靠清理接口时，通过托管包装程序集声明 `EditorRestartRequired`，并为 native PluginImporter 保留人工评审诊断 `PS211`；无托管包装的 native 插件同样会被报告。

`EditorRestartRequired` 阻断 Fast Play；允许重启后的第一次正常 Play，进入后会在 Editor SessionState 留下重启要求，后续正常/快速 Play 都会阻断，脚本重载不能清除，只能关闭并重开编辑器。声明不能覆盖进入 Play 前已被其他 Editor 插件污染的进程状态，首次接入也应先重启。

工具包不自动重启编辑器、不卸载任意原生模块、不强杀线程。原生清理需要项目或供应商的真实回归测试；严格依赖覆盖模式下，未能证明安全的原生插件保持阻断。

## 回归清单

1. 记录 Unity 版本、包版本/提交、DLL 哈希及 API 依据。
2. 正常 Reload 与连续至少三次 Fast Play：初始化状态一致。
3. 订阅、对象、句柄及后台任务没有累积，退出后关闭。
4. 在第二次初始化/清理阶段注入失败，确认提示和阻断有效。
5. 升级包或替换 DLL：旧声明应报版本/哈希不符。
6. 原生 SDK 独立验证进程级恢复；不能用托管夹具结果替代。
