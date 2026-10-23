# PKG-01 / UniTask

## 包、路径与状态

- 包：`com.cysharp.unitask@2.5.11`；提交 `2e993ff18f28c931602a07292df0b0804eebef99`。
- 用途：基于 Unity PlayerLoop 的异步任务、组合等待和取消。
- 活动测试：[UniTaskValidationTests.cs](../../Assets/UniTaskValidation/Tests/Editor/UniTaskValidationTests.cs)。
- 测试程序集：`UniTaskValidation.Tests`；当前仅引用 `UniTask`。
- 适配器参考：[SessionUniTaskExtensions.cs](../References/UniTaskPlaySessions/Integrations/UniTask/SessionUniTaskExtensions.cs)，已退出主工程编译。
- 2026-10-22 历史结果：**4 通过、1 失败**。详细操作与结果见 [本地 README](../../Assets/UniTaskValidation/README.md)。2026-10-23 本次只补 UT-06/07 设计，未运行或修改测试代码。

## 测试要点、通过标准和实测

| ID | 测试要点 | 通过时应得到什么 | 2026-10-22 实测 |
| --- | --- | --- | --- |
| UT-01 | EditMode Yield、Delay、WhenAll、原生 AsTask 取消 | 工作返回 42；该版本取消转换成 Faulted，内层异常为 OperationCanceledException | 通过 |
| UT-02 | 正常 Domain Reload 下完成真实游戏帧任务 | 16 个 timing × 2 + 1 个同步上下文，共 33 类节点，各只有 1 个；任务返回 42 | 通过 |
| UT-03 | 3 次 Fast Play 的 runner 与编辑器回调 | runner 无重复，回调数等于本轮开始基线 | **失败**：80 → 96 → 112 → 128；每次 +16，runner 无重复 |
| UT-04 | 默认取消和立即取消 | 默认等下一轮 PlayerLoop；立即取消同步完成；原生 AsTask 均验证 Faulted 语义 | 通过 |
| UT-05 | 用户订阅 UnobservedTaskException 的生命周期 | 退出前触发 1 次，退出后累计为 2；刻画“用户订阅不会自动清理”，测试最终自行退订 | 通过（行为刻画） |
| UT-A01 | 会话 token + 取消桥接 + 退出退订 | 连续 3 轮 Task=Canceled、Host=Idle，退出后无旧订阅调用 | 历史通过，本轮未运行 |
| UT-A02 | 外部完成源晚到 | 旧 scope 提交 73 被拒绝，新会话 Published=0 | 历史通过，本轮未运行 |
| UT-A03 | 真正的 UniTask 异常 | Host=Faulted、保留错误、禁止复用 | 历史通过，本轮未运行 |

UT-03 的基线受前序 Fast Play 次数影响，不能硬编码为 16 或 32。比较的是同一轮的净增长；Mathematics 用例也会经过 UniTask 已安装的 PlayerLoop 初始化。

## 根据调研补充的待实现用例

| ID | 前置条件与操作 | 通过标准 | 本次结果 |
| --- | --- | --- | --- |
| UT-06 | 锁定包版本，模拟“调用方手动初始化 → 启动等待任务 → 包自动初始化”，记录初始化阶段、次数与任务状态 | 初始化 owner 和顺序明确；同一当前会话任务不因再次初始化而丢失/挂起；runner 不重复；若原版不满足，保留复现并单独评审适配时机 | 未执行、待实现 |
| UT-07 | R1 中启动跨多帧/长延迟任务，退出再进；同时放置带 token 和不带 token 的对照 | 拥有的旧任务实际停止或结果被拒绝；新任务仍完成；不能用“PlayerLoop 节点数正常”替代“旧队列/任务已结束”的证据 | 未执行、待实现 |

拟扩展现有 `Assets/UniTaskValidation/Tests/Editor/`，本次没有创建新 C#。包名仍为 `com.cysharp.unitask`；适配方案分别是“唯一初始化入口/明确阶段”和“取消 + 结果提交门禁”，不直接清空供应商内部队列来制造通过。

研究线索：[PR #52](https://github.com/Cysharp/UniTask/pull/52) 与 [issue #681](https://github.com/Cysharp/UniTask/issues/681)。引用调研将后者列为未确认修复的报告；本次不复核其当前状态，也不据此断定 2.5.11 必现。两个新增用例的结果需要以后单独取得。

## package 适配器方案

保留独立程序集方案：业务任务接收会话 token；将 UniTask 只消费一次，通过 `async Task` 包装后交给 scope.Track，保留取消语义；事件登记后成对退订；外部晚到结果通过旧 scope 的提交门禁。这只处理调用方的生命周期。

适配器不能修复 `PlayerLoopHelper.InsertRunner` 内部匿名 Editor 回调累积，不能宣称整包 SelfManaged。当前分支没有 Play Sessions，参考源码仅供以后重新接入和评审，不伪造可运行的适配程序集。

## 供应商边界

在固定版本 [PlayerLoopHelper.cs](https://github.com/Cysharp/UniTask/blob/2.5.11/src/UniTask/Assets/Plugins/UniTask/Runtime/PlayerLoopHelper.cs) 中，InsertRunner 注册匿名 playModeStateChanged 回调。测试只读观测事件存储，不反射删除回调，不修改 PackageCache。

失败断言保留，恢复脚本域只能恢复测试环境，不能把缺陷写成已解决。无 Domain Reload 的完整兼容结论仍为不通过。
