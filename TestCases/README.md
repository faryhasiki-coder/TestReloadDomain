# 实战案例测试集

基础案例整理：2026-10-22。接入样例重写：2026-10-23。目标环境：Unity 6000.3.x（不限定小版本）/ Windows Editor；各结果记录实际运行版本。

这是一份“可运行用例 + 待接入案例”的统一目录。每项分别记录测试要点、通过标准和实际结果；“找到源码”“下载完成”“适配完成”“测试通过”“用户认可适配方案”是不同状态。

**2026-10-23 修正：** 原 UNITY-01～08（提交 b2c1327）把样例直接写成了接入 Easy Reload Guard 之后的形态，并且有实测失败，已删除。改为 [INT 接入样例](Cases/06-IntegrationSamples.md)：接入前的实战代码 + 9 个接入验收测试（Explicit，接入前预期失败），未编译、未运行。下面的历史数字不代表重新验证。

## 案例目录

| ID | 案例 / 用途 | 类型与包名 | 适配或修改位置 | 当前结果 |
| --- | --- | --- | --- | --- |
| PKG-01 | [UniTask](Cases/01-UniTask.md)：异步任务与 PlayerLoop | package：`com.cysharp.unitask@2.5.11` | [独立测试](../Assets/UniTaskValidation/Tests/Editor/UniTaskValidationTests.cs)；取消桥接方案见案例页 | 2026-10-22：4/5 通过、1 失败；新增 UT-06/07 设计未执行 |
| PKG-02 | [ZString](Cases/02-ZString.md)：字符串构建、缓冲池、格式化委托 | package：`com.cysharp.zstring@2.6.0` | 拟用独立 Formatter 适配器；不修改供应商源码 | 已核对固定版本源码；未安装、未运行 |
| PKG-03 | [Unity.Mathematics](Cases/03-Mathematics.md)：向量运算、随机数状态 | package：`com.unity.mathematics@1.3.3`（本地已有） | [独立测试](../Assets/MathematicsValidation/Tests/Editor/MathematicsValidationTests.cs)；仅业务随机状态需要 reset | 2026-10-22：2/2 通过；本次未复测 |
| SRC-01 | [2048](Cases/04-2048.md)：棋盘、合并、协程、GoKit 动画 | 完整源码工程；MIT；原 Unity 5.6.5f1 | [本地案例](../outputs/test-cases/projects/2048/)；重点 `Assets/Scripts/GameManager.cs` 与 `Assets/Plugins/GoKit/Go.cs` | 固定提交已下载；未升级、未接入、未运行 |
| SRC-02 | [3D Snake](Cases/05-Snake.md)：单例、场景重进、动态对象 | 完整源码工程；MIT；原 Unity 6000.2.2f1 | [本地案例](../outputs/test-cases/projects/Snake/)；重点 `Assets/Scripts/GameManager.cs`、`SnakeMovement.cs` | 固定提交已下载；未接入、未运行 |
| INT-01 | [Easy Reload Guard 接入样例](Cases/06-IntegrationSamples.md)：计分/事件总线、泛型单例、对象池、服务注册、ScriptableObject、async/Timer/晚到结果、ExecuteAlways | 源码样例；接入目标 `com.yongshi.easyreloadguard`，样例本身不引用 | [样例源码](../Assets/ReloadIntegrationSamples/Runtime/)、[验收测试](../Assets/ReloadIntegrationSamples/Tests/Editor/)、[ExecuteAlways 场景](../Assets/ReloadIntegrationSamples/ExecuteAlways/) | 9 个验收方法；未编译、未运行 |
| GAME-01～03 | [真实开源小游戏](Cases/06-IntegrationSamples.md)：Breakout、Asteroider、Voidrunner | 完整游戏源码；MIT；上游 Unity 6000.3.5f2 / 6000.3.8f1 / 6000.3.24f1 | [Games](../Assets/ReloadIntegrationSamples/Games/)，原版未修改 | 已导入；未编译、未运行 |

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
| S12 四种重载组合与重编译 | R0～R3 独立记录，正常重载/编译不能掩盖 R1/R3 残留 | 未纳入当前样例（见 INT 的不覆盖范围） |
| S13 单例与泛型静态 | 泛型基类单例、直接使用的闭合泛型每局重新开始 | INT-01、INT-02a |
| S14 ScriptableObject | 已加载资产上的运行时字段每局恢复，作者配置不变 | INT-03b |
| S15 编辑态与 Prefab 世界 | ExecuteAlways 编辑态写入不带进 Play；InitialValue 首次捕获的限制 | INT-05 |
| S16 初始化与注册表 | 场景入口每局重新登记服务；初始化标志不跳过必要初始化 | INT-01、INT-03a |
| S17 后台工作与晚到结果 | async 循环、线程池计时器退出即停；旧会话结果不写入新会话 | INT-04a～c |
| S18 自动化与版本边界 | 工具 opt-in 范围、包/DLL 适配、锁重载、Player/IL2CPP 分别验收 | 未纳入当前样例；原设计（PKG-INIT/TWEEN、TOOL、BUILD）见提交 b2c1327 的 `TestCases/DomainReload-TestMatrix.md` |

Player/IL2CPP、性能、Scene Reload 关闭、原生与 GPU 资源、未安装的第三方工具不在当前样例范围内。本清单不承诺覆盖全部第三方 API 或多线程路径。新案例不因"存在 static"直接判失败；纯函数库不为展示适配而修改供应商代码。

## 统一执行方法

1. 固定 Unity 版本、包版本/提交、场景和输入；先验证正常 Domain Reload。旧工程先完成独立的编译/渲染/输入迁移，登记迁移改动。
2. 在同一脚本域下执行至少 3 次 Fast Play，保持 Scene Reload 开启。每轮记录进入前、运行中、退出后的状态。
3. 同一脚本域不能靠每轮强制重载掩盖累积；清理恢复在观测结束后进行。
4. 保存 Unity Test Runner 原始结果和观测数据。失败保留失败；行为刻画通过不等于兼容性通过。
5. 恢复测试前的 Enter Play Mode 设置、后台运行选项和场景。新增适配器应给出源码差异、涉及文件数、是否修改供应商和可拔除方式，再交用户评估。

已有代码入口：`UniTaskValidation.Tests`、`MathematicsValidation.Tests`、`DomainReloadCases.Tests`（包的修复回归）、`ReloadIntegrationSamples.Tests`（接入验收，分类 `ReloadIntegration`，Explicit）。其余已有分类为 `Case.UniTask`、`Case.Mathematics` 和 `Scenario.*`；`Regression.KnownFailure` 仅作筛选标签。此处只登记入口，本次不运行这些测试。

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
