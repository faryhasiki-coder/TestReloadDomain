# Play Sessions / UniTask 适配

> 历史参考：2026-10-22 从 Assets 移至 TestCases，当前分支不编译本目录。下文使用方式要求先接回 Play Sessions；本轮结果见 [独立用例](../../../../../Assets/UniTaskValidation/README.md)。

这个可独立拔除的集成程序集引用 `Faryhasiki.PlaySessions.Core` 和 `UniTask`，不修改这两个包的源码。

## 使用

```csharp
using PlaySessions.UniTaskIntegration;

plan.Add("game.async", scope => ResetState(), start: scope =>
{
    scope.TrackUniTask(RunAsync(scope.Token));
});
```

只消费每个 UniTask 一次。RunAsync 应接受会话 CancellationToken，Unity 结果写入还应通过捕获的 `scope.TryCommit(...)`。退出敏感的 UniTask.Delay/DelayFrame 可设置 `cancelImmediately: true`，让退出取消不依赖后续游戏帧。

把 PlayerLoop 任务启动放在模块 Start：本包 Reset 发生于 SubsystemRegistration，而 UniTask 2.5.11 自身初始化位于 AfterAssembliesLoaded。不同框架的初始化时机需要明确，不能假定在任意 Reset 方法内启动异步工作都安全。

`TrackUniTask` 通过一层 `async Task` 包装保留取消状态，避免原生 `.AsTask()` 把取消变成 Faulted。它没有吞掉真正异常，也不能停止未传入 token 的任务、处理任意外部线程写入或修复 UniTask 的编辑器事件累积。

历史测试结果：见 [原专项验证记录](../../HistoricalResults.md)。仍有一个未解决的供应商回调累积回归，因此此集成没有声明整个 UniTask 包 SelfManaged。

## 拔除

移除项目中 `TrackUniTask` 的调用/命名空间引用后，可移除这个目录及对应 `.meta`；Play Sessions 核心无需变化。UniTask 包升级时重新运行专项测试，再评估是否仍需要取消语义转换。该转换产生 Task 包装开销，不属于零分配路径。
