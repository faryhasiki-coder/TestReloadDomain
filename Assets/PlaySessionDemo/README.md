# Play Sessions 演示

## 先看最小源码对比：捡金币

入口：`Tools > Play Sessions > Demo > Open Coin Code Comparison`。

两边执行相同的 `CollectCoin() => Coins++`。在当前 Domain Reload 关闭、Scene Reload 开启的设置下，拾取几枚金币，再停止并再次 Play：未接入版保留上次值，接入版从 0 开始。Game View 同时展示代码和数值。

具体增加了哪些代码、为何需要这些代码，见 [源码差异说明](Runtime/CodeComparison/README.md)。

## 原有完整生命周期演示

入口：`Tools > Play Sessions > Demo > Open Demo Scene`。场景：`Assets/PlaySessionDemo/Scenes/PlaySessionDemo.unity`。

1. 使用 `Enable Fast Play (Domain Reload Off)`，打开 Monitor，然后 Play。
2. 初始受管计数为 0，事件订阅 1、演示资源 1。左侧对照值会在本次脚本域中继续累加。
3. 点击“发送一个事件”：两侧各加 1，受管状态不应因重复订阅一次增加多次。
4. 点击“启动 2 秒后台任务”：正常等待会提交一次；任务中停止 Play 会取消，不写回退出的会话。
5. “有效工具请求”增加通过次数，“过期工具请求”只增加拒绝次数，不执行动作。
6. 停止：Monitor 最终显示 Stopped，资源/任务均为 0；事件订阅与演示资源均回到 0。
7. 再次 Play：受管计数重新为 0，订阅/资源仍各为 1，左侧对照继续累加。

静态归属声明与适配代码集中在 `Runtime/DemoComposition.cs`。`DemoView.cs` 仅展示状态和交互，不包含清理框架。

`DemoRaw` 的持久化声明仅用于故意展示未重置效果；`DemoSignals` 模拟跨 Play 事件发布端。它们不是第三方开源 package，不能用于声明简易验证通过。

测试：在 Test Runner 的 EditMode 下执行 `PlaySessionDemo.Tests`；测试会临时配置 Domain Reload 关闭，结束后恢复原设置。自动化核对与用户对修改量/代码质量的评估分开。
