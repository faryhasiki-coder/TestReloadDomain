# UNITY-05 / 事件、异步与外部工作所有权

补全日期：2026-10-23。当前交付为 **10 个有实际断言的测试方法及其案例源码，未编译、未运行**。下列通过标准是待执行的验收要求，不是通过结果。

## 包名、源码与适配方案

- 类型：本地源码案例，涵盖自有事件、第三方样式事件源、真实编辑器事件和 .NET Task / Thread / Timer。
- 会话适配包：`com.yongshi.easyreloadguard@0.1.0`，使用公开的 `[ResetStatic]`、`[OnExitPlayMode]`、`PlaySession.Token`、`PlaySession.Current`。
- 案例源码：[ExternalWorkCase.cs](../../Assets/DomainReloadCases/Runtime/ExternalWorkCase.cs)。
- 测试入口：[ExternalWorkTests.cs](../../Assets/DomainReloadCases/Tests/Editor/ExternalWorkTests.cs)。
- 共用运行环境管理：[ReloadCaseTestBase.cs](../../Assets/DomainReloadCases/Tests/Editor/ReloadCaseTestBase.cs)。
- 对应总矩阵：[EV-01～03、ASYNC-01～03](../DomainReload-TestMatrix.md)。

事件订阅、Task、Thread、Timer 都由明确的 owner 显式创建并登记；退出 Play 时只释放登记的本案例 owner。静态初始化不会启动线程、计时器或异步工作。Task 取消后检查最终状态；Thread 使用有界 `Join`；Timer 停止调度后等待在途回调排空。清理超时会报告异常，不把引用置空当作工作已经停止。

委托槽采用条件恢复协议：只有槽仍指向自己安装的委托时才允许恢复前任；恢复时跳过已经释放的前任。外部完成源的等待不捕获旧的 Unity 同步上下文；完成后由主线程显式检查 token 和会话 stamp 再提交。

这是这些适配原则的代表实现。`FormatterSlot` 不是 ZString 的真实 formatter 注册接口，`Task` 也不是 UniTask 或其 `AsTask` 语义验证。这里没有安装、修改或宣称完成真实 ZString / UniTask 适配；原 package 用例分别见 [ZString](02-ZString.md) 和 [UniTask](01-UniTask.md)。未修改供应商源码或 Easy Reload Guard 包源码。

## 测试方法与通过标准

R1 表示 Domain Reload 关闭、Scene Reload 开启。标为 R1 的方法均连续进出 Play 3 轮；EditMode-only 方法只验证明确的所有权协议，不冒充跨 Play 验证。当前每项结果均为 **未编译、未运行**。

| 矩阵 ID / 本页项 | 实际测试方法与范围 | 操作步骤 | 通过标准 | 当前结果 |
| --- | --- | --- | --- | --- |
| EV-01 | `OwnedEvent_HasOneCurrentSubscriberAcrossThreeSessions`；R1 × 3 | 每轮进入后确认自有事件为空；订阅一个 owner 并触发一次；退出后再次触发 | 每轮订阅数为 1、调用数为 1；退出 owner 已释放、订阅数归零，再触发不增加旧计数 | 未编译、未运行 |
| EV-02-A | `ExternalEvent_UnsubscribesOnlyItsOwner`；EditMode-only | 在同一第三方样式源注册长期 owner 与临时 owner；触发；临时 owner 连续释放两次；再次触发 | 临时 owner 总计只响应 1 次；长期 owner 响应 2 次；源中保留 1 个订阅，案例 owner 数回基线 | 未编译、未运行 |
| EV-02-B | `EditorEvent_PreservesLongLivedOwnerAcrossThreeSessions`；R1 × 3 | 在真实 `EditorApplication.playModeStateChanged` 上保留编辑器长期 owner；每轮进入后注册 Play 临时 owner，退出时观察 `EnteredEditMode` | 临时订阅必须在 `EnteredEditMode` 前解除，旧调用为零；长期 owner 每轮恰好响应一次；案例 owner 数回每轮基线 | 未编译、未运行 |
| EV-03-A | `FormatterSlot_EarlierOwnerCannotOverwriteReplacement`；EditMode-only | 初始 formatter → A 注册 → B 接管 → A 释放 → B 释放 | A 释放后仍得到 `B:7`；B 释放跳过失效的 A，恢复初始委托身份及 `base:7` 结果 | 未编译、未运行 |
| EV-03-B | `FormatterSlot_ReverseReleaseRestoresLivePredecessor`；EditMode-only | 初始 formatter → A 注册 → B 接管 → B 释放 → A 释放 | B 释放时恢复仍存活的 A，得到 `A:7`；A 再释放后恢复初始委托身份 | 未编译、未运行 |
| ASYNC-01 | `Task_CancellationActuallyTerminatesWorkAcrossThreeSessions`；R1 × 3 | 启动等待取消的真实 Task；确认已开始且未结束；退出 Play | 取消已请求，最终状态确为 `Canceled`，`finally` 恰执行一次；owner 已释放、登记数回基线 | 未编译、未运行 |
| ASYNC-02-A | `Thread_StopsAndRemainsStoppedAcrossThreeSessions`；R1 × 3 | 启动真实 Thread，等到产生工作计数；退出；再观察至少 50 ms | 退出清理在两秒有界等待内结束；`IsAlive=false`、owner 已释放；停止后的计数不再增长，登记数回基线 | 未编译、未运行 |
| ASYNC-02-B | `Timer_DrainsCallbacksAcrossThreeSessions`；R1 × 3 | 启动真实 Timer，等到回调计数增长；退出；再观察至少 50 ms | 停表并在两秒内排空在途回调，`CallbacksDrained=true`；退出后计数不再增长，登记数回基线 | 未编译、未运行 |
| ASYNC-03-A | `LateCompletion_CannotCommitIntoNextSession`；R1 × 3 | 每轮提交一个当前结果，再留下不可取消的外部完成源；在下一轮才完成旧源；第三轮旧源在退出后完成 | 当前结果成功提交；旧结果无论在新会话还是 EditMode 完成都被拒绝；旧 gate 已接受的值保持原值，不写入新会话 | 未编译、未运行 |
| ASYNC-03-B | `ExternalFailure_RemainsFaultedWithOriginalException`；EditMode-only | 向外部完成等待入口提供真实的 `InvalidOperationException` 故障 Task，读取最终结果 | Task 为 `Faulted` 且不是取消；读取时抛出同一异常对象；没有成功提交 | 未编译、未运行 |

## 证据范围与未覆盖项

目前只有源码和断言，尚无编译、测试日志或执行通过证据。后续执行应分别记录每个方法的实际结果，不能用本页新增方法数量改写历史通过数。

- Thread / Timer 的正常有界关闭已有实现；人为卡死、超时故障注入和复杂回调重入尚未实现，不能声称超时分支已经验证。
- EV-02 的第三方源是本地可控模型；真实外部事件新增覆盖 `EditorApplication`，不代表任何第三方 SDK 的具体订阅机制都已适配。
- EV-03 验证委托槽所有权协议，没有验证真实 ZString 的版本接口或其他代码直接覆写槽的所有组合。
- ASYNC-03 的 gate 在主线程提交；没有声明可用于任意后台线程与退出操作并发提交的通用同步协议。
- 本页只覆盖 R1 与注明的 EditMode-only 路径；R0/R2/R3、Player 和 IL2CPP 结果均未获得。
- 图形与 NativeArray / Job 资源见 [资源所有权案例](11-OwnedResources.md)；本页不以 GC 数字替代资源释放证据。
