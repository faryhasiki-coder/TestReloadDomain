# 可直接检查的接入示例

- `SessionExamples.cs`：新字段只加 `[ResetOnPlay]`；集合/事件集中 Reset；原有 `LegacyBus` 保持独立，适配器单独覆盖其静态状态。
- `InputSystemAdapter.cs`：复用已安装的 Input System 1.20.1，自身包状态声明 SelfManaged；项目创建的 InputAction 用 scope 管理。
- `Tests/Editor/PlaySessionIntegrationTests.cs`：真实进入/退出 Play，正常重载与连续三次 Fast Play；测试后恢复原始 Editor 设置。

运行 Test Runner 的 `PlaySessionDemo.Tests` 查看结果。进入 Play 后，可在代码中调用 `Wallet.CollectCoin()`、`LegacyBus.Publish()`；再次进入 Play 应分别恢复为 0，并且只有一次有效订阅。场景不需要添加管理对象，不修改 SampleScene。

交互入口是 `Tools > Play Sessions > Monitor`，可查看状态、运行检查和强制重载。完整接入说明在包 README。
