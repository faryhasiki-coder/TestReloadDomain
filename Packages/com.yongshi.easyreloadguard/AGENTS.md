# Easy Reload Guard：Agent 索引

本文件给 Agent 定位本包的代码和文档。用法和原理见 [README](README.md)，处理检查报告见 [接入手册](Documentation~/接入手册.md)，版本变化见 [CHANGELOG](CHANGELOG.md)。

- 包名 `com.yongshi.easyreloadguard`，版本以 `package.json` 为准，Unity 6000.3。
- 作用：关闭 Domain Reload 时，在 Editor 中按声明恢复静态状态、停止 Play 时调用释放方法，并检查遗漏。Player 中不参与任何逻辑。

## 在接入本包的工程中写代码

使用 Agent Skill `easy-reload-guard`，源文件在本包 [Skills~/easy-reload-guard](Skills~/easy-reload-guard/SKILL.md)。安装方式：`Project Settings/Easy Reload Guard` 的“Agent Skill”分组，或菜单 `Tools/Easy Reload Guard/安装或更新 Agent Skill`，会复制到 `~/.claude/skills/easy-reload-guard` 与 `~/.codex/skills/easy-reload-guard`。Skill 内容变化后（按内容哈希判断，不依赖包版本号），设置页和 Console 会提示重新安装。

不使用 Skill 时，至少遵守以下几条（细节见 README 第 2 节）：

1. 始终适用：本包在业务代码中只以特性、`#if UNITY_EDITOR` 中的 `[ResetMethod]` 重置方法和程序集引用出现，运行时流程不调用本包接口；订阅、线程、资源的释放与异步取消由业务负责；`[OnExitPlayMode]` 只加在业务流程本身也会调用的停止方法上。
2. 写新代码：按 README 2.2 的编码规范一次写对（状态放实例、可变静态声明时就加特性、初始化可重复、订阅成对、资源有停止方法、令牌沿调用链传递），不产生新的检查问题。
3. 接入已有代码：只在已有成员上加特性，或新增 Editor 重置方法；不为本包新增业务方法、分支、登记调用；业务缺陷确认后修业务本身。

## 目录

| 路径 | 内容 |
| --- | --- |
| `Runtime/` | 运行时程序集 `EasyReloadGuard`：只有特性和 `PlaySession` 上的 `[Conditional("UNITY_EDITOR")]` 方法 |
| `Editor/Core/` | 进入和停止 Play 时的执行、强制重新加载、停止后检查、与 Unity 版本相关的调用 |
| `Editor/Audit/` | 静态检查：扫描范围、规则、读写位置分析、报告、遗留清单、命令行入口 |
| `Editor/Adapters/` | 适配器接口与内置适配器 |
| `Editor/Settings/` | 设置、设置页、菜单、工具栏按钮、Skill 安装 |
| `Tests/Editor/` | `EasyReloadGuard.Editor.Tests`（EditMode） |
| `Documentation~/` | 接入手册（目录名以 ~ 结尾，Unity 不导入） |
| `Skills~/easy-reload-guard/` | Agent Skill 源文件（同上，不导入，不需要 .meta） |

## 主要文件

| 文件 | 职责 |
| --- | --- |
| `Runtime/ResetAttributes.cs` | `ResetMode`、`[ResetStatic]`、`[ResetMethod]`、`[OnExitPlayMode]`、`[ReloadSafe]` |
| `Runtime/PlaySession.cs` | 重置登记与 `RequestDomainReload`；Player 中调用被编译器删除 |
| `Editor/Core/ReloadGuardBootstrap.cs` | 订阅 Play 状态变化，分发到关口、重置与释放 |
| `Editor/Core/PrePlayGate.cs` | 进入 Play 前：待处理的重新加载请求、按设置检查并阻止或警告 |
| `Editor/Core/ResetPipeline.cs` | 进入 Play 时执行重置，停止 Play 时执行释放；失败汇总并登记重新加载 |
| `Editor/Core/ResetPlan.cs` | 从特性和适配器生成重置计划，按脚本加载缓存；只读反射，不触发静态构造 |
| `Editor/Core/ResetSteps.cs` | 单个字段的重置步骤，`InitialValue` 初值的记录 |
| `Editor/Core/ResetRules.cs` | 特性用法是否合法，泛型字段展开到具体类型 |
| `Editor/Core/ResidueProbe.cs` | 停止 Play 后检查：已销毁引用、订阅和集合增长，写 `ResidueReport.md` |
| `Editor/Core/ForceReload.cs` | 重新加载脚本、重新加载并进入 Play |
| `Editor/Core/EditorCompat.cs` | **所有依赖 Unity 版本的调用**：Enter Play Mode 设置（以序列化字段为准）、取消进入 Play、延迟执行、引擎事件容器读取、工具栏显示 |
| `Editor/Audit/ReloadAudit.cs` | 检查入口：扫描程序集、逐类型套用规则、写 `AuditReport.md` |
| `Editor/Audit/AuditScope.cs` | 扫描范围：排除的包和程序集、适配器自行处理、目录排除标记 `.easyreloadguard-exclude` |
| `Editor/Audit/FieldAccessAnalyzer.cs`、`InitializerPurity.cs` | 只读 IL 分析：字段读写位置；静态构造是否只做纯初始化 |
| `Editor/Audit/Finding.cs` | 规则编号 `RuleIds` 与面向人的问题类型名 `RuleIds.Title()` |
| `Editor/Audit/Baseline.cs` | 已知遗留问题清单 `ProjectSettings/EasyReloadGuardBaseline.txt` 的读写 |
| `Editor/Audit/AuditReport.cs` | 报告模型与 Markdown 输出 |
| `Editor/Audit/AdapterReview.cs` | 项目适配器跳过检查的类型：记录静态字段签名，与遗留清单中上次的记录比对（不按版本号判断） |
| `Editor/Audit/ReloadGuardCli.cs` | `-executeMethod EasyReloadGuard.Editor.ReloadGuardCli.Audit`（CI） |
| `Editor/Adapters/ReloadAdapter.cs` | 已安装包的来源与版本 `InstalledPackage`（只用于显示和内置适配器）；`ReloadAdapter` 与 `ReloadAdapterBuilder`（`ResetField`、`InvokeOnReset`、`InvokeOnExitPlayMode`、`OnReset`、`OnExitPlayMode`、`HandlesType`、`SelfHandled`、`KnownIssue`、`RequireDomainReload`） |
| `Editor/Adapters/BuiltInAdapters.cs` | 内置适配器及其复核版本和依据 |
| `Editor/Settings/ReloadGuardSettings.cs` | 项目设置 `ProjectSettings/EasyReloadGuardSettings.asset` |
| `Editor/Settings/ReloadGuardSettingsProvider.cs`、`ReloadGuardStyles.cs` | 设置页 |
| `Editor/Settings/ReloadGuardMenu.cs`、`ReloadGuardToolbar.cs` | 菜单；主工具栏的 Domain Reload 状态图标（代码绘制的闪电，每秒比对设置、变化时 `MainToolbar.Refresh`）与“重载并 Play”按钮 |
| `Editor/Settings/ReloadGuardSkillInstaller.cs` | Agent Skill 安装、内容哈希标记、内容变化时的 Console 提示 |

## 运行时产生的文件

| 文件 | 说明 |
| --- | --- |
| `ProjectSettings/EasyReloadGuardSettings.asset` | 项目设置，纳入版本管理 |
| `ProjectSettings/EasyReloadGuardBaseline.txt` | 已知遗留问题清单，由设置页按钮生成，纳入版本管理 |
| `Library/EasyReloadGuard/AuditReport.md` | 最近一次检查报告 |
| `Library/EasyReloadGuard/ResidueReport.md` | 最近一次停止后检查报告 |
| `~/.claude/skills/easy-reload-guard`、`~/.codex/skills/easy-reload-guard` | 安装的 Skill，目录中的 `.easyreloadguard-skill` 第一行是安装时的内容哈希，第二行是包版本 |

## 修改本包时

- **不能影响打包后的逻辑**：运行时程序集不加可读取的状态、启动入口或回调；新功能放在 Editor 程序集。
- 依赖 Unity 版本语义的调用集中写在 `EditorCompat`，并在 README 第 10 节记录实测结论。
- 面向人的文字（报告、Console、设置页、文档）用问题类型名和直白描述，不用规则编号和内部代称；规则编号只出现在遗留清单的行和 README 附录。
- 新增规则或改变遗留清单的行格式时，保证已有清单继续有效，或在 CHANGELOG 中写明迁移方式。
- 新文件要有 `.meta`，`~` 结尾的目录除外。
- 使用方多是基于包开发、包更新频繁，且常用 Git tag 或提交号引用、很少改 `package.json` 版本。需要识别“内容是否变了”时按内容判断（如静态字段签名、文件哈希），不要依赖版本号。
- 每次修改：在 `package.json` 更新版本，在 CHANGELOG 记录改动和实际验证情况（编译、测试、实测分别写，没做的写“未验证”）；同步 README、接入手册和 Skill 中受影响的内容。
- 验证：编译；运行 `EasyReloadGuard.Editor.Tests`；涉及 Play 流程时，关闭 Domain Reload 连续 Play → Stop 实测。用脚本切换 Domain Reload 时要同时写入 `EditorSettings.asset` 的 `m_EnterPlayModeOptionsEnabled`。
