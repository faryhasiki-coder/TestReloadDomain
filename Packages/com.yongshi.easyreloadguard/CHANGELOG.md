# Changelog

## [0.4.2] - 2026-11-03

给 Agent 的索引与约束 Skill（参考 Unity MCP 的“Install Skills”）：

- 新增包根目录 `AGENTS.md`：目录、主要文件职责、运行时产生的文件、修改本包时的约束。
- 新增 Agent Skill `easy-reload-guard`，源文件在 `Skills~/easy-reload-guard`（Unity 不导入）：写代码时的三条约束、每种静态状态的处理方式、排查“关闭 Domain Reload 后才出错”的方法、验证步骤与常见错误；`references/` 中是写法参考和检查报告处理方法。
- 安装：设置页新增“Agent Skill”分组，显示 Claude Code 与 Codex 的安装状态并可安装或更新；菜单新增 `Tools/Easy Reload Guard/安装或更新 Agent Skill`。从已安装的包复制到 `~/.claude/skills/easy-reload-guard`、`~/.codex/skills/easy-reload-guard`，Skill 与包内容一致；目标目录中的 `.easyreloadguard-skill` 记录内容哈希，不是本包安装的同名目录和目录联接不覆盖。
- 本机用过的工具还没有最新内容的 Skill 时，Console 提示一次（每份内容每台机器一次）。不用模态对话框，避免卡住由外部工具驱动的编辑器。
- 新增 `SkillInstallerTests`。

项目适配器不再按版本号判断是否需要重新复核，改为比对静态字段（基于包开发的项目包更新频繁，又常用 Git tag 发版而很少改 `package.json`：版本号在无关更新时误报，在不改版本时漏报。例如 Origins 中 YSRP 的 `package.json` 一直是 1.0.0，tag 已从 1.1.68 升到 1.1.69）：

- 新增问题类型“跳过检查的代码中静态字段有变化”（警告，可记为已知遗留）：项目适配器用 `HandlesType`、`SelfHandled` 跳过检查的每个类型，记录静态字段签名（名称、类型、是否只读；常量和编译器生成的缓存不计）。遗留清单中这一行的标识附带签名，记为已知遗留即表示已复核；字段增删或类型变化时原来那一行失效，报告列出与上次记录相比新增、删除的字段。只比对字段定义，不比对方法体。
- 项目适配器的 `VerifiedVersions` 改为可选（基类给出空默认值），只作记录，不再产生“适配器目标的版本未复核”。内置适配器针对 Unity 官方发布的包，仍按 `package.json` 版本比对。
- 报告与设置页的适配器“复核方式”一栏：项目适配器显示“比对静态字段”，内置适配器显示版本是否已复核；“已安装版本”显示来源，如 `Git 1.1.69（提交 70699041，package.json 1.0.0）`、`1.0.0（嵌入）`。
- **迁移**：升级后第一次检查，项目适配器跳过的每个有静态字段的类型都会报一次（不阻止进入 Play）。这些声明当初已经复核过，确认无误后用“把当前问题记为已知遗留”一次记下即可；之后只有静态字段真的变化时才会再报。原来项目适配器因版本不符产生的“版本未复核”不再出现。
- Skill 安装标记改为记录内容哈希（第一行哈希、第二行包版本），内容变化即提示更新，不依赖包版本号。
- 新增静态字段签名、与上次记录比对、项目适配器不比对版本的测试。

主工具栏新增 Domain Reload 状态图标：

- 绿色闪电表示 Domain Reload 已关闭（快速），黄色闪电表示已开启（常规）。以 `EditorSettings.asset` 的序列化开关为准，与接口不一致时显示黄色并在悬停提示中说明。
- Enter Play Mode 设置没有变化事件：每秒比对一次，变化时调用 `MainToolbar.Refresh`。图标在代码中绘制，不增加资源文件，脚本重新加载前销毁。
- 点击打开 Enter Play Mode 设置（不直接切换，避免误改共享的 `EditorSettings.asset`），右键打开本包设置。第一次加载时自动显示一次，与“重载并 Play”相同。

- 静态校验，未编译、未运行测试。

## [0.4.1] - 2026-11-03

编辑器界面整理（检查、重置与日志行为不变，设置字段与序列化格式不变）：

- 主工具栏 Play 按钮旁新增“重载并 Play”按钮（Unity 6000.3 `MainToolbarElement`），等同菜单“重新加载脚本并进入 Play”；右键可只重新加载脚本或打开设置页。6000.3 的第三方工具栏元素默认隐藏，本包在第一次加载时用 `EditorWindow.TryGetOverlay` 把它显示一次，并在 `UserSettings` 记下（每人每工程一次），之后在工具栏右键菜单中隐藏不会被改回。重置失败的 Console 提示改为指向该按钮。
- `Tools/Easy Reload Guard` 菜单：“运行检查”改为“检查静态变量并查看结果”，“设置与报告”改为“打开设置”；“把当前问题记为已知遗留”移出菜单，只保留在设置页（需先看过检查结果再决定）。
- 设置页重排：顶部状态卡显示包版本、Domain Reload / Scene Reload 状态色块，并可直接打开 Enter Play Mode 设置；设置项分为“进入 Play 前”“停止 Play 后”“检查范围”“日志”四个可折叠分组，全部设置项带悬停说明；“停止后检查也读取有静态构造的类型”在未开启残留检查时置灰。
- “进入 Play 前检查”的选项显示为“不检查 / 只输出警告 / 阻止进入 Play”（原显示 Off / Warn / Block，序列化值不变）。
- 操作区按钮带图标，报告位置改为一组小按钮。检查结果显示错误、警告、提示、已知遗留的计数色块；问题按“级别 → 问题类型”分组为可折叠卡片，组内逐条列出成员、原因和读写位置；已知遗留分组默认折叠并排在最后；适配器与扫描的程序集收进折叠项。去掉报告区内层滚动条，随设置页整体滚动。
- 静态校验，未编译、未运行测试。

目录排除标记（只在本机安装的插件不必写进共享设置）：

- 新增 `.easyreloadguard-exclude`：放在某个目录中，该目录及子目录里的程序集（有 asmdef 的源码程序集或 DLL）不参与检查，没有 asmdef 的代码编进 `Assembly-CSharp`，不能单独跳过；文件内容（可选）作为原因。从程序集位置逐级向上查找，到包根目录（含 `package.json`）或工程根目录为止，工程根目录的标记不生效。文件以点开头，Unity 不导入、不生成 `.meta`。
- 检查报告开头与设置页列出按标记跳过的程序集、标记目录和原因。设置中的“排除的包 / 排除的程序集”不变。
- 新增测试覆盖逐级查找、原因读取、包根目录与工程根目录边界。

Domain Reload 状态判断改为以序列化字段为准：

- 6000.3.1f1 实测：`EditorSettings.enterPlayModeOptionsEnabled` 的读取值只由 options 推导，写入也不改 `m_EnterPlayModeOptionsEnabled`；进入 Play 时 Unity 按该序列化字段决定。字段为 false 时接口显示已关闭 Domain Reload，脚本却照样重新加载（Origins 验收时遇到，本工程用 `AppDomain` 探针复现：字段为 false 时重新加载，为 true 时不重新加载）。
- `EditorCompat` 改为读取 `EditorSettings.asset` 的序列化字段判断“是否启用”，读不到时退回接口值。重置、Play 前检查、停止后检查、报告与设置页的状态都随之以 Unity 实际行为为准。
- 接口与序列化字段不一致时：设置页显示警告，检查报告和停止后检查的 Enter Play Mode 一行附带说明，进入 Play 时 Console 输出警告。

## [0.4.0] - 2026-11-03

面向人的输出去掉内部代称，改为直白描述（规则编号、遗留清单的行格式与 API 不变，已有清单继续有效）：

- 检查报告、Console 日志、设置页不再显示 `RG0xx` 编号，改为问题类型名称（如“静态事件或委托未处理”“声明保留的字段仍在 Play 中被修改”）。报告的错误/警告/提示/已知遗留各节按问题类型分小节；“工作清单”改名为“按类型汇总”，“规则”列改为“问题”列；成员显示为 `命名空间.类型.成员（程序集）`。
- “基线”在界面与文字中统一称为“已知遗留问题清单”：菜单“将当前遗留问题写入基线”改为“把当前问题记为已知遗留”，统计中的“已基线”改为“已知遗留”，“阻断”并入“待处理的错误”说明。文件仍为 `ProjectSettings/EasyReloadGuardBaseline.txt`，重新生成时按问题类型分组并加注释行。
- “上下文”改为“读写位置”，“类型初始化”改为“静态初始化”；“会话”“owner”“PSP”“重载域”等说法改为“下一次 Play”“负责的代码”“连续 Play → Stop”“重新加载脚本”。菜单“立即重载域”“重载域并进入 Play”改为“立即重新加载脚本”“重新加载脚本并进入 Play”；设置项“重置失败后强制重载”“退出 Play 后探测残留”“残留探测读取有静态构造的类型”改为“重置失败后重新加载脚本”“停止 Play 后检查残留”“停止后检查也读取有静态构造的类型”（序列化字段名不变，已有设置继续有效）。
- 停止后检查报告的两节改名为“仍引用已销毁的对象”“比上次停止时增加”，并用“第 N 次 Play / 本次脚本加载后第 N 次停止”代替“会话 #N / 本域第 N 次回到编辑模式”。
- README 与接入手册按“为什么 → 怎么做 → 怎么看报告”重写，去掉编号与历史验证记录（验证记录只保留在本文件）；编号对照放在 README 附录，仅供阅读遗留清单文件时使用。
- 静态校验，未编译、未运行测试。

## [0.3.4] - 2026-11-03

- 检查范围排除本包运行时程序集 `EasyReloadGuard`：`PlaySession` 的会话编号与登记表由本包在 Play 边界自行管理、只在 Editor 中存在，并由包自身测试覆盖。0.3.3 的 RG015 会把这 3 个字段报给每个接入项目并写进项目基线。
- 0.3.3 的 RG015 在 Unity 6000.3.1f1 batchmode 下编译通过，`EasyReloadGuard.Editor.Tests` 55/55 通过；本工程修复组 `GuardedUniTaskLoader` 的 2 条 RG015 复核后写入基线（写入为案例设计，原因仍成立）。

## [0.3.3] - 2026-11-03

`[ReloadSafe]` 声明后的漂移检测（此前标注后字段被直接跳过，之后新增的会话内写入无法发现）：

- 新增 RG015（警告，可入基线）：字段级或类型级 `[ReloadSafe]` 覆盖的可变静态字段，在声明类型内、类型初始化以外仍有赋值或内容修改时报告，并附声明的原因与写入位置，提示复核原因是否仍然成立。不可变的 readonly 字段、只在类型初始化时填充的内容、缺少原因（已报 RG011）的声明不报告。程序集级声明视为整体复核，不检查。
- RG015 的基线键在“规则|主体”后附加全部写入位置（`|赋值=…;修改=…`，排序、不截断）：合法缓存复核后写入基线；之后写入位置增减时条目失效（旧条目报 RG030），需要重新复核。同一方法内新增的写入不改变证据。
- 残留探测范围加入 RG015 字段：声明保留的集合跨会话增长或引用已销毁对象时同样报告。
- 开销：只对带 `[ReloadSafe]` 的类型做 IL 分析，结果与其他规则共用按类型的缓存；检查仍按域缓存，连续 Play 不重复计算。
- 静态校验，未编译、未运行测试。

## [0.3.2] - 2026-11-02

内置适配器版本复核（Origins 收尾被 4 条不可入基线的 RG021 阻断；以两工程中较高的版本为复核基准，已验证的旧版本保留）：

- Input System 追加 1.20.0：`InputSystemEditorInitializer.OnPlayModeChange` 的 Play 边界处理与 1.17.0 一致；1.20.0 新增官方的关闭 Domain Reload 支持（ISX-2411），`RuntimeInitialize(BeforeSceneLoad)` 重新执行编辑器初始化，`InputActionState`/`PlayerInput` 全局状态在关闭 Domain Reload 时于 BeforeSceneLoad 重置。
- Collections 追加 2.6.3：运行时源码差异为容量上限检查与 `MaxCapacity` 常量，无静态状态变化。
- AI Navigation 追加 2.0.11：官方变更为警告与 UI 修复；内部镜像另含 `NavMeshModifierCylinder`，其注册表 OnEnable/OnDisable 成对维护，临时体积列表只由 Editor 烘焙成对创建与清理。
- Newtonsoft Json 追加 3.2.1：`Newtonsoft.Json.dll` 与 AOT 版本和已复核的 3.2.2 字节一致。
- README 内置适配器表同步。

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
