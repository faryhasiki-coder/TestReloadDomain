# UNITY-01 / ExecuteAlways 与编辑世界隔离

用例设计补全：2026-10-23。**本次未运行测试、未修改此案例 C#、场景或 Prefab。** 上一轮已留下的实现继续作为待整理代码，历史执行记录不作为本次结果。

## 类型、包名与实际路径

- 类型：Unity 引擎生命周期源码案例，非第三方 package 示例。
- 引擎目标：Unity 6000.3.8f1。
- 修复对照引用包：`com.yongshi.easyreloadguard@0.1.0`。
- 组件：[ExecuteAlwaysCaseCounter.cs](../../Assets/DomainReloadCases/Runtime/ExecuteAlwaysCaseCounter.cs)。
- 自动化入口：[ExecuteAlwaysTests.cs](../../Assets/DomainReloadCases/Tests/Editor/ExecuteAlwaysTests.cs)。
- 场景：[ExecuteAlwaysCase.unity](../../Assets/DomainReloadCases/ExecuteAlways/ExecuteAlwaysCase.unity)。
- Prefab：[ExecuteAlwaysCase.prefab](../../Assets/DomainReloadCases/ExecuteAlways/ExecuteAlwaysCase.prefab)。
- 资产创建辅助：[ExecuteAlwaysFixture.cs](../../Assets/DomainReloadCases/Tests/Editor/ExecuteAlwaysFixture.cs)。已有资产不用重新生成。

## 适配方案

业务/预览分支按对象是否属于运行世界判断，即 `Application.IsPlaying(gameObject)`。仅看全局 `Application.isPlaying` 无法表达 Play 中的 Prefab Stage 对象所属世界。游戏状态与编辑预览状态分别拥有，不能把全部 static 一起清空。

已有样例使用 `[ResetMethod]` 处理进入 Play 的计分/运行 owner，`[OnExitPlayMode]` 释放运行 owner；对象自身的外部事件订退与启停生命周期配对。它不依赖关闭 Domain Reload 后仍出现额外的域重载 OnDisable/OnEnable。

本案例要修改的源码位置是上面的本地组件与测试文件；不修改 Unity 引擎或 PackageCache。后续扩展仅是设计，本次没有继续修改实现。

## 用例

共用模式：R0=Domain/Scene 都开，R1=Domain 关/Scene 开；R2/R3 另按 [矩阵](../DomainReload-TestMatrix.md)记录。每项的 **本次结果均为未执行**。

| ID | 前置条件与操作步骤 | 通过标准 | 代码覆盖 |
| --- | --- | --- | --- |
| EA-01 编辑态预览安全 | Edit Mode 创建/加载组件，修改可编辑参数，通过真实编辑器更新观察预览并触发一次事件 | 编辑回调被观察到；预览正确，游戏计分/生成/运行注册不发生；作者字段不被意外改写 | 已有 `EditorPreviewDoesNotExecuteGameplay`；参数变化的预览行为仍需扩展 |
| EA-02 启停和销毁退订 | 在编辑态重复启用/禁用 3 次，再销毁；每阶段向真实外部事件源发一次探针 | 启用时一次响应、禁用/销毁后零响应；退订不影响其他拥有者；不能只数 OnEnable 被调几次 | 已有 `EnableDisableAndDestroyBalanceSubscriptions` |
| EA-03 编辑态污染后重进 | R1 连续 3 轮；每次进入前，由真实编辑回调改写静态业务值；退出后确认预览继续工作，再次污染 | 修复组每次进入恢复业务默认值；复现组保留污染并单独记为行为刻画；编辑态拥有的计数不被清空；退出后预览仍响应、游戏不写入 | 已有 `EditPollutionIsResetAcrossThreeFastPlays` |
| EA-04 Play 与 Prefab Stage 共存 | Play 运行中打开测试 Prefab，场景实例和 Stage 实例同时接收探针；随后关闭 Stage | 全局 Play 为 true，但只有运行实例执行游戏逻辑；Stage 不抢单例/注册表；运行响应一次；关闭后无失效订阅；Prefab 的 authored 字段、Transform、dirty 状态及文件 hash 不被意外改变 | 已有 `PrefabStageCannotMutatePlayingWorld` |
| EA-05 正常重载对照 | R0；编辑态先污染，再进入 Play | 真实 Domain Reload 后修复/复现组都回各自初始值，序列化配置不变；此结果不能替代 EA-03 | 已有 `NormalDomainReloadProvidesCleanBaseline` |
| EA-06 InitialValue 捕获边界 | 全新域首次 Play 前，编辑回调将字段从源码初值 7 改为 73；比较 InitialValue 与显式 ResetMethod | 行为刻画明确首次观察值可能为 73，不能称“恢复源码初值”；需要固定业务初值的修复组恢复 7；捕获阶段与值有记录 | **待实现**；不能靠新增全局 InitialValue 字段破坏其他用例的运行顺序 |
| EA-07 场景/Prefab 关闭与 inactive 对象 | 编辑态反复开关测试场景/Prefab，启停 GameObject，再进出 Play；覆盖未激活对象 | 无跨世界 owner、旧订阅或无主预览资源；重新打开后预览可用；资产与用户原场景按意图恢复 | **待实现**；EA-02 只覆盖组件 enabled，不能冒充完整 inactive/场景覆盖 |
| EA-08 ExecuteInEditMode 对照 | 在独立样例使用 ExecuteInEditMode，记录编辑态和进入 Prefab/Play 时的行为，与 ExecuteAlways 对照 | 实际回调和 Prefab 编辑限制被明确记录；不把两个特性视为语义相同；若工作流需迁移到 ExecuteAlways，则迁移后按 EA-01～07 验收 | **待实现**；拟路径 `Assets/DomainReloadCases/Runtime/ExecuteInEditModeCaseCounter.cs` |

## 观测要求

- 编辑态 Update 不按游戏固定帧率断言。需要实际回调证据；每阶段先记基线再等待增长，不读取旧计数充当本阶段已更新。
- 未收到回调要记录焦点、启用、active、暂停、后台运行和触发方式，状态写“观测超时/待排障”，不能直接断言引擎不支持。
- 不手工调用 Update、OnEnable 等生命周期方法冒充 Unity 执行。
- 清理代码只恢复本用例取得所有权的场景/Stage/设置；前置条件失败不能关闭用户已经打开的 Prefab。
- EA-06 的观测值与修复值必须分栏；EA-03 的复现组通过不是兼容性通过。

## 研究依据

引用聊天首先指出 ExecuteAlways / ExecuteInEditMode 不再依赖额外域重载回调，并强调 ScriptableObject 与 Editor 对象需要独立考虑。本页把这个调研点展开为可审查用例。

- [Unity Domain Reload](https://docs.unity3d.com/6000.3/Documentation/Manual/domain-reloading.html)
- [ExecuteAlways](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ExecuteAlways.html)
- [Application.IsPlaying](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Application.IsPlaying.html)
- [ExecuteInEditMode](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/ExecuteInEditMode.html)

上一篇历史运行有部分通过与编辑态回调超时，文件仍在 `outputs/test-cases/executealways/`。本页只补测试规格，不把那批结果移植为 EA-01～EA-08 全部通过。
