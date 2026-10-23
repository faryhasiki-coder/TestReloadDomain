# Domain Reload 案例

验证 Easy Reload Guard（`Packages/com.yongshi.easyreloadguard`）的正式案例。复现组和修复组分开管理：复现组故意保留问题，不要为它加入重置逻辑。

2026-10-23 已补代表性源码与测试规格，入口为 [Domain Reload 完整矩阵](../../TestCases/DomainReload-TestMatrix.md)。本次未编译、未运行测试、未操作 Unity；新增代码不计入历史通过数。当前 ProjectVersion 目标为 **6000.3.8f1**；下表的旧版本与后文运行结果保留为历史记录。

## 新增源码案例（2026-10-23，全部未编译、未运行）

| 案例规格 | 测试源码（均位于 Tests/Editor） | 方法数 |
| --- | --- | --- |
| [静态状态与状态树](../../TestCases/Cases/07-StateOwnership.md) | [StateOwnershipTests.cs](Tests/Editor/StateOwnershipTests.cs) | 6 |
| [对象字段与生命周期](../../TestCases/Cases/08-ObjectLifetime.md) | [ObjectLifetimeTests.cs](Tests/Editor/ObjectLifetimeTests.cs) | 3 |
| [ScriptableObject](../../TestCases/Cases/09-ScriptableObject.md) | [ScriptableStateTests.cs](Tests/Editor/ScriptableStateTests.cs) | 2 |
| [事件与后台工作](../../TestCases/Cases/10-ExternalWork.md) | [ExternalWorkTests.cs](Tests/Editor/ExternalWorkTests.cs) | 10 |
| [原生与 Unity 资源](../../TestCases/Cases/11-OwnedResources.md) | [NativeJobOwnershipTests.cs](Tests/Editor/NativeJobOwnershipTests.cs)、[OwnedResourceTests.cs](Tests/Editor/OwnedResourceTests.cs) | 3 |
| [初始化与 Editor 生命周期](../../TestCases/Cases/12-Initialization.md) | [InitializationOrderTests.cs](Tests/Editor/InitializationOrderTests.cs)、[EditorLifecycleTests.cs](Tests/Editor/EditorLifecycleTests.cs) | 4 |
| [四种重载组合](../../TestCases/Cases/13-ReloadModes.md) | [ReloadModeMatrixTests.cs](Tests/Editor/ReloadModeMatrixTests.cs) | 4（2 个 Explicit） |

共 32 个新增测试方法，不含此前的 PlayReentry 与 ExecuteAlways。共用 [ReloadCaseTestBase.cs](Tests/Editor/ReloadCaseTestBase.cs) 在未来执行时保存/恢复环境；临时资产使用测试独占目录，结束后由所属用例清理。已有 InitialValue 案例的干净域前提仍适用；本次未通过改变设置、编译或强制重载来满足它。仅为 NativeArray 案例在测试 asmdef 引用工程已安装的 `Unity.Collections`，未安装新包。

| 项 | 值 |
| --- | --- |
| Unity（原记录） | 6000.3.1f1；不能作为本次目标版本 |
| 入口 | `Tests/Editor/PlayReentryTests.cs`（EditMode，`DomainReloadCases.Tests`） |
| 运行方式 | `Tools/UnityCI/Run-UnityTests.ps1 -TestFilter DomainReloadCases`，或在 Test Runner 中运行。在编辑器内运行时，需要本域尚未进入过 Play（刚编译或刚重载） |
| 操作 | 每组在同一次测试中连续 Play→Stop 3 轮，不改代码、不重新编译。每轮新建一个案例组件，并输出一次探针日志，统计挂在 `Application.logMessageReceived` 上的处理器数量 |
| 设置 | 测试运行时临时修改 Enter Play Mode Options，结束后恢复原值（原值保存在 SessionState，域重载后依然有效） |

## 案例

| 用例 | 类型 | Domain Reload | Scene Reload | 期望 |
| --- | --- | --- | --- | --- |
| `Fix_GuardedStateIsResetEachSession`（Order 1） | 修复验证：`GuardedCaseCounter` | 关闭 | 开启 | 每轮计数/集合/事件订阅/外部处理器都是 1；`InitialValue` 字段恢复为 `initial`；上一会话的 Token 已取消、标记失效；退出后外部处理器残留为 0 |
| `Repro_LeakyStateAccumulatesAcrossSessions`（Order 2） | 复现：`LeakyCaseCounter` | 关闭 | 开启 | 第 k 轮各项均为 k（逐轮累积） |
| `Control_DomainReloadEnabledStartsFresh`（Order 3） | 对照：`LeakyCaseCounter` | 开启 | 开启 | 每轮各项都是 1 |

用 Order 固定顺序的原因：对照组会重载域。如果修复组排在后面，它首次进入 Play 时域已经运行过 Play，而 `InitialValue` 还没记录初值，关口会按规则强制重载，打断测试。

## 补充的 ExecuteAlways 用例规格（2026-10-23，本次未执行）

| ID | 设计内容 | 已有实现 / 待实现 |
| --- | --- | --- |
| EA-01 | 编辑态真实回调与游戏逻辑隔离 | `ExecuteAlwaysTests.EditorPreviewDoesNotExecuteGameplay`；参数变化的预览行为待扩展 |
| EA-02 | 启用、禁用、销毁与外部订阅 | `ExecuteAlwaysTests.EnableDisableAndDestroyBalanceSubscriptions` |
| EA-03 | 编辑态再次污染后，连续 Fast Play 的进入重置 | `ExecuteAlwaysTests.EditPollutionIsResetAcrossThreeFastPlays` |
| EA-04 | Play 与 Prefab Stage 共存、静态 owner 和资产隔离 | `ExecuteAlwaysTests.PrefabStageCannotMutatePlayingWorld` |
| EA-05 | 正常 Domain Reload 对照 | `ExecuteAlwaysTests.NormalDomainReloadProvidesCleanBaseline` |
| EA-06 | InitialValue 首次观察值与源码业务初值的差别 | 待实现 |
| EA-07 | inactive 对象、场景/Prefab 关闭重开与预览资源 | 待实现 |
| EA-08 | ExecuteInEditMode 的行为与迁移对照 | 待实现 |

前置条件、步骤、通过标准和实际路径集中在 [ExecuteAlways 案例页](../../TestCases/Cases/06-ExecuteAlways.md)。只登记源码存在；不将此前部分通过/超时记录写成本次验收。普通 PlayReentry 每轮新建组件的结果，也不能替代这些编辑世界和复用实例用例。

## 实际结果（2026-10-22，批处理流水线，CI 镜像工程）

```
修复（Domain Reload=关闭）  第1轮 session=1 created=1 instances=1 tickedSubscribers=1 logHandlers=1 label=initial
                            第2轮 session=2 created=1 instances=1 tickedSubscribers=1 logHandlers=1 label=initial
                            第3轮 session=3 created=1 instances=1 tickedSubscribers=1 logHandlers=1 label=initial
复现（Domain Reload=关闭）  第1/2/3轮 created=instances=tickedSubscribers=logHandlers=1/2/3
对照（Domain Reload=开启）  第1/2/3轮 各项均为 1
```

- 检查器只检出复现案例的 4 个问题（RG001×1、RG003×2、RG004×1），已写入 `ProjectSettings/EasyReloadGuardBaseline.txt`；修复案例没有任何发现项。
- 强制重载链路用一次性脚本在 CI 镜像中验证，脚本已删除。验证项包括：编辑器请求、运行时请求、手动“重载域并进入 Play”都换成了新域；没有请求时不重载；在 ExitingEditMode 中取消进入 Play 有效。
- 用户打开的编辑器中的实测见下一节。

## 实际结果（2026-10-22，用户打开的编辑器，通过 com.unity.pipeline 驱动）

- `run_tests`：`EasyReloadGuard.Editor.Tests` 24/24、`DomainReloadCases.Tests` 3/3 通过。对照组会重载域，之后 Pipeline 的 `test_status` 一直停在 `running`，结果以 Unity 写出的 `TestResults.xml` 为准。
- 不经过测试框架，人工连续 Play→Stop 3 轮：用 `editor_play` / `editor_stop` 驱动，每轮通过 `eval` 新建两个案例组件，再输出一次探针日志。Domain Reload 关闭，Scene Reload 开启，中途不改代码、不重新编译：

  ```
  第1轮 session=1 Leaky 1/1/1/1 Guarded 1/1/1/1 label=initial stampAlive=True
  第2轮 session=2 Leaky 2/2/2/2 Guarded 1/1/1/1 label=initial（上一轮已改为 changed）
  第3轮 session=3 Leaky 3/3/3/3 Guarded 1/1/1/1 label=initial
  ```

  （Leaky / Guarded 后的四个数依次为 created / instances / tickedSubscribers / logHandlers。）
- 强制重载：调用 `ForceReload.RequestBeforeNextPlay` 后进入 Play，关口先取消进入，重载后自动进入 Play，用时 5.3 秒；会话号和 Leaky 计数都回到 1。下一轮恢复为无重载进入（0.3 秒）。
- 关口规则“本域已运行过 Play，InitialValue 需要在干净的域中记录初值”在实测中按预期触发。
- 实测期间暴露了两个问题，均已修正：
  1. 编辑器在后台时 `delayCall` 不执行，导致“重载并进入 Play”卡住；工具包已改为在下一次 update 中执行。
  2. Unity 6.3 会把 Enter Play Mode 设置规范化，旧版测试的恢复顺序把 enabled 写成了 false；测试改为先写 options、再写 enabled，并对原先未修改的资源清除脏标记。
- 实测前后均核对过：设置的生效值为两项重载都开启，`EditorSettings.asset` 不是脏状态，磁盘文件仍为 `1/0`；临时调整的交互模式（不限速）已恢复原值。
