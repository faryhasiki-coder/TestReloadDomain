# Changelog

## [0.3.1] - 2026-10-30

依据 Origins 首次 PSP 基线（0.3.0）暴露的残留探测盲区：

- 读取 Unity 6 编辑器事件的 `EventWithPerformanceTracker<T>` 容器（`m_Delegate` → `m_DelegateOrList.Reference`，单个委托或 `Entry[]`），覆盖 `EditorApplication.playModeStateChanged`、`quitting`、`hierarchyChanged` 等此前漏读的事件。只读字段，不调用会创建性能追踪器的枚举器；结构读取集中在 `EditorCompat`，不符时计为“无法读取”。
- 新增 `InitializerPurity`：只读 IL 判定静态构造是否为纯初始化（BCL 集合/字符串/值类型构造、写本类型字段、本类型内纯方法与 lambda 缓存；UnityEngine、业务代码、IO/线程/环境 API、读写其他类型静态字段一律不纯）。纯初始化的类型默认读取，设置“残留探测读取有静态构造的类型”只影响不纯的类型。
- 报告与 Console 分别统计“跳过（静态构造可能有副作用）”与“无法读取”。
- 6000.3.1f1 batchmode：编译通过，`EasyReloadGuard.Editor.Tests` 53/53 通过（新增纯初始化判定、事件容器读取等 4 项）。尚未在用户编辑器中重做 PSP，Origins 中实际可读字段数待下次 PSP 确认。

## [0.3.0] - 2026-10-29

接入能力改进：检查报告从“违规清单”改为可直接决策的工作清单，接入边界改为可判定，新增运行时残留证据。

- 检查器读取方法体 IL（只读，`MethodBody.GetILAsByteArray`，不引入 Cecil、不执行代码、不改写程序集），范围限声明类型及其嵌套类型（含 lambda、局部函数与 async/迭代器状态机）。每条字段问题附“上下文”：类型初始化中是否赋值/填充、类型初始化外的赋值位置、内容修改位置（集合/数组/对象，启发式）、分析范围。属性 setter 与事件访问器的写入归属到调用方；非私有访问器标为类型外可写。
- 建议按上下文给出，不再对每条 RG003 给同一句话。私有且只在类型初始化时赋值的字段（RG003）与只在类型初始化时填充的私有 readonly 集合（RG004）降为提示；元素被改写的 readonly 数组升为警告。规则编号与主体不变，现有基线继续有效。
- 新增 RG013：已声明的 `[ResetStatic]` 会破坏下一会话（把只在类型初始化中创建、类型内没有其他赋值位置的引用置 null；`Clear` 清掉类型初始化中填充的内容）。
- 新增 RG014：类型有 `[ResetMethod]` 时，核对重置方法（含其调用的类型内方法）是否赋值或清理了每个可变静态字段；有意保留的字段用 `[ReloadSafe]` 说明。
- 报告开头新增工作清单：按来源（Assets / 包 / DLL）→ 程序集 → 类型汇总未基线的错误与警告。设置页的问题列表同时显示上下文。
- 接入边界放宽（用户确认）：允许业务新增只在 Editor 编译的 `[ResetMethod]` 重置方法（方法连同特性整体放在 `#if UNITY_EDITOR` 内，只恢复本类型的静态状态）。`[OnExitPlayMode]` 仍只标注业务已有方法：退出时释放订阅、线程与资源在 Player 中同样需要，属于业务生命周期。
- 新增退出 Play 后的残留探测（设置“退出 Play 后探测残留”，默认开启）：关闭 Domain Reload 时，每次回到编辑模式读取引擎/编辑器静态回调与检查报告中未处理的项目字段，报告指向已销毁对象的引用与回调，以及跨会话增长的订阅和集合，写入 `Library/EasyReloadGuard/ResidueReport.md`。默认跳过有静态构造的项目类型（设置“残留探测读取有静态构造的类型”可放开）。
- 新增 `Documentation~/接入手册.md`：归属判断（业务缺陷 / 改模式源头 / 本包声明 / 适配器）、按上下文的决策表、禁忌清单、Editor-only 重置方法写法、大工程推进节奏与交付证据。
- 在 Unity 6000.3.1f1 上以 batchmode 验证：编译通过；EditMode 63 项中 62 通过，`EasyReloadGuard.Editor.Tests` 全部通过（新增 9 项，覆盖上下文与建议、访问器归属、RG013、RG014、工作清单与残留探测比较）；唯一失败为既有的 MATH-02 全量顺序问题，单独运行通过。检查为错误 6、警告 17、提示 0、已基线 8，错误均来自接入前形态的 `ReloadIntegrationSamples`。残留探测尚未在用户编辑器中做 PSP 实测。

## [0.2.0] - 2026-10-27

依据第 01 轮双模型审查（工具包逻辑代码）的共识修正：

- `PlaySession.Token` 取消回调抛出的异常计入退出（或进入时结束上一会话）的失败汇总，按“重置失败后强制重载”登记重载；其他回调和释放步骤照常执行。
- `RegisterPersistentReset` 改为独立的持久登记表：每次进入 Play 都执行，某次失败不再丢失登记；新增 `UnregisterPersistentReset`；动作目标是已销毁的 `UnityEngine.Object` 时自动解除；同一目标与方法只登记一份。进入 Play 只取出一次性登记，持久登记不再因 Domain Reload 开启时的 Play 或测试排出而丢失。
- 直接标在实例属性/事件上的 `[ResetStatic]` 报 RG010（不可入基线）；属性/事件扫描只遍历引用了本包运行时程序集的程序集。
- `ResetMode.Default`/`Clear` 不再长期持有首次观察到的字段值，只有 `InitialValue` 保存初值。
- 检查默认扫描 Assets 与全部包；设置项“纳入检查的包”（`includedPackages`）改为“排除的包”（`excludedPackages`）。
- 移除内置 UniTask 适配器：2.5.11 的 Editor 回调累积不能声明为整包已处理（见 README“已知限制”）。
- 新增适配器 API `KnownIssue` 与规则 RG023；新增内置适配器（锁定版本，逐条源码复核）：Core RP 17.3.0、URP 17.3.0、uGUI 2.0.0、Visual Scripting 1.9.9、Timeline 1.8.9、Burst 1.8.26、Collections 2.6.2、AI Navigation 2.0.9、Newtonsoft Json 3.2.2、Unity MCP 10.0.0。复核出的会影响运行逻辑的项（Core RP `DebugUpdater`、TMP `MaterialReferenceManager` 等）以 RG023 继续报告。
- 预编译 DLL 只扫描对当前构建目标可用的部分；NUnit 不再参与检查。
- 在 Unity 6000.3.1f1 上验证：batchmode EditMode 测试 53 项中 52 通过（MATH-02 单独运行通过，全量失败源于测试顺序与 InitialValue 关口重载的相互作用）；经 com.unity.pipeline 连续 4 轮 Play→Stop（关闭 Domain Reload、开启 Scene Reload），Console 无错误。默认扫描全部包后首次检查报出错误 149、警告 1566（多来自 Unity 官方包）；逐包复核并加入内置适配器、过滤仅编辑器 DLL、测试工程排除 com.unity.pipeline 后，检查为错误 0、警告 7，退出码 0（复核明细见工程 TestCases/References/PackageReloadReview）。

依据第 02 轮双模型审查（工具包逻辑代码）的共识修正：

- 基线应用每次按当前基线重算“已基线”标记：同一域内撤销 RG023 的基线条目后再次检查，该警告恢复为未基线（此前按域缓存的计划复用同一 Finding，标记只置不清）。新增对应 EditMode 测试；本项修正尚未在 Unity 中编译或运行测试。

依据第 03 轮双模型审查（工具包运行时隔离）的共识修正（不兼容变更）：

- 运行时程序集不再进入 Player 的业务生命周期：移除 `PlaySession.Token`、`Current`、`Id`、`IsActive` 与 `PlaySessionStamp`，以及 Player 中的启动入口（`SubsystemRegistration`）、`CancellationTokenSource` 和 `Application.quitting` 订阅。会话编号（仅用于日志）、进入 Play 的钩子与重置登记表只在 Editor 中编译；`PlaySession` 对外只剩带 `Conditional("UNITY_EDITOR")` 的 void 命令。新增测试锁定这一公开面。
- 退出 Play 释放不再包含“令牌取消”步骤，失败汇总只统计 `[OnExitPlayMode]` 与适配器释放步骤。
- 接入约定改为：业务只在已有成员上加特性并添加必要的程序集引用；不为本包新增业务方法、Unity 消息、参数、登记调用或控制分支，特性表达不了的状态放到 Editor 适配器。README、特性注释及泛型展开/泛型方法的提示文本同步修改。
- 迁移：读取 `PlaySession.Token`/`Current` 的业务代码改由业务 owner 自己的取消源处理；工程内的修复组案例 `GuardedUniTaskLoader`/`GuardedCaseCounter` 与接入样例已按此调整。
- 以上修正尚未在 Unity 中编译，未运行测试、检查或 PSP。

新增异步处理器扩展点（填补移除会话令牌后异步处理的空缺，本包不内置策略；已被下文 2026-10-28 的异步规范调整撤销）：

- 新增 Editor 接口 `IAsyncReloadHandler`（`OnExitingPlayMode` / `OnEnteringPlayMode`）与适配器 API `ReloadAdapterBuilder.AsyncHandler(handler, order)`。处理器随所在适配器的目标与版本复核生效；空处理器报 RG020。
- 调用时机：退出 Play 时最先调用 `OnExitingPlayMode`（早于 `[OnExitPlayMode]` 与适配器释放，Domain Reload 开启时也调用）；关闭 Domain Reload 时进入 Play 最先调用 `OnEnteringPlayMode`（早于登记项与全部重置）。失败计入汇总，按设置登记重载。
- `ResetPipeline.RunEnter` 拆出可注入计划的重载；检查报告的重置计划统计增加异步处理器数量。新增 EditMode 测试锁定调用顺序。
- 本项尚未在 Unity 中编译，未运行测试、检查或 PSP；接入样例 `AutoSaveService`/`ProfileLoader` 仍按 `KnownIssue` 报告，需由工程实现处理器后另行验证。

后续验证与工程原型（2026-10-27，上述“未验证”为实现当时的记录）：

- Unity 6000.3.1f1 编译完成，`EasyReloadGuard.Editor.Tests` 40/40 通过，包括新增的异步处理器顺序测试。
- 在工程的 `Assets/ReloadIntegrationSamples/Editor` 实现样例专用处理器；保留 INT-04c 的终态要求，旧请求实际取消结束，无关旧任务继续完成，业务 Runtime 不改动。原型只在工程侧，不属于 package 内置策略。
- 完整 Explicit 接入验收 11/12 通过（后台工作 6/6）；唯一失败仍为 INT-05 编辑预览前置条件。四场景各 3 轮 Pipeline PSP 完成，逐轮无新增错误或 Guard 日志。尚未验证 Player 构建、其他 Unity/Mono 版本和长期使用成本；详细结果与原型限制见工程原型 README。

异步规范调整（2026-10-28，不兼容变更，撤销上述异步处理器扩展点）：

- 规范：异步任务、后台循环与晚到结果不得跨越其 owner 的生命周期，由业务用自有取消源或 `Application.exitCancellationToken` / `destroyCancellationToken` 负责。这是业务自身的正确性要求，不属于“为本包改业务”；接入边界仍禁止业务依赖本包的运行时状态或流程。
- 移除 `IAsyncReloadHandler` 与 `ReloadAdapterBuilder.AsyncHandler`，以及重置计划中的异步步骤、`ResetPipeline.RunEnter` 的可注入重载、检查报告中的异步处理器统计和对应测试。第三方代码的取消继续使用适配器 `OnExitPlayMode` 调用其自带入口。
- 新增检查规则：RG005（Player 程序集中的 `async void` 方法）、RG006（类型为 `Task`/`ValueTask`/`CancellationTokenSource`/`Thread`/计时器的未处理静态字段），均为可入基线的警告。新增 EditMode 测试。
- 工程侧样例异步原型 `Assets/ReloadIntegrationSamples/Editor/IntegrationSamplesAsyncHandler.cs` 及其 README 已移除（原文归档至 `F:/本地文档/项目技术方案文档/域重载/归档/`）；`AutoSaveService`/`ProfileLoader` 需业务改造。同日主线清除了接入样例的全部接入代码（Runtime 特性、asmdef 引用与 Editor 适配器，差异归档至同一目录），样例恢复接入前形态；下一条检查结果中的两条样例 RG023 来自清除前的适配器。上文 2026-10-27 的原型验收与 ExecuteAlways 场景 PSP 结果不再适用。
- 2026-10-28 经 Unity Pipeline 在 6000.3.1f1 下验证：编译通过，`EasyReloadGuard.Editor.Tests` 40/40 通过；检查错误 0、警告 10（新增 RG005 `AutoSaveService.Loop` 及两条样例 RG023）、提示 1、已基线 8，阻断 0，RG006 未命中。未运行 PSP 与接入验收。

## [0.1.0] - 2026-10-22

- 初版：ResetStatic/ResetMethod/OnExitPlayMode/ReloadSafe 特性、PlaySession 会话边界、重置引擎、ExitingEditMode 关口、强制重载、适配器（含 UniTask 2.5.11、Input System 1.17.0 内置声明）、离线检查与基线、批处理入口、Project Settings 页、EditMode 测试。
- 延迟执行改用 `EditorCompat.RunOnNextUpdate`，替代 `delayCall`。原因：编辑器在后台由外部工具驱动时 `delayCall` 不执行，“取消后重载并进入 Play”会卡住。
- 在 Unity 6000.3.1f1 上验证：批处理流水线中编译无错误、无警告，EditMode 测试 27/27 通过；在用户打开的编辑器中通过 com.unity.pipeline 完成测试、连续 3 轮人工 Play→Stop 重入，以及强制重载链路验证。
