# Easy Reload Guard 接入样例

接入**前**的实战代码，用来给 `com.yongshi.easyreloadguard` 做接入测试。本目录的样例不引用该包，源码中也不标注问题位置。请在单独分支或工程副本中做接入练习，主线保持接入前状态。

- `Runtime/`：程序集 `ReloadIntegrationSamples`。包含计分与事件总线、泛型单例、对象池、服务注册、ScriptableObject、async 自动存档、Timer 心跳、档案加载、ExecuteAlways 计分板。
- `Tests/Editor/`：程序集 `ReloadIntegrationSamples.Tests`，共 9 个接入验收测试，分类为 `ReloadIntegration`，标记 `Explicit`。在 Domain Reload 关闭、Scene Reload 开启的条件下连续 3 轮 Play→Stop，要求每局行为都与第一局一致，且退出后后台工作停止。**接入前预期失败**，需在 Test Runner 中手动选中运行，并从刚编译或刚重载的域开始。
- `ExecuteAlways/`：人工连续 Play→Stop 用的场景和 Prefab。
- `Games/`：从 GitHub 导入的三个开源小游戏（Breakout、Asteroider、Voidrunner），均为 Unity 6000.3.x、MIT 许可证，原版源码未修改。各自带上游 `LICENSE.txt` 和独立 asmdef。

适用 Unity 6000.3.x，不限定小版本。接入流程、各用例的通过标准，以及用于事后评估的问题点清单，见 [INT 接入样例](../../TestCases/Cases/06-IntegrationSamples.md)。

**验证状态：未编译、未运行。**
