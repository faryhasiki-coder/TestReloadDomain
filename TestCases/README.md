# 实战案例测试集

整理日期：2026-10-22。当前分支：`main`。目标环境：Unity 6000.3.8f1 / Windows Editor。

这是一份“可运行用例 + 待接入案例”的统一目录。每项分别记录测试要点、通过标准和实际结果；“找到源码”“下载完成”“适配完成”“测试通过”“用户认可适配方案”是不同状态。

## 首批 5 个案例

| ID | 案例 / 用途 | 类型与包名 | 适配或修改位置 | 当前结果 |
| --- | --- | --- | --- | --- |
| PKG-01 | [UniTask](Cases/01-UniTask.md)：异步任务与 PlayerLoop | package：`com.cysharp.unitask@2.5.11` | [独立测试](../Assets/UniTaskValidation/Tests/Editor/UniTaskValidationTests.cs)；取消桥接方案见案例页 | **本轮 4/5 通过，1 个真实回归失败** |
| PKG-02 | [ZString](Cases/02-ZString.md)：字符串构建、缓冲池、格式化委托 | package：`com.cysharp.zstring@2.6.0` | 拟用独立 Formatter 适配器；不修改供应商源码 | 已核对固定版本源码；未安装、未运行 |
| PKG-03 | [Unity.Mathematics](Cases/03-Mathematics.md)：向量运算、随机数状态 | package：`com.unity.mathematics@1.3.3`（本地已有） | [独立测试](../Assets/MathematicsValidation/Tests/Editor/MathematicsValidationTests.cs)；仅业务随机状态需要 reset | **本轮 2/2 通过** |
| SRC-01 | [2048](Cases/04-2048.md)：棋盘、合并、协程、GoKit 动画 | 完整源码工程；MIT；原 Unity 5.6.5f1 | [本地案例](../outputs/test-cases/projects/2048/)；重点 `Assets/Scripts/GameManager.cs` 与 `Assets/Plugins/GoKit/Go.cs` | 固定提交已下载；未升级、未接入、未运行 |
| SRC-02 | [3D Snake](Cases/05-Snake.md)：单例、场景重进、动态对象 | 完整源码工程；MIT；原 Unity 6000.2.2f1 | [本地案例](../outputs/test-cases/projects/Snake/)；重点 `Assets/Scripts/GameManager.cs`、`SnakeMovement.cs` | 固定提交已下载；未接入、未运行 |

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

Player/IL2CPP、关闭 Scene Reload、性能/内存长期压测、多线程全覆盖均为后续专项，不在本轮通过范围内。新案例没有用“存在 static”直接判失败；纯函数库也不为了展示适配而修改供应商代码。

## 统一执行方法

1. 固定 Unity 版本、包版本/提交、场景和输入；先验证正常 Domain Reload。旧工程先完成独立的编译/渲染/输入迁移，登记迁移改动。
2. 在同一脚本域下执行至少 3 次 Fast Play，保持 Scene Reload 开启。每轮记录进入前、运行中、退出后的状态。
3. 同一脚本域不能靠每轮强制重载掩盖累积；清理恢复在观测结束后进行。
4. 保存 Unity Test Runner 原始结果和观测数据。失败保留失败；行为刻画通过不等于兼容性通过。
5. 恢复测试前的 Enter Play Mode 设置、后台运行选项和场景。新增适配器应给出源码差异、涉及文件数、是否修改供应商和可拔除方式，再交用户评估。

当前可运行：Test Runner → EditMode → `UniTaskValidation.Tests` 与 `MathematicsValidation.Tests`。分类标签为 `Case.UniTask`、`Case.Mathematics` 和各 `Scenario.*`；`Regression.KnownFailure` 仅作筛选标签，**没有 Ignore 或放宽断言**。

## 本轮结果与分支清理

- 原始 job：[test-job.json](../outputs/test-cases/test-job.json)，ID `f75511bd7db640d7b181c164188a6608`。
- 当前共 **7 项：6 通过，1 失败，0 跳过**。Mathematics 2/2；UniTask 4/5。结果详情见 [运行记录](Results/2026-10-22.md)。
- 当前分支没有 Play Sessions 包；移除失效的 manifest testables、lock 中 embedded 包记录，以及活动 asmdef 的 Play Sessions 引用。
- 原 UniTask 8 项中的 **5 项原生包测试继续运行**；另 **3 项依赖 Play Sessions 的适配测试**和适配源码移至 [历史参考](References/UniTaskPlaySessions/README.md)，不参与本分支编译。原始结果 7/8 仍保留，但不计作本轮运行。
- 原包与 Demo 的 33/33 属于其他分支/先前运行，本轮没有将其充入当前通过数。

## 开源来源与 B 站筛选

- [B 站：Unity 新手练习的开源游戏](https://www.bilibili.com/video/BV1vN4y1G7yL/)直接列出本批采用的 [dgkanatsios/2048](https://github.com/dgkanatsios/2048)。
- [3D Snake 源码](https://github.com/striderzz/Snake-Game-Unity-Project)从 GitHub 补充筛选，**没有声称它就是某条 B 站贪吃蛇视频的配套工程**。
- 也查看了 [王小呆 20 分钟贪吃蛇](https://www.bilibili.com/video/BV1rb411C7Ju/)和 [Unity2D 源码](https://github.com/xingwangdexiaodai/Unity2D)。仓库未见明确许可证，本轮只保留为备选线索，不混入已核对 MIT 的两份下载。
- [ZString](https://github.com/Cysharp/ZString)、[UniTask](https://github.com/Cysharp/UniTask)为 MIT；本地 Mathematics 1.3.3 随包许可证为 Unity Companion License，适用条件与 MIT 不同。
