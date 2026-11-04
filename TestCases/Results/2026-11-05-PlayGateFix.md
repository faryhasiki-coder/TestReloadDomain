# 进入 Play 的 6 个阻断错误修复与验证

日期：2026-11-05。当前分支：`feature/easy-reload-guard`。用户明确确认将本次涉及的样例改为接入后的形态，并运行连续进入和退出 Play 的验证。

## 范围与修改

- Breakout 的 `BallSpawner.Instance`、`PowerUp.Instance` 与 Asteroider 的 `DifficultyManager.Instance`、`UIManager.Instance` 添加静态重置特性；场景重新加载后由原有 Awake 赋值。
- `ScoreLabel.OnDisable` 退订分数事件。
- `ScoreSystem.Shutdown` 提供可重复调用的业务释放入口，解除击杀订阅并恢复计分初始状态；退出 Play 时调用此入口。仅在 Editor 编译的重置方法恢复事件、分数、倍率 1 和初始化标记，不依赖首次进入 Play 时记录初值。
- `EventBus<T>` 添加重置声明；Editor 适配器明确配置业务直接使用的 `EventBus<EnemyKilled>`，避免遗漏闭合泛型。
- 扩展已有计分验收用例，验证禁用显示后退订、重复停止、停止后重新初始化与重复初始化。
- 原有故意保留问题的复现案例、遗留问题清单、包版本和排除配置未修改。其余手写样例与 Voidrunner 未接入。

## 环境与静态检查

- Unity：6000.3.8f1。
- Easy Reload Guard：manifest 与 packages-lock 均固定 `c28f0fa6eeb11c456cafa6bd073011cab523c7cb`。
- Unity Pipeline：0.10.0-exp.1，连接项目路径已核实。
- Domain Reload 关闭，Scene Reload 开启。
- Unity 项目编译通过，最终编译错误为 0；`git diff --check` 通过。
- 检查结果：待处理错误 **6 → 0**，警告 **157 → 155**，提示 0，已知遗留 10。
- 计分的 3 条状态警告消除；新增 1 条泛型自动发现警告：检查器在处理泛型定义特性时未找到派生类形式的闭合类型。该版本在随后应用适配器后仍保留该提示；闭合类型实际已明确登记，3 轮计分验证均从 0 开始且旧击杀订阅为 0。此提示保留，没有通过排除类型或新增遗留条目隐藏。
- 未出现特性用法错误、重置破坏下一次 Play 或重置遗漏字段的新增发现。

## 自动验收

通过 Pipeline 运行 Explicit 的 `ReloadIntegrationSamples.Tests.ScoringAcceptanceTests.EverySessionScoresFromZeroAndOncePerKill`：**1 通过，0 失败**，包含同一脚本加载期间的 3 轮进入和退出 Play。

每轮验证从 0 分开始，一次击杀得到 10 分，双倍后得到 30 分；禁用显示后保持旧文本；停止后击杀不计分；重复停止安全；再次初始化两次后一次击杀仅获得 10 分。

其余 8 个手写样例的验收测试未运行，其涉及的对象池、服务、后台任务、资产状态与 ExecuteAlways 得分仍保持接入前状态。

## Pipeline 连续进入和退出 Play

通过 `editor.playmode start` / `editor.playmode stop` 驱动。每个场景分别连续 3 轮，期间没有修改源码、项目重新编译、脚本域重载或 Editor 重启。9 轮期间 Pipeline 的 codeLoadCount 始终为 4。观测通过临时内存求值完成，没有向 Assets 写入临时脚本。

| 场景 | 轮次 | 关键观测 |
| --- | --- | --- |
| ExecuteAlwaysCase.unity | 1、2、3，每轮均满足 | 临时创建 ScoreLabel，分数 0 → 10 → 30，文本为 30；进入时旧击杀订阅为 0 |
| Breakout/Scenes/SampleScene.unity | 1、2、3，每轮均满足 | BallSpawner 与 PowerUp 的静态引用均为存活对象，且各自与场景内唯一实例一致 |
| Asteroider/Scenes/Game.unity | 1、2、3，每轮均满足 | DifficultyManager 与 UIManager 的静态引用均为存活对象，且各自与场景内唯一实例一致 |

9 轮期间 Console 未新增错误，未出现 Easy Reload Guard 阻止进入 Play 或重置执行错误；存在停止后检查警告。未测试游戏完整玩法、重开按钮或其他未涉及入口。手写场景中的 ExecuteAlways 得分逻辑未修复，也不计为本次通过的项目。

## 停止后检查的限制

相关样例没有被报告为持有已销毁对象或逐轮增长。工程整体报告仍非干净状态：

1. `UnityEditor.AssemblyReloadEvents.m_BeforeAssemblyReloadEvent` 保留目标已销毁的 `UnityEditor.TestTools.TestRunner.EditModeRunner.OnBeforeAssemblyReload` 回调。
2. `SceneManager.sceneUnloaded` 中 `Unity.VisualScripting.ReferenceCollector` 的订阅继续增加；最终报告记录 11 → 12。

最终停止后报告读取 115 个字段、跳过 2 个可能有静态构造副作用的字段、1 个字段无法读取，检查不代表覆盖全部状态。上述编辑器工具问题与剩余静态警告保留，不据此宣称所有接入样例或整个工程验收完成。

## 收尾

- 恢复原 `Assets/Scenes/SampleScene.unity` 场景，编辑器处于停止 Play 状态。
- 恢复后台运行覆盖为关闭，关闭 Pipeline 自动驱动，恢复交互模式。
- Enter Play Mode 设置恢复为原值：选项 1、enabled 为 true，设置资产无脏标记；磁盘 `EditorSettings.asset` 与操作前逐字一致。
- 未创建临时项目脚本，未保存游戏场景或资产，未修改包缓存源码。
