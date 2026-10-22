# UniTask 2.5.11 专项验证

日期：2026-10-22。工程 Unity 6000.3.8f1 / Windows Editor / Unity Test Framework 1.6.0。

**结论：8 项专项测试中 7 通过、1 失败。当前版本不能认定为完整支持连续 Fast Play。**

通过常规 UPM 安装：

```text
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11
commit: 2e993ff18f28c931602a07292df0b0804eebef99
```

manifest 与 lock 已记录依赖及提交。项目只增加独立集成代码和测试；UniTask 安装后的 156 个 C# 文件前后 SHA256 一致，Play Sessions 包核心没有修改。

## 最终测试结果

Test Runner 选择 **EditMode → UniTaskValidation.Tests**。这些 EditMode 协程会实际进入和退出 Play；Scene Reload 保持开启。

| 测试 | 结果 | 含义 |
| --- | --- | --- |
| EditMode Yield、时间 Delay、WhenAll、取消 | 通过 | 编辑器时钟下的基础行为正常 |
| 正常 Domain Reload | 通过 | 33 种节点各 1 个，包含 32 个 runner/yielder 与 1 个同步上下文节点；任务完成 |
| 连续 3 次 Fast Play 无重复节点/编辑器回调 | **失败** | 节点无重复，但编辑器回调从 32 增长至 80 |
| 默认取消与立即取消 | 通过 | 默认由下一次 PlayerLoop 观察；立即取消同步完成；原生 AsTask 将取消表示为 Faulted |
| 原生异常订阅退出后的行为 | 通过（行为刻画） | UnobservedTaskException 的用户订阅会保留，仍须使用者退订 |
| 会话 token + 适配桥接，连续 3 次 Fast Play | 通过 | 取消后 Task=Canceled，Host=Idle，用户事件无重复/残留 |
| 外部 UniTaskCompletionSource 延迟结果 | 通过 | 旧 scope 无法将 73 提交给新会话 |
| 真实 UniTask 故障 | 通过 | 异常仍触发 Faulted 并禁止 Host 复用，没有被取消适配吞掉 |

安装后的原 Play Sessions / Demo 回归：**33/33 通过**。这些结果不是 UniTask 官方完整测试集，也不覆盖全部第三方 API、Player、IL2CPP 或其他 Unity 版本。

## 已复现的供应商问题

同一脚本域中，三次 Fast Play 的只读观测：

| 时点 | UniTask 编辑器状态回调 | PlayerLoop 节点种类 | 重复节点 |
| --- | ---: | ---: | ---: |
| 基线 | 32 | 33 | 0 |
| 第一次退出 | 48 | 33 | 0 |
| 第二次退出 | 64 | 33 | 0 |
| 第三次退出 | 80 | 33 | 0 |

基线为 32，是因为前一项测试已进行一次 Fast Play。每次新增 16 个回调，增量不依赖基线。正常 Domain Reload 后观测为 16。

`PlayerLoopHelper.Init` 在关闭 Domain Reload 时重新 Initialize；`InsertRunner` 移除旧 PlayerLoop 节点，但每次都会注册一个捕获 runner/queue 的 `EditorApplication.playModeStateChanged` 匿名回调。本轮没有观察到对应退订。计数反映实际委托注册，不是根据源码推算。存在持续保留旧 runner/queue 引用的风险；本轮没有测量进程内存增长或性能损耗。

失败测试仍保留原断言，不标记 Ignore，也不把“确实增长”写成通过。探针只读取 Unity 6000.3.8f1 的事件存储；没有通过私有反射移除或篡改任何供应商回调。升级 Unity 时必须重新核对该探针。

本轮清理实测：强制脚本重载前回调为 **64**，重载后为 **16**，Host=Idle、无编译错误。记录于 `outputs/unitask/recovery.json`。这证明本次托管回调可以通过域重载恢复，不表示供应商缺陷已修复。

建议当前使用正常 Domain Reload，或在受控测试结束后执行 `Tools > Play Sessions > Force Script Reload`。没有将 UniTask 声明为 `SelfManaged`；也没有在本次“先测试”阶段自动增加阻断所有 Fast Play 的生产策略。若决定强制阻断，可在项目集成程序集添加：

```csharp
[assembly: PlaySessionCompatibility(DependencyKind.Package, "com.cysharp.unitask", "2.5.11",
    DependencyHandling.FullReloadRequired, "Assets/UniTaskValidation/README.md: Editor callback accumulation reproduced.")]
```

添加后 Fast Play 复现测试也会被预检查阻断，应在独立验证工程复测供应商修复，再更新策略。

## 取消语义的最小适配

2.5.11 的 `UniTaskExtensions.AsTask` 对 `OperationCanceledException` 调用 `TaskCompletionSource.SetException`，因此得到 **Faulted**，而非 **Canceled**。直接 `scope.Track(operation.AsTask())` 会使正常退出被会话管理认为是故障。

独立适配器在 `Assets/Integrations/UniTask/`，仅 20 行 C#（含空行/注释）及一个 asmdef，通过 `async Task` 方法构建器保留取消语义：

```csharp
scope.TrackUniTask(WorkAsync(scope.Token));
```

这不是供应商内部状态清理。它只解决当前 Host 的 Task 边界；会产生 Task 包装成本，不能宣称零分配。退出敏感的 PlayerLoop 操作在本轮使用 `cancelImmediately: true`；项目应根据行为与开销选择，不把“清空队列”当作任务已取消。

## 测试环境与初轮失败说明

初轮为 3 通过、5 失败，原始结果保存在 `outputs/unitask/initial-test-job.json`，没有删除：

- Editor 不在前台且 `runInBackground=false` 时，游戏帧测试超时。后续只在测试期间启用后台运行，结束恢复，并继续要求真实 PlayerLoop 推进；没有手工调用 runner 冒充游戏帧。
- EditMode 不保证 `Time.frameCount` 前进，将 NextFrame/DelayFrame 测试放在真实 Play，编辑器测试只使用 Yield 和时间 Delay。
- 原始“取消应为 Canceled”的假设不适用于 UniTask.AsTask；改为显式验证其 Faulted 语义，再单独验证适配桥接能保留 Canceled。原供应商回调累积的失败断言没有放宽。

最终专项 job：`ec47588318f5483bba8b7e16e5bd1eb3`。原包回归 job：`d4897b87d6e842648188d4cbd72fd231`。

结果、逐项记录和观测位于 `outputs/unitask/`：`final-test-job.json`、`test-results.jsonl`、`observations.jsonl`、`play-sessions-regression.json`、`source-verification.json`。这些本机产物按项目规则不纳入 Git；本文保留可复查的核心结论。

项目原场景和 Enter Play Mode 配置文件保持不变。工作区另有 Unity Pipeline 0.8.0-exp.1 → 0.9.0-exp.1 的独立升级，本次保留，未将其归入 UniTask 修改。

## 官方源码与说明

- [UniTask 2.5.11 release](https://github.com/Cysharp/UniTask/releases/tag/2.5.11)
- [PlayerLoopHelper：编辑器回调注册与无 Domain Reload 初始化](https://github.com/Cysharp/UniTask/blob/2.5.11/src/UniTask/Assets/Plugins/UniTask/Runtime/PlayerLoopHelper.cs#L203)
- [UniTaskExtensions.AsTask：异常转换](https://github.com/Cysharp/UniTask/blob/2.5.11/src/UniTask/Assets/Plugins/UniTask/Runtime/UniTaskExtensions.cs#L128)
- [官方安装、取消与 Editor 限制说明](https://github.com/Cysharp/UniTask/tree/2.5.11#for-unityeditor)
