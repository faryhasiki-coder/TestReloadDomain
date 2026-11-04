# Easy Reload Guard 接入样例

用于给 `com.yongshi.easyreloadguard` 做接入测试的实战代码。原始版本为接入前状态，应保留作对照；当前分支于 2026-11-05 经用户确认，修复了计分与事件总线、Breakout 和 Asteroider 的 6 个进入 Play 阻断错误。这三个运行时程序集现已引用该包，其余手写样例仍为接入前状态。后续练习应在单独分支或工程副本进行。

- `Runtime/`：程序集 `ReloadIntegrationSamples`。包含计分与事件总线、泛型单例、对象池、服务注册、ScriptableObject、async 自动存档、Timer 心跳、档案加载、ExecuteAlways 计分板。
- `Tests/Editor/`：程序集 `ReloadIntegrationSamples.Tests`，共 9 个接入验收测试，分类为 `ReloadIntegration`，标记 `Explicit`。在 Domain Reload 关闭、Scene Reload 开启的条件下连续 3 轮 Play→Stop，要求每局行为都与第一局一致，且退出后后台工作停止。**接入前预期失败**，需在 Test Runner 中手动选中运行，并从刚编译或刚重载的域开始。
- `ExecuteAlways/`：人工连续 Play→Stop 用的场景和 Prefab。
- `Games/`：从 GitHub 导入的三个开源小游戏（Breakout、Asteroider、Voidrunner），均为 Unity 6000.3.x、MIT 许可证。Breakout 与 Asteroider 的 4 个单例已添加重置特性；Voidrunner 保持原版。各自带上游 `LICENSE.txt` 和独立 asmdef。

适用 Unity 6000.3.x，不限定小版本。接入流程、各用例的通过标准，以及用于事后评估的问题点清单，见 [INT 接入样例](../../TestCases/Cases/06-IntegrationSamples.md)。

**本次修改的验证：** Unity 6000.3.8f1 编译通过；计分验收测试通过；手写样例场景、Breakout、Asteroider 各通过 Pipeline 连续进入和退出 Play 3 轮的相关入口检查（Domain Reload 关闭、Scene Reload 开启）。检查中的待处理错误由 6 降至 0，警告为 155；停止后仍存在编辑器工具的残留。其余 8 个手写验收用例与完整玩法未验证，不能据此认为整个目录已完成接入。详见 [本次验证记录](../../TestCases/Results/2026-11-05-PlayGateFix.md)。
