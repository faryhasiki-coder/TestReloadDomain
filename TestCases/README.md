# 实战案例测试集

基础案例整理：2026-10-22。按调研补全用例：2026-10-23。目标环境：Unity 6000.3.8f1 / Windows Editor。

这是一份“可运行用例 + 待接入案例”的统一目录。每项分别记录测试要点、通过标准和实际结果；“找到源码”“下载完成”“适配完成”“测试通过”“用户认可适配方案”是不同状态。

**2026-10-23 已补 7 组代表性源码案例、32 个测试方法及对应规格，未编译、未运行测试、未操作 Unity、未安装依赖。** 其中 R2/R3 两项为 Explicit；每组都区分通过标准和实际结果。完整分类及尚未实现的扩展场景见 [Domain Reload 测试用例矩阵](DomainReload-TestMatrix.md)。下面的历史数字不代表这次重新验证。

## 案例目录

| ID | 案例 / 用途 | 类型与包名 | 适配或修改位置 | 当前结果 |
| --- | --- | --- | --- | --- |
| PKG-01 | [UniTask](Cases/01-UniTask.md)：异步任务与 PlayerLoop | package：`com.cysharp.unitask@2.5.11` | [独立测试](../Assets/UniTaskValidation/Tests/Editor/UniTaskValidationTests.cs)；取消桥接方案见案例页 | 2026-10-22：4/5 通过、1 失败；新增 UT-06/07 设计未执行 |
| PKG-02 | [ZString](Cases/02-ZString.md)：字符串构建、缓冲池、格式化委托 | package：`com.cysharp.zstring@2.6.0` | 拟用独立 Formatter 适配器；不修改供应商源码 | 已核对固定版本源码；未安装、未运行 |
| PKG-03 | [Unity.Mathematics](Cases/03-Mathematics.md)：向量运算、随机数状态 | package：`com.unity.mathematics@1.3.3`（本地已有） | [独立测试](../Assets/MathematicsValidation/Tests/Editor/MathematicsValidationTests.cs)；仅业务随机状态需要 reset | 2026-10-22：2/2 通过；本次未复测 |
| SRC-01 | [2048](Cases/04-2048.md)：棋盘、合并、协程、GoKit 动画 | 完整源码工程；MIT；原 Unity 5.6.5f1 | [本地案例](../outputs/test-cases/projects/2048/)；重点 `Assets/Scripts/GameManager.cs` 与 `Assets/Plugins/GoKit/Go.cs` | 固定提交已下载；未升级、未接入、未运行 |
| SRC-02 | [3D Snake](Cases/05-Snake.md)：单例、场景重进、动态对象 | 完整源码工程；MIT；原 Unity 6000.2.2f1 | [本地案例](../outputs/test-cases/projects/Snake/)；重点 `Assets/Scripts/GameManager.cs`、`SnakeMovement.cs` | 固定提交已下载；未接入、未运行 |
| UNITY-01 | [ExecuteAlways / ExecuteInEditMode](Cases/06-ExecuteAlways.md)：编辑世界与运行世界隔离 | 引擎源码案例；修复对照引用 `com.yongshi.easyreloadguard@0.1.0` | [组件](../Assets/DomainReloadCases/Runtime/ExecuteAlwaysCaseCounter.cs)、[场景与 Prefab](../Assets/DomainReloadCases/ExecuteAlways/) | EA-01～05 已有实现；EA-06～08 待实现；本次均未执行 |
| UNITY-02 | [静态状态与所有权](Cases/07-StateOwnership.md) | 源码案例；`com.yongshi.easyreloadguard@0.1.0` | [StateOwnershipCase](../Assets/DomainReloadCases/Runtime/StateOwnershipCase.cs)；字段 reset / 状态根重建 / 泛型显式登记 | 6 个方法；未编译、未运行 |
| UNITY-03 | [对象字段与生命周期](Cases/08-ObjectLifetime.md) | 引擎源码案例；R1 使用现有 Easy Reload Guard | [ObjectLifetimeProbe](../Assets/DomainReloadCases/Runtime/ObjectLifetimeProbe.cs)；场景/Prefab 重建 | 3 个方法；未编译、未运行 |
| UNITY-04 | [ScriptableObject](Cases/09-ScriptableObject.md) | 源码案例；`com.yongshi.easyreloadguard@0.1.0` | [ScriptableStateCase](../Assets/DomainReloadCases/Runtime/ScriptableStateCase.cs)；资产运行状态与副本 owner | 2 个方法；未编译、未运行 |
| UNITY-05 | [事件、Task、Thread、Timer](Cases/10-ExternalWork.md) | 源码案例；`com.yongshi.easyreloadguard@0.1.0` | [ExternalWorkCase](../Assets/DomainReloadCases/Runtime/ExternalWorkCase.cs)；退出释放、token 与结果门禁 | 10 个方法；未编译、未运行 |
| UNITY-06 | [原生与 Unity 资源](Cases/11-OwnedResources.md) | 源码案例；现有 `com.unity.collections@2.6.2` + Easy Reload Guard | [OwnedResourceCase](../Assets/DomainReloadCases/Runtime/OwnedResourceCase.cs)、[NativeJobOwnershipTests](../Assets/DomainReloadCases/Tests/Editor/NativeJobOwnershipTests.cs)；完成 Job 后释放自有资源 | 3 个方法；未编译、未运行 |
| UNITY-07 | [初始化与 Editor 入口](Cases/12-Initialization.md) | 源码案例；`com.yongshi.easyreloadguard@0.1.0` | [InitializationOrderCase](../Assets/DomainReloadCases/Runtime/InitializationOrderCase.cs)、Editor 测试探针；显式 B→A 顺序 | 4 个方法 + 人工重编译观察入口；未编译、未运行 |
| UNITY-08 | [四种重载组合](Cases/13-ReloadModes.md) | 引擎源码案例；static reset 对照用 Easy Reload Guard | [ReloadModeMatrixTests](../Assets/DomainReloadCases/Tests/Editor/ReloadModeMatrixTests.cs)；按组合记录身份、字段与回调 | 4 个方法，其中 R2/R3 Explicit；未编译、未运行 |

游戏完整源码放在 `outputs/test-cases/projects/`，没有导入主工程 `Assets/`。因此没有同名 GameManager 冲突，也没有在主工程安装旧游戏依赖。该目录按现有规则被 Git 忽略；版本、来源和文件哈希见 [来源清单](sources.json) 与 [下载清单](../outputs/test-cases/source-manifest.json)，可按固定提交重新取得。

## 场景分类

下表覆盖本批用例和已保留的适配测试设计，不声称覆盖所有第三方 API。

| 场景 | 测什么 / 怎么判断 | 对应用例 |
| --- | --- | --- |
| S01 基础功能与正常重载 | 正常 Domain Reload 下结果正确，作为 Fast Play 对照 | UT-01、UT-02、MATH-01；全部新案例的前置基线 |
| S02 连续 Fast Play | 同一脚本域进入/退出至少 3 次；节点、回调、实例不逐轮累积 | UT-03、UT-04、MATH-02、ZS-04、G2048-03、SNAKE-03 |
| S03 静态业务状态 | 业务状态按会话重置；不可变常量和可复用缓冲不需要机械清零 | MATH-02、ZS-02、G2048-03 |
| S04 事件与所有权 | 单次触发一次；退出解除用户订阅；供应商内部挂钩另行观测 | UT-05、UT-A01 |
| S05 异步取消 | 延迟取消与立即取消；原生 Faulted 与桥接 Canceled 的差别 | UT-04、UT-A01 |
| S06 旧结果与真实故障 | 旧会话回调不能写入新会话；业务异常不得被吞成正常取消 | UT-A02、UT-A03 |
| S07 缓冲与格式化注册 | using/finally 归还缓冲；恢复自己替换的委托；无旧对象闭包 | ZS-01～ZS-04 |
| S08 数学与随机数 | 纯计算结果稳定；RNG state 属于调用方；固定种子可重复 | MATH-01、MATH-02 |
| S09 Unity 对象与单例 | 单例引用有效，重复重进后对象数回基线；保留场景内真实行为 | SNAKE-02～SNAKE-04、G2048-03 |
| S10 协程与动画队列 | 中途停止/重开不让旧动画继续写对象，退出后队列无残留 | G2048-02、G2048-03 |
| S11 玩法与升级隔离 | 合并、计分、移动、碰撞仍正确；版本迁移问题单独登记 | G2048-01、G2048-04、SNAKE-01 |
| S12 四种重载组合与重编译 | R0～R3 独立记录，正常重载/编译不能掩盖 R1/R3 残留 | DR-01～04 |
| S13 实例字段与对象身份 | 每局 new、场景重建、inactive/disabled、复用实例分别判断 | OBJ-01～04 |
| S14 ScriptableObject | 资产、运行副本、事件通道和磁盘保存边界 | SO-01/02 |
| S15 编辑态与 Prefab 世界 | ExecuteAlways、ExecuteInEditMode、预览、InitialValue 捕获、跨世界静态 owner | EA-01～08 |
| S16 编辑器与初始化顺序 | 静态构造、运行阶段依赖、Editor 初始化、真实程序集重载 | INIT-01/02、IN-03/04 |
| S17 后台与原生资源 | 取消请求与实际停止分开；线程、计时器、Job、GPU 资源按所有权收尾 | ASYNC-01～03、RES-01/02 |
| S18 自动化与版本边界 | 工具 opt-in 范围、包/DLL 适配、锁重载、Player/IL2CPP 分别验收 | PKG-INIT/TWEEN、TOOL-01～04、BUILD-01/02 |

Player/IL2CPP、性能和未安装工具仍是专项设计；关闭 Scene Reload、线程与原生资源已有代表源码，但均无本次执行结果。具体未覆盖项见矩阵。本清单不承诺所有第三方 API 或多线程路径全覆盖。新案例不因“存在 static”直接判失败；纯函数库不为展示适配而修改供应商代码。

## 统一执行方法

1. 固定 Unity 版本、包版本/提交、场景和输入；先验证正常 Domain Reload。旧工程先完成独立的编译/渲染/输入迁移，登记迁移改动。
2. 在同一脚本域下执行至少 3 次 Fast Play，保持 Scene Reload 开启。每轮记录进入前、运行中、退出后的状态。
3. 同一脚本域不能靠每轮强制重载掩盖累积；清理恢复在观测结束后进行。
4. 保存 Unity Test Runner 原始结果和观测数据。失败保留失败；行为刻画通过不等于兼容性通过。
5. 恢复测试前的 Enter Play Mode 设置、后台运行选项和场景。新增适配器应给出源码差异、涉及文件数、是否修改供应商和可拔除方式，再交用户评估。

已有代码入口：`UniTaskValidation.Tests`、`MathematicsValidation.Tests`、`DomainReloadCases.Tests`。ExecuteAlways 使用 `Case.ExecuteAlways` 分类，其余已有 `Case.UniTask`、`Case.Mathematics` 和 `Scenario.*`；`Regression.KnownFailure` 仅作筛选标签。此处只登记入口，本次不运行这些测试。

## 历史结果与分支清理（2026-10-22）

- 原始 job：[test-job.json](../outputs/test-cases/test-job.json)，ID `f75511bd7db640d7b181c164188a6608`。
- 当日共 **7 项：6 通过，1 失败，0 跳过**。Mathematics 2/2；UniTask 4/5。结果详情见 [运行记录](Results/2026-10-22.md)，不计入本次设计补全结果。
- 当日所在分支没有 Play Sessions 包；已移除失效的 manifest testables、lock 中 embedded 包记录，以及活动 asmdef 的 Play Sessions 引用。
- 原 UniTask 8 项中的 **5 项原生包测试保留**；另 **3 项依赖 Play Sessions 的适配测试**和适配源码移至 [历史参考](References/UniTaskPlaySessions/README.md)，不参与当前编译。原始结果 7/8 仍保留，但不计作本次运行。
- 原包与 Demo 的 33/33 属于其他分支/先前运行，本轮没有将其充入当前通过数。

## 开源来源与 B 站筛选

- [B 站：Unity 新手练习的开源游戏](https://www.bilibili.com/video/BV1vN4y1G7yL/)直接列出本批采用的 [dgkanatsios/2048](https://github.com/dgkanatsios/2048)。
- [3D Snake 源码](https://github.com/striderzz/Snake-Game-Unity-Project)从 GitHub 补充筛选，**没有声称它就是某条 B 站贪吃蛇视频的配套工程**。
- 也查看了 [王小呆 20 分钟贪吃蛇](https://www.bilibili.com/video/BV1rb411C7Ju/)和 [Unity2D 源码](https://github.com/xingwangdexiaodai/Unity2D)。仓库未见明确许可证，本轮只保留为备选线索，不混入已核对 MIT 的两份下载。
- [ZString](https://github.com/Cysharp/ZString)、[UniTask](https://github.com/Cysharp/UniTask)为 MIT；本地 Mathematics 1.3.3 随包许可证为 Unity Companion License，适用条件与 MIT 不同。
