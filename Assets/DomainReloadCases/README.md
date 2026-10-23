# Domain Reload 案例

验证 Easy Reload Guard（`Packages/com.yongshi.easyreloadguard`）的正式案例。复现组和修复组分开管理：复现组故意保留问题，不要为它加入重置逻辑。

| 项 | 值 |
| --- | --- |
| Unity | 6000.3.1f1 |
| 入口 | `Tests/Editor/PlayReentryTests.cs`、`Tests/Editor/PlayReentryTests.UniTask.cs`（同一个 partial 类，EditMode，`DomainReloadCases.Tests`） |
| 运行方式 | `Tools/UnityCI/Run-UnityTests.ps1 -TestFilter DomainReloadCases`，或在 Test Runner 中运行。在编辑器内运行时，需要本域尚未进入过 Play（刚编译或刚重载） |
| 操作 | 每组在同一次测试中连续 Play→Stop 3 轮，不改代码、不重新编译。每轮新建一个案例组件，并输出一次探针日志，统计挂在 `Application.logMessageReceived` 上的处理器数量 |
| 设置 | 测试运行时临时修改 Enter Play Mode Options，结束后恢复原值（原值保存在 SessionState，域重载后依然有效） |

## 案例

| 用例 | 类型 | Domain Reload | Scene Reload | 期望 |
| --- | --- | --- | --- | --- |
| `Fix_GuardedStateIsResetEachSession`（Order 1） | 修复验证：`GuardedCaseCounter` | 关闭 | 开启 | 每轮计数/集合/事件订阅/外部处理器都是 1；`InitialValue` 字段恢复为 `initial`；上一会话的 Token 已取消、标记失效；退出后外部处理器残留为 0 |
| `Fix_UniTaskWorkEndsWithSession`（Order 2） | 修复验证：`GuardedUniTaskLoader` | 关闭 | 开启 | 见下方 UniTask 一节 |
| `Repro_LeakyStateAccumulatesAcrossSessions`（Order 3） | 复现：`LeakyCaseCounter` | 关闭 | 开启 | 第 k 轮各项均为 k（逐轮累积） |
| `Repro_UniTaskWorkLeaksAcrossSessions`（Order 4） | 复现：`LeakyUniTaskLoader` | 关闭 | 开启 | 见下方 UniTask 一节 |
| `Control_DomainReloadEnabledStartsFresh`（Order 5） | 对照：`LeakyCaseCounter` | 开启 | 开启 | 每轮各项都是 1 |
| `Control_UniTaskDomainReloadEnabledStartsFresh`（Order 6） | 对照：`LeakyUniTaskLoader` | 开启 | 开启 | 见下方 UniTask 一节 |

用 Order 固定顺序的原因：对照组会重载域。如果任何关闭 Domain Reload 的用例排在对照组之后，它进入 Play 时域已经运行过 Play，而 `InitialValue` 还没记录初值，关口会按规则强制重载，打断测试。NUnit 3.5 的 `Order` 只能加在方法上，无法跨类排序，所以 UniTask 用例与原有用例写在同一个 partial 类中。

## UniTask 案例（`com.cysharp.unitask` 2.5.11，嵌入式包）

依据 2.5.11 源码 `Runtime/PlayerLoopHelper.cs`：

- 关闭 Domain Reload 时，`Init`（AfterAssembliesLoaded）每次进入 Play 都会重建 runner 和 continuation 队列；`EnteredEditMode`/`ExitingEditMode` 时，先执行一次旧队列，再清空。
- 因此，只依赖 PlayerLoop 的等待不会被带进下一个会话，但被丢弃的状态机不会继续执行，其中的 `finally` 永远不会运行。
- 线程池工作如果跨会话才完成，`RunOnThreadPool` 在 finally 中调用 `UniTask.Yield()`，经 `AddContinuation` 进入的是**当前**静态队列，也就是新会话的队列，于是旧结果被写入新会话。
- `InsertRunner` 每次初始化都会向 `EditorApplication.playModeStateChanged` 追加一个闭包（每个 timing 一个），而且从不移除。关闭 Domain Reload 时，这些闭包逐轮累积，每个都持有已清空的旧 runner 和队列。这是 UniTask 自身的残留，内置 `UniTaskAdapter` 的 `SelfHandled` 依据没有覆盖到这一点。本工程只记录这一现象，不修复。

| 写法 | 复现：`LeakyUniTaskLoader`（不传令牌） | 修复：`GuardedUniTaskLoader`（owner 为 Play 会话） |
| --- | --- | --- |
| 线程池加载后回写 | `await UniTask.RunOnThreadPool(work)` 后直接写结果 | 把 `PlaySession.Token` 传给工作本身和 `RunOnThreadPool`，回写前再判断 `PlaySessionStamp.IsAlive` |
| 占用资源，在 finally 中释放 | `await UniTask.Delay(duration)` | `UniTask.Delay(..., PlaySession.Token, cancelImmediately: true)`；退出 Play 时同步结束，执行 finally |
| 资源计数 | 未处理（RG003，已入基线） | `[ReloadSafe]`：计数由 finally 成对维护，重置反而会掩盖未释放的资源 |

后台工作阻塞在测试持有的闸门（`ManualResetEventSlim`）上，用来模拟慢速 IO，完成时机由测试决定。资源占用使用 10 分钟的 Delay，测试期间不会自然结束。期望如下：

- 修复（Order 2）：每轮在会话内完成一次加载，`completedLoads=1`；再启动一个阻塞的加载和一份资源占用后退出。退出后 `heldResources=0`，阻塞的工作随令牌取消结束（`blockedWorkers=0`），下一轮开始时没有旧结果写入。
- 复现（Order 4）：第 k 轮开始时，上一轮的工作仍阻塞在线程池中；放开闸门后，上一轮的加载写入第 k 个会话。`heldResources` 逐轮为 k；UniTask 的 `playModeStateChanged` 处理器逐轮增加同样的数量。
- 对照（Order 6）：每轮 `heldResources=1`，UniTask 处理器数量每轮相同（finally 同样没有执行，只是残留随旧域一起被丢弃）。后台工作阻塞的线程会跨域重载，可能拖住卸载，所以对照组不启动后台工作。
- 处理器统计使用 6000.3.1f1 的内部结构 `EditorApplication.m_PlayModeStateChangedEvent.m_Delegate`（`length` / `GetDelegateAt`）。读取失败时测试会直接失败并提示复核，不会静默跳过。
- 每个用例结束时（TearDown）放开闸门，并等待后台工作退出，避免阻塞的线程留到后续用例或域重载中。

**验证状态：未验证。** 截至 2026-10-22 只做了静态检查，尚未编译，也尚未运行 Test Runner 或人工连续 Play→Stop。

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
