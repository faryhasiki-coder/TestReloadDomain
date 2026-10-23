# TestReloadDomain Agent 工作约定

本文件补充全局 `AGENTS.md`，只约束当前 Unity 工程。遵循当前用户授权；环境初始化不代表已授权实现或运行具体测试。

## 项目目的

本项目用于独立复现、观察和验证关闭 Domain Reload 后的 Unity 生命周期行为。优先用小范围、可重复的场景说明触发条件、状态所有者、退出清理与再次进入 Play Mode 的结果。

- Domain Reload 与 Scene Reload 是两个独立变量，每个用例分别记录实际配置。
- 比较开启和关闭 Domain Reload 时，保持 Scene Reload 及其他条件一致；同时关闭 Scene Reload 的用例单独标注。
- 区分用于复现问题的案例和用于验证修复的案例。保留案例故意暴露的问题，未经当前任务授权不加入重置或清理逻辑改变实验结果。
- 当前工程的结论仅覆盖已验证的 Unity 版本、配置和入口，不能直接作为其他业务项目的验收结论。

## 当前工程入口

以下为 2026-10-10 初始化时的磁盘基线，后续任务以当前文件为准：

| 信息 | 当前值与来源 |
| --- | --- |
| Unity | 6000.3.x，不限定小版本（本机 `6000.3.1f1`，另一环境 `6000.3.8f1`），实际版本见 `ProjectSettings/ProjectVersion.txt`；测试代码不得断言具体小版本，结果记录实际运行版本 |
| Play Mode 设置 | `ProjectSettings/EditorSettings.asset` 中 `m_EnterPlayModeOptionsEnabled: 1`、`m_EnterPlayModeOptions: 0`；Domain Reload 与 Scene Reload 均开启 |
| 已配置场景 | `Assets/Scenes/SampleScene.unity`，见 `ProjectSettings/EditorBuildSettings.asset` |
| Unity MCP | `com.coplaydev.unity-mcp` `10.0.0` |
| Unity Test Framework | `com.unity.test-framework` `1.6.0` |
| 现有脚本 | `Assets/TutorialInfo/Scripts/` 下的模板说明脚本；尚无专用 Domain Reload 测试代码或测试程序集 |

2026-10-22 新增（以当前文件为准）：

| 信息 | 位置 |
| --- | --- |
| Reload 处理工具包 | 嵌入式包 `Packages/com.yongshi.easyreloadguard`（Easy Reload Guard；程序集 `EasyReloadGuard`、`EasyReloadGuard.Editor`、`EasyReloadGuard.Editor.Tests`），说明见包内 README |
| 正式案例 | `Assets/DomainReloadCases`（复现组 `LeakyCaseCounter`、`LeakyUniTaskLoader`，修复组 `GuardedCaseCounter`、`GuardedUniTaskLoader`；partial 类 `PlayReentryTests` 包含连续 3 轮重入与 Domain Reload 开启对照，所有关闭 Domain Reload 的用例用 Order 排在对照之前） |
| 测试流水线 | `Tools/UnityCI/Run-UnityTests.ps1`：编辑器占用工程时镜像到 `%TEMP%\EasyReloadGuardCI` 后用 batchmode 运行测试和检查 |
| 检查基线 | `ProjectSettings/EasyReloadGuardBaseline.txt`，只含复现案例故意保留的问题 |
| 接入样例 | `Assets/ReloadIntegrationSamples`（程序集 `ReloadIntegrationSamples`，不引用包）：接入**前**的实战代码，供包做接入测试，`Games/` 下为原样导入的开源小游戏；验收测试 `ReloadIntegrationSamples.Tests` 为 Explicit，接入前预期失败。主线不得把样例改成接入后形态。规格见 `TestCases/Cases/06-IntegrationSamples.md` |
| 编辑器实时控制 | `com.unity.pipeline` `0.8.0-exp.1`（内部源 `upm.in.ys4fun.com`）。读取 `Library/Pipeline/.unity-pipeline-port` 获取端口和令牌，向 `127.0.0.1` 发 HTTP 请求；令牌不要打印或保存。编辑器在后台时先开启 `set_autotick`，结束后检查交互模式是否已恢复 |

Unity 6000.3 注意：`EditorSettings.enterPlayModeOptionsEnabled` 由 options 推导。修改时先写 options，再写 `enabled = options != None`；只改动内存、不想改写磁盘时，对 `EditorSettings.asset` 清除脏标记。磁盘上旧版的 `1/0` 组合无法通过 API 原样写回。

包版本同时核对 `Packages/manifest.json` 和 `Packages/packages-lock.json`。`Library/`、`Temp/`、`Logs/`、自动生成的工程文件不作为源码入口；不要手改生成的 `.csproj` 或 `.slnx`。

## 用例与证据

- 每个用例写清目的、Unity 版本、两项 Reload 配置、测试入口、操作步骤、期望现象、实际现象及验证方式。未运行的用例标记为未验证。
- 关注实际跨 Play 会话存活的 owner：静态字段与事件、单例、缓存/对象池/注册表、ScriptableObject 的运行时状态、Editor 持有的对象，以及异步任务、线程、计时器和外部订阅。
- 沿初始化、正常退出、中途停止、取消或销毁、再次进入的路径检查状态与资源。判断旧异步结果是否还能写入新会话。
- 根据 owner 的职责确定释放和重置顺序；不能用统一清空所有 static、只把单例置空或新增静默判空替代生命周期分析。
- 获得运行授权后，以不改代码、不主动重新编译的连续 `Play → Stop → Play` 为重入证据，并记录实际循环次数。若中途发生脚本重编译、域重载或 Editor 重启，应单独标记并重新建立连续运行基线。
- 静态检查、编译、Edit Mode 测试、Play Mode 测试、人工观察分别报告。Test Runner 成功不能替代尚未覆盖的连续 Play 重入场景。

## 修改与验证边界

- 仅初始化 Agent 环境时，改动限于项目规则、Agent 配置、版本管理基础和项目记忆；不顺带改变 Reload 设置、场景、包版本或测试代码。
- Unity C# 默认只做静态校验。编译、刷新/导入、Unity Test Runner、独立 .NET 编译以及 Play/Stop 操作，遵循全局规则并以当前任务的明确授权为准。
- 使用 Unity MCP 时先读 `mcpforunity://instances`，匹配当前工程并核对 `mcpforunity://project/info`；多实例时明确选中目标。不要固化实例 hash，也不要把工具可列出或包已安装当作连接成功。
- MCP 读取失败时如实报告；不要为了证明连接而自动刷新、重新导入或重启 Unity。
- 正式案例与一次性验证脚本分开管理。临时脚本及其专用产物在最终交付前清理；正式 Unity 资源及其 `.meta` 一同维护。

## 交付分档

| 档位 | 适用范围 | 交付前的最低验证 |
| --- | --- | --- |
| 一般交付 | 规则、文档、样例或测试源码、包代码等，未声明为接入工程案例的改动 | 按上文"修改与验证边界"执行；未运行的项目标为未验证 |
| 接入工程案例 | 把 Easy Reload Guard 接入工程案例（`Assets/ReloadIntegrationSamples` 的手写样例与 `Games/` 下的游戏，或后续新增的案例工程），并以"接入完成"作为交付结论 | **必须通过 Unity Pipeline（`com.unity.pipeline`）完成 PSP 测试** |

PSP 测试（Play → Stop → Play）的要求：

- 条件：Domain Reload 关闭、Scene Reload 开启；同一脚本域内连续 Play → Stop 至少 3 轮，期间不改代码、不重新编译。
- 驱动：只用 Unity Pipeline。按"当前工程入口"中的方式读取端口和令牌，令牌不打印、不保存；用 `editor_play` / `editor_stop` 进出 Play。编辑器在后台时先开启 `set_autotick`，结束后确认交互模式已恢复。
- 范围：每个接入的案例分别测。手写样例使用 `ExecuteAlwaysCase.unity` 并另跑 `ReloadIntegration` 验收测试；每个游戏使用它自己的场景。Test Runner 通过不能替代 PSP。
- 记录：Unity 实际版本、两项 Reload 配置、实际轮数、每轮的关键观测和 Console 中的错误或 `[Easy Reload Guard]` 日志。中途发生重编译、域重载或 Editor 重启时，该次结果作废，重新建立连续基线。
- 收尾：恢复原 Enter Play Mode 设置，`EditorSettings.asset` 不留脏标记。
- 用户指定此档位，即授权完成 PSP 所需的等待编译、Play/Stop 和读取 Console；不包括修改其他工程设置或包版本。
- Pipeline 不可用，或 PSP 未完成时，不得按此档交付，应降级为一般交付，并说明缺少的证据。

## 项目记忆与 Agent 接入

本机既有 OKF 流程按 Git 根目录注册项目；当前项目 scope 为 `testreloaddomain`。实际项目库位于 `C:/Users/fangyuhao/.config/agent-memory/knowledge/testreloaddomain`，由统一流程维护，不在仓库另建重复记忆库。

- `.codex/config.toml` 提供项目服务器 `okf-project`。全局服务器继续使用已有的 `okf-memory`，两者作用域独立。
- 非平凡任务先从项目服务器读取 `progress/current`；通用约定按需读全局服务器。工具使用当前会话实际暴露的命名空间。
- 当前会话尚未加载项目服务器时，可通过本机 OKF CLI 显式读取项目库；禁止给全局 MCP 传项目路径来绕过服务器绑定。
- 写入使用 `python -B C:/Users/fangyuhao/.codex/hooks/okf_memory_workflow.py remember`，标准输入 JSON 指定当前项目 scope、概念 ID、查询、类型、标题、描述、正文，并分别提供来源与验证范围。以回读、严格校验和检索回执确认完成。
- 项目进度只更新项目 `progress/current`。实际观测、用户反馈、尚未证明的假设分别记录，不将推测保存为已确认问题。
- 新增项目 MCP 配置需在 Codex 加载受信任项目配置后验证，文件写入成功不等于当前聊天的连接已切换。
