# INT / Easy Reload Guard 接入样例

整理日期：2026-10-23。**样例与验收测试均未编译、未运行**；下文"未接入时的现象"是按源码推导的预期，不是实测记录。

## 目的与边界

- 提供**接入前**的实战代码，用来检验 `com.yongshi.easyreloadguard` 的接入流程：检查器能否发现问题、按包 README 能否处理、处理后验收测试能否通过，以及哪些问题超出包的能力、只能改业务代码。
- 样例程序集 `ReloadIntegrationSamples` **不引用** Easy Reload Guard，源码中只有业务注释，不标注问题位置。主线上的样例保持接入前状态，接入练习在单独分支或工程副本中进行。
- 包的修复写法回归仍由 `Assets/DomainReloadCases`（Leaky/Guarded 对照）负责，与本组样例分开。
- Unity 不限定小版本：在 6000.3.x 上使用。测试日志会输出实际 `Application.unityVersion`，结论只覆盖实际运行的版本。

## 路径

| 内容 | 路径 |
| --- | --- |
| 样例源码（程序集 `ReloadIntegrationSamples`） | [Assets/ReloadIntegrationSamples/Runtime](../../Assets/ReloadIntegrationSamples/Runtime/) |
| 验收测试（程序集 `ReloadIntegrationSamples.Tests`，分类 `ReloadIntegration`） | [Assets/ReloadIntegrationSamples/Tests/Editor](../../Assets/ReloadIntegrationSamples/Tests/Editor/) |
| 人工观察用场景 / Prefab（ExecuteAlways 计分板） | [Assets/ReloadIntegrationSamples/ExecuteAlways](../../Assets/ReloadIntegrationSamples/ExecuteAlways/) |
| 真实开源小游戏（原版源码，见下文 GAME-01～03） | [Assets/ReloadIntegrationSamples/Games](../../Assets/ReloadIntegrationSamples/Games/) |

## 接入测试流程

1. 在单独分支或副本中操作，记录 Unity 版本与包版本。
2. 运行 **Tools/Easy Reload Guard/运行检查**，记录 `ReloadIntegrationSamples` 程序集的发现项（接入前预期有发现）。
3. 给 `ReloadIntegrationSamples.asmdef` 添加 `EasyReloadGuard` 引用，按包 README 处理。记录改动文件数、用到的特性 / API，以及包无法处理、只能改业务代码的位置。
4. 从**刚编译或刚重载的脚本域**开始，在 Test Runner → EditMode 中手动选中 `ReloadIntegrationSamples.Tests` 运行。用例标为 `Explicit`，整体运行时会跳过。
5. 用 Unity Pipeline 做 PSP 测试：在 `ExecuteAlwaysCase.unity` 和每个游戏场景中，关闭 Domain Reload，连续 Play→Stop 至少 3 轮，期间不改代码、不重新编译。这是"接入工程案例"档位的必需项，要求见 AGENTS.md 的"交付分档"。
6. 检查、编译、验收测试、PSP 分别报告。验收测试通过不能替代 PSP。

## 统一验收标准

- **每局与第一局一致**：Domain Reload 关闭、Scene Reload 开启，同一脚本域内连续 3 轮 Play→Stop，每局可观察行为都与从新域开始时相同。
- **退出即停止**：退出 Play 后，样例启动的后台工作不再执行。这一条与 Domain Reload 设置无关，但关闭后残留工作会串进下一局。
- 测试只通过样例的公开业务接口观察，不引用包，也不重置样例状态。测试收尾只恢复 Enter Play Mode 设置与后台运行选项，并停止仍在编辑器中运行的后台工作；收尾发生在断言之后，不影响结果。
- 测试中途发生域重载（例如 `InitialValue` 关口要求干净域）时，用例以"连续 Fast Play 前提不成立"失败，不计为通过。

## 用例

| ID | 验收测试 | 操作（每轮） | 通过标准 |
| --- | --- | --- | --- |
| INT-01 | `ScoringAcceptanceTests.EverySessionScoresFromZeroAndOncePerKill` | 放置 `ScoreLabel` → `ScoreSystem.Initialize` → 击杀 1 次 → 拾取双倍 → 再击杀 1 次 | 开局 0 分；第一次击杀后 10 分，显示 "10"；双倍后 30 分，显示 "30" |
| INT-02a | `SingletonAndPoolAcceptanceTests.InventoryStartsEmptyEverySession` | 读取 `Inventory.Instance` → 花 60 买 Sword | 开局金币 100、背包为空；购买后剩 40，背包只有 Sword |
| INT-02b | `SingletonAndPoolAcceptanceTests.BulletPoolOnlyHandsOutLiveBullets` | 取 3 发并发射 → 全部回收 → 再取 1 发 | 取出的都是存活对象；本局回收的子弹被复用 |
| INT-03a | `ServiceAndAssetAcceptanceTests.BootstrapRegistersLiveServicesEverySession` | 放置 `GameBootstrap` → 取 `AudioService` 播放一次 | 取得本局创建的存活服务；无异常日志 |
| INT-03b | `ServiceAndAssetAcceptanceTests.PlayerStatsStartAtFullHealthEverySession` | 读取血量 → 受到 30 伤害 | 开局满血 100；受伤后 70 |
| INT-04a | `BackgroundWorkAcceptanceTests.AutoSaveRunsOnlyForCurrentSession` | 以 50 ms 间隔启动自动存档 → 退出 | 本局存档运行；上一局的回调不再执行；退出 0.1 s 后的 0.3 s 静默期内不再存档 |
| INT-04b | `BackgroundWorkAcceptanceTests.HeartbeatRunsOnlyForCurrentSession` | 以 20 ms 间隔启动心跳 → 退出 | 同 INT-04a |
| INT-04c | `BackgroundWorkAcceptanceTests.LateProfileResultDoesNotOverwriteNewSession` | 本局加载档案并完成；再发起一个慢请求后退出；下一局完成上一局的慢请求 | 开局无档案；本局结果正常写入；上一局请求结束（完成或取消均可），晚到结果不覆盖本局 |
| INT-05 | `ExecuteAlwaysAcceptanceTests.EditPreviewDoesNotLeakIntoGameplayScore` | 编辑态预览把得分写为 10 → 进入 Play → 加 1 分 | 运行时从默认分 7 开始，加分后为 8 |

测试中的近似处理：

- INT-03b 用一份 `HideFlags.DontSave` 的常驻实例代替已加载的 `.asset`。两者都跨 Play 存活，只在加载或域重载时执行 `OnEnable`。
- INT-05 在当前编辑场景中放置对象，测试结束时先退出 Play 再删除。每轮退出后，场景恢复出的编辑态对象会再次预览，所以每一局进入前都有一次真实的编辑态写入。

## 评估参考：样例中的问题点

接入者可以先不看本节，接入后再用它核对检查器的发现和处理方式是否完整。"未接入时的现象"为推导，未实测。

| ID | 状态所有者 | 未接入时的现象 | 评估关注点 |
| --- | --- | --- | --- |
| INT-01 | `ScoreSystem.Score`（静态自动属性）、`ScoreChanged`（静态事件）、`s_Multiplier = 1f`（非默认初值）、`s_Initialized`；`EventBus<EnemyKilled>.s_Handlers`（直接使用的闭合泛型）；`ScoreLabel` 未在 OnDisable 退订 | 第 2 局开局分数为 30；继续击杀时，残留在 `ScoreChanged` 上的上一局 `ScoreLabel` 已被销毁，访问 `transform` 抛 MissingReferenceException | 各项必须一致处理：只重置事件总线而不重置 `s_Initialized`，会导致不再计分；只重置 `s_Initialized`，会重复订阅、一次击杀计两次分。`s_Multiplier` 需要恢复 1 而不是 0；泛型总线的闭合类型不一定能被静态扫描发现 |
| INT-02a | `Singleton<T>.s_Instance`（经派生类 `Inventory` 出现的闭合类型） | 第 2 局金币 40，背包已有 Sword | 泛型基类字段的展开 |
| INT-02b | `BulletPool.s_Free`（`static readonly Stack`，元素是场景对象） | 第 2 局 `Get` 弹出已销毁的子弹，抛 MissingReferenceException | readonly 集合应清空，不能置 null |
| INT-03a | `ServiceLocator.s_Services`（`static readonly Dictionary`） | 第 2 局 `GameBootstrap.Awake` 重复 `Add`，抛 ArgumentException；`Get` 返回上一局已销毁的服务 | 注册表的重置时机必须早于场景 Awake |
| INT-03b | `PlayerStats.CurrentHealth`（ScriptableObject 实例上的 `[NonSerialized]` 字段） | 第 2 局开局血量 70 | 这是实例状态而非静态字段，检查器预期不会报告。需要接入方提供每局入口，属于包能力边界的观察点 |
| INT-04a | `AutoSaveService.s_Running` 与 `async void` 循环 | 第 1 局退出后仍在存档（静默期断言失败）；第 2 局 `Start` 被跳过 | 仅在退出时调用 `Stop()` 只是置标志。若下一局在一次 Delay 内重新 `Start`，旧循环会继续运行，需要按会话区分循环（例如令牌）。取消 `async void` 中的 `Task.Delay` 时要处理 OperationCanceledException，否则会产生错误日志 |
| INT-04b | `HeartbeatService.s_Timer`（线程池计时器） | 第 1 局退出后仍有回调；第 2 局 `Start` 被跳过 | 退出时需要释放计时器，而不只是把字段置 null |
| INT-04c | `ProfileLoader.CurrentProfile` 与未完成的 `await` | 第 2 局中，上一局的晚到结果把档案改成 `stale-from-1` | 回写前需要判断请求是否仍属当前会话；`CurrentProfile` 本身也应按局重置 |
| INT-05 | `ExecuteAlwaysCaseCounter.GameplayScore`（编辑态 Update 写入） | 进入 Play 时得分为 10 而不是 7 | `ResetMode.InitialValue` 记录的是本域首次进入 Play 时的值；编辑态已先写入 10 时会把 10 当作初值（包 README 已知限制），需要显式恢复 `DefaultScore` |

## 真实开源小游戏（GAME-01～03）

2026-10-23 从 GitHub 筛选导入，与手写样例并列，用于观察包在**未经改写的真实项目**上的接入表现。筛选条件：Unity 6000.3.x、MIT 许可证、只用 Input System（与本工程 `activeInputHandler: 1` 一致），除本工程已有的 URP、Input System、uGUI 和内置模块外没有其他依赖，体量小。**已导入，未编译、未运行。**

| ID | 项目 / 固定提交 | 上游 Unity | 场景 | 程序集 | 关注的状态所有者 |
| --- | --- | --- | --- | --- | --- |
| GAME-01 | [admtrv/Breakout](https://github.com/admtrv/Breakout) `2b960a3589f30b6e0025f2509ef00bf9155d6f45` | 6000.3.5f2 | `Games/Breakout/Scenes/SampleScene.unity` | `ReloadIntegrationSamples.Games.Breakout` | `BallSpawner.Instance`、`PowerUp.Instance`（`public static` 字段，Awake 中以 `Instance == null` 判断）；砖块破碎协程、`Platform` 加宽协程 |
| GAME-02 | [AmpolStack/asteroider-game](https://github.com/AmpolStack/asteroider-game) `3523520cd0386abd855e5da5c63c596976488863` | 6000.3.8f1 | `Games/Asteroider/Scenes/Game.unity` | `ReloadIntegrationSamples.Games.Asteroider` | `DifficultyManager.Instance`、`UIManager.Instance`（静态自动属性，OnDestroy 中自行清空）；UI Toolkit 回调；PlayerPrefs `HighScore` |
| GAME-03 | [s10nfault/Voidrunner](https://github.com/s10nfault/Voidrunner) `e3668a9b5bb3577f80ca764915e92d2de20b52fe` | 6000.3.24f1 | `Games/Voidrunner/Scenes/SampleScene.unity` | `ReloadIntegrationSamples.Games.Voidrunner` | ScriptableObject 事件通道上的委托、`PlayerCurrentHealthSO` / `IsPlayerGroundedSO` 运行时写入的序列化字段；无静态状态 |

导入方式（脚本一次性执行，不在仓库中保留）：

- 原版 C# 与场景逻辑**未修改**。各游戏目录附 `LICENSE.txt`（上游许可证原文）；有上游 README 时附 `UPSTREAM-README.md`。
- 未导入：各工程自带的 URP `Settings/`、`_Recovery/`，与本工程重复的模板文件（`DefaultVolumeProfile.asset`、`UniversalRenderPipelineGlobalSettings.asset`、`InputSystem_Actions.inputactions`），以及 asteroider 中未使用的模板场景 `Scenes/SampleScene.unity`。
- 每个游戏新增一个 asmdef，只引用 `Unity.InputSystem`，用来隔离全局命名空间中的同名类。
- 模板生成的 `Scenes` 目录和 `SampleScene.unity` 在三个工程中 GUID 相同：asteroider 与 Voidrunner 中的对应资源已换新 GUID，并改写了引用。
- 三个工程引用了 `com.unity.2d.sprite` 的默认精灵 `Square.png` / `Triangle.png`，本工程未安装该包。已从 Unity 安装目录复制到 `Games/_Shared2DSprites/`，换新 GUID 并改写引用，没有安装新包。
- 导入后校验：全工程没有重复 GUID。唯一无法解析的引用是 asteroider `Prefabs/Obstacle.prefab` 上的 AnimatorController，它在上游仓库中本来就缺失，保持原样。
- 工程设置只改了一处：`TagManager` 增加 `Ball` Tag（Breakout 使用 `CompareTag("Ball")`）。Voidrunner 用到 3 号层（上游命名为 Ground），本工程该层未命名，按序号生效，未修改。

按源码推导的预期（未实测）：三个原版大多已能应对关闭 Domain Reload。Breakout 依赖 Unity 对已销毁对象的判空重新赋值单例；asteroider 在 OnDestroy 中清空单例；Voidrunner 的订阅都在 OnEnable/OnDisable 中成对出现。因此它们主要用来观察两件事：

- 检查器对这些写法的报告是否属于误报（例如 RG002 静态 `UnityEngine.Object`），基线或 `[ReloadSafe]` 的处理成本有多大。
- 接入后连续 Play→Stop 时玩法是否正常、Console 中有无错误。若实测发现原版确实没有问题，这本身就是有效结论，不为制造问题而改原版。

使用限制：

- asteroider 的"重开"按钮通过 `SceneManager.LoadScene` 按名称加载场景。场景不在 Build Settings 中时会失败；这不影响 Play→Stop→Play 重入，本次也未修改 Build Settings。
- asteroider 在 `OnApplicationQuit` 中会删除 PlayerPrefs 的 `HighScore` 键。
- Voidrunner 在运行时直接改写资产上的序列化字段（血量、是否着地）。编辑器中这些改动会留在已加载的资产里，保存工程时可能产生 `.asset` 差异。
- 三个工程原本使用 URP 2D Renderer，本工程使用自己的 URP 配置，画面效果可能与上游不同（未验证）。
- 这三个游戏暂时没有自动化验收测试，接入时以 Unity Pipeline 驱动的 PSP 测试为准（见 AGENTS.md 的"交付分档"）。

## 运行前提与已知限制

- 测试从刚编译或刚重载的域开始。若与 `DomainReloadCases.Tests` 同时运行，两组都需要"本域首次进入 Play"，应分开运行。
- 接入前运行检查时，样例程序集的发现项不在 `ProjectSettings/EasyReloadGuardBaseline.txt` 中，`Tools/UnityCI/Run-UnityTests.ps1` 的检查步骤会因此返回 1。是写入基线、在设置中排除该程序集，还是流水线使用 `-SkipAudit`，需要另行决定；本次未改动基线与设置。
- 命名刻意不含 `DomainReloadCases`：流水线常用的 `-TestFilter "EasyReloadGuard|DomainReloadCases"` 不会选中这组 Explicit 用例。
- 不覆盖：Scene Reload 关闭（R2/R3）、脚本重编译与 Editor 重启路径、Player/IL2CPP、原生容器与 GPU 资源、第三方包（UniTask 见 [PKG-01](01-UniTask.md) 与 `PlayReentryTests`）。

## 2026-10-23 修正记录

b2c1327 新增的 7 组案例（原 UNITY-01～08 / 06～13 号案例页）直接写成了接入后的形态：样例源码里已有 `[ResetStatic]`、`[ResetMethod]`、`[OnExitPlayMode]`、`PlaySession`，无法用于接入测试。同时存在以下错误，已整体删除，源码可从提交 b2c1327 取回：

1. **实测失败**：`TestResults.xml`（2026-10-23 15:00）记录 4/4 失败。`ObjectLifetimeTests` 的 3 项以及 `ReloadModeMatrixTests.R1_ReloadSceneOnly` 用 `EditorSceneManager.NewScene(..., Additive)` 建场景，在未保存的 untitled 场景下抛出 InvalidOperationException，并留下空目录 `Assets/DomainReloadCases/GeneratedTestAssets`（已删除）。
2. 新测试类没有排序约束；按名称排序时会排在 `PlayReentryTests` 之前进入 Play，破坏 `Fix_GuardedStateIsResetEachSession` 依赖的"本域首次 Play"前提。
3. `OwnedResourceCase` 在 ExitingPlayMode（此时 `isPlaying` 仍为 true）中用延迟执行的 `Object.Destroy` 释放 `HideAndDontSave` 资源，测试却在退出后立即断言资源已销毁。
4. `InitializationOrderCase` 在 BeforeSceneLoad 中创建 `HideFlags.DontSave` 对象，退出 Play 时不会被销毁，逐轮残留。
5. `ExecuteAlwaysReproState.Score` 等未处理的静态字段不在检查基线中，流水线检查会报出新问题。
6. 引擎行为刻画（重载组合矩阵、字段序列化矩阵、初始化阶段顺序、Editor 事件）与"给包做接入测试"的目的无关，本次不保留。

版本约束同时去除：`UniTaskValidationTests` 中 `Assert.AreEqual("6000.3.8f1", Application.unityVersion)` 已删除。经核对，6000.3.1f1 的 `UnityEditor.CoreModule.dll` 同样含 `m_DelegateOrList` 与 `GetDelegateAt`，两份只读探针都不再绑定小版本；内部结构真的变化时，测试会报出缺失的字段名。
