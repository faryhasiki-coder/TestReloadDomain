# PKG-01 / UniTask 独立测试

[返回实战案例测试集](../../TestCases/README.md) · [完整案例与适配器方案](../../TestCases/Cases/01-UniTask.md)

日期：2026-10-22。Unity 6000.3.8f1 / Windows Editor / UniTask 2.5.11。

**当前分支本轮 5 项原生包测试：4 通过、1 失败。连续 Fast Play 的编辑器回调累积尚未解决。**

## 运行入口与分类

Test Runner → EditMode → `UniTaskValidation.Tests`。测试会实际进入和退出 Play；Scene Reload 保持开启，测试后恢复原设置。

| ID | 测试方法 | 类别 | 通过标准 | 本轮结果 |
| --- | --- | --- | --- | --- |
| UT-01 | BasicAwaitWhenAllAndCancellationWorkInEditMode | Async | Yield/Delay/WhenAll 返回 42；原生 AsTask 取消为 Faulted，内层为 OperationCanceledException | 通过 |
| UT-02 | NormalReloadHasOneRunnerPerTimingAndCompletesWork | ReloadBaseline | 33 类 PlayerLoop 节点各 1；真实游戏帧任务返回 42 | 通过 |
| UT-03 | FastPlayDoesNotAccumulatePlayerLoopOrEditorCallbacks | RepeatedPlay / KnownFailure | 3 轮后节点无重复，编辑器回调不比本轮基线增加 | **失败**：80 → 96 → 112 → 128 |
| UT-04 | DefaultCancellationWaitsForPlayerLoopButImmediateCancellationDoesNot | Cancellation | 默认取消在下一次 PlayerLoop 被观察；立即取消同步完成 | 通过 |
| UT-05 | NativeExceptionSubscriptionRemainsAfterFastPlayExit | EventOwnership | 退出前后事件计数从 1 到 2，证明用户订阅须显式清理；TearDown 完成退订 | 通过（行为刻画） |

NUnit 分类含 `Case.UniTask`、`Scenario.*`；UT-03 另有 `Regression.KnownFailure`。没有 Ignore，没有放宽失败断言。UT-05 的通过只说明观测与断言一致，不代表包会自动替调用方清理订阅。

## 适配与分支整理

当前分支没有 Play Sessions 包，所以活动程序集仅引用 UniTask。原来的取消桥接源码与依赖框架的 3 项测试保留在 [历史参考](../../TestCases/References/UniTaskPlaySessions/README.md)，不再使当前分支出现失效程序集引用。

计划沿用的适配方案：

- 独立 `async Task` 桥接把 UniTask 的 OperationCanceledException 保留为 Canceled，再交给会话 scope 跟踪。
- 任务使用会话 token；退出敏感等待按需选择 cancelImmediately；用户事件成对订退。
- 外部晚到结果由旧 scope 的提交门禁拦截；真实异常仍进入 Faulted。
- 桥接有 Task 包装成本，不能称零分配，也不能修复供应商内部编辑器回调累积。

## 已复现的失败

[UniTask 2.5.11 PlayerLoopHelper](https://github.com/Cysharp/UniTask/blob/2.5.11/src/UniTask/Assets/Plugins/UniTask/Runtime/PlayerLoopHelper.cs) 的 InsertRunner 会注册捕获 runner/queue 的匿名 EditorApplication.playModeStateChanged 回调。当前只读探针观测到每轮 Fast Play 增加 16 个；未测进程内存或性能损耗。

探针限定 Unity 6000.3.8f1，只读取事件存储，不移除或修改供应商回调。升级 Unity 必须复核探针。

本轮后执行的正常 Domain Reload 基线测试把观测恢复为 16。恢复不等于修复；3 轮 Fast Play 中间没有插入重载。原 Play Sessions 的 Force Script Reload 菜单当前分支不存在。

## 结果文件

- job：`f75511bd7db640d7b181c164188a6608`，含本案例 5 项和 Mathematics 2 项。
- [原始 job](../../outputs/test-cases/test-job.json)、[逐阶段观测](../../outputs/test-cases/unitask/observations.jsonl)、[逐项结果](../../outputs/test-cases/unitask/test-results.jsonl)。
- 旧结果保留在 `outputs/unitask/`，其中 7/8 和原框架 33/33 属于之前分支的运行，不是本轮计数。
- 安装锁定提交：`2e993ff18f28c931602a07292df0b0804eebef99`。本轮没有修改 UniTask 包代码、原场景或持久的 Play Mode 设置。

不覆盖完整 UniTask 官方测试集、其他 Unity 版本、Player/IL2CPP 或全部第三方 API。
