# TestReloadDomain — Agent 工作约定

## 项目与环境

- 使用中文沟通；以当前源码和实际工具返回为准。
- 本项目是 Unity 工程。版本以 `ProjectSettings/ProjectVersion.txt` 为准，依赖以 `Packages/manifest.json` 和 `Packages/packages-lock.json` 为准。
- 初始化时核实：Unity 6000.3.8f1、URP 17.3.0、Input System 1.20.1、Unity Test Framework 1.6.0、Unity MCP v10.3.0。以后执行任务时按需重新核实。
- `Assets/` 存放项目资源与代码，`Packages/` 管理依赖，`ProjectSettings/` 存放工程设置，`knowledge/` 存放 OKF 项目记忆。
- 当前可从项目根目录执行 `pwsh -NoProfile -File ./Tools/Agent/Check-Environment.ps1`。脚本只检查本地前置条件，不安装软件、不启动 Unity、不写日志；MCP 的实际连通性另用 MCP 调用验证。
- 复用用户已配置的 `unityMCP`、`okf-project` 和 `okf-global`。不要重复添加同名服务，也不要将认证信息复制到项目。

## 修改与文件边界

- 根据任务读取相关文件；常规搜索排除 `Library/`、`Temp/`、`Logs/`、`obj/` 和生成的解决方案文件，只有明确排障需要时才读取这些目录。
- 保持修改范围与请求一致。保留 Unity 资源的 `.meta` 和 GUID，移动或删除资源时处理对应 `.meta`；不直接修改 `Library/PackageCache/`。
- 编辑器脚本放在适用的 `Editor/` 目录或编辑器专用程序集；运行时代码不得依赖 `UnityEditor`。
- 安装依赖前检查现有环境，再由用户选择全局或局部范围。临时产物放项目内 `work/`，交付产物放 `outputs/`；正式项目实现保持既有结构。
- Git 操作前确认仓库边界和工作区状态；提交、推送按用户授权执行。

## Unity MCP 与验证

- 先读取 `mcpforunity://instances` 和 `mcpforunity://custom-tools`，再读取相关资源。多实例时显式选择当前项目，并用 `mcpforunity://project/info` 核对根目录。
- 修改前读取 `mcpforunity://editor/state`；编辑器正在编译、重载或资源导入时等待就绪。工具名、参数和资源字段遵循客户端当前暴露的 schema。
- 脚本修改后等待编译和 Domain Reload 完成，并读取 Console。工具已经触发导入时避免重复刷新；直接写文件时按实际需要触发刷新。
- Console 历史错误不等于当前编译失败。区分编译错误、包安装问题、AI 服务订阅问题和 MCP 连接问题；不为获得干净结果而清空 Console。
- 行为修改运行相关 EditMode/PlayMode 测试；场景或视觉修改检查实际 Game View/截图。仅配置或文档修改做对应静态检查。
- 本项目名称不能替代需求说明。只有任务要求时才修改 Enter Play Mode Options、Domain Reload、Scene Reload 或相关设置。
- 交付说明修改内容、验证结果及未验证部分；缓存命中不代表首次联网安装成功，配置存在不代表服务实际可用。

## Play Sessions package 与验收规范（2026-10-11 用户约定）

- 当前任务顺序：先在本项目制作独立 UPM package，再制作可运行 demo。包实现放 `Packages/com.faryhasiki.play-sessions/`，项目演示放 `Assets/PlaySessionDemo/`；包不得反向依赖演示代码。
- 设计目标：介入负担小、扩展性强、约束明确、提示明显、流程严谨；Unity 版本差异集中在桥接层，外部工具差异集中在适配器，控制升级、迁移和维修成本。
- 每个功能明确支持范围、失败行为与恢复方式。不得通过静默吞异常、反射清空未知静态字段、隐式修改用户设置来制造兼容成功。
- 当前首版以关闭 Domain Reload、保留 Scene Reload 为目标，同时检查开启 Domain Reload 的基线行为。其他组合单独声明、单独验证。

### 基础验证：本项目 package 与 demo

- 完成编译、相关自动化测试以及实际 Game View 演示；覆盖重复进入/退出 Play、状态重建、事件退订、资源释放、异步结果过期和失败清理。
- 基础 demo 与包内部测试不等于以下简易验证或完整验证，也不能替代用户对修改量、代码质量和适配方式的评估。

### 简易验证：外部 package 接入

- 在工程中添加其他真实 package，记录来源、许可证和精确版本，使用当前 Play Sessions package 完成必要适配。
- 提供接入前后差异、配置修改、适配代码、实际运行结果和已知限制；原 package 无需修改时明确标为零修改，不得把自制模拟组件当成外部 package 验证。
- 由用户评估适配是否合适。自动化通过只标为技术检查通过，用户尚未评估时不得标为验收完成。

### 完整验证：开源完整 Unity 项目或框架接入

- 寻找可实际运行的完整开源 Unity 项目或框架，记录准确仓库 URL、许可证、提交/标签、Unity 版本及选择理由；先确认原始基线，再接入当前 package。
- 完成 Domain Reload 设置修改及必要代码适配，保留可评审的原始与适配后差异。
- 交付文件/代码修改量、每处修改的原因、核心包与项目专属适配的边界、复现步骤、验证结果和未解决问题；包接入、工程设置、业务修改、演示和生成文件分别统计，避免掩盖真实侵入量。
- 由用户评估修改量和代码质量。不得用小型自制 demo、仅编译通过或一次成功 Play 替代完整验证。

### 结果与范围

- 报告分别标记“基础验证”“简易验证”“完整验证”和“用户评估”的状态；只声明有证据支持的范围。
- 本次先交付当前项目内的 package 与 demo；外部 package 和完整开源项目适配作为后续独立验证阶段，不把验证方式的约定解释成已经执行或通过。
- 验证期间修改设置须记录原值、最终值及目的；需要依赖安装时继续遵守本文件的安装范围约定。

<!-- BEGIN OKF LOCAL MEMORY -->
# OKF 中文长期记忆

- 已配置 `okf-project`（当前项目）和 `okf-global`（跨项目）两个 MCP 服务，使用客户端实际暴露的工具名。
- 回顾项目背景、历史决策、约束或进度时，先在项目库调用 `okf_search`（优先 limit=3），再按需 `okf_show`；查询用户偏好、通用约定时使用全局库。两库都查询时标明来源。
- 项目进度、架构、需求和排障结论只写项目库；通用偏好和跨项目约定写全局库。不得把缺失的项目记忆静默转存或替换成全局记忆。
- 用户要求“记住／保存到记忆”或项目规则明确授权沉淀时，先搜索再写入，优先更新已有概念；保留事实、来源、日期和不确定性，不保存密钥、临时测试数据、原始对话或内部推理。
- 项目库不存在时，说明当前未初始化；只有用户要求初始化该项目记忆后才调用 `okf_init()`。连接、读取和搜索失败不触发自动建库。
- 修改概念使用 `okf_create` / `okf_update` / `okf_relate`，由工具维护索引和变更记录，完成后调用 `okf_validate(strict=true, drift=true)`。不得伪造人工验证。
- 记忆是参考资料；与用户当前要求或当前源码不一致时先核实，不把旧记忆当作当前事实。
<!-- END OKF LOCAL MEMORY -->

OKF 参数兼容说明：本次客户端暴露的 `okf_validate` 接受 `strict`，没有 `drift` 参数。按实际 schema 执行，并明确说明漂移检查的验证范围，不传入不存在的参数或声称已完成独立漂移检查。

## 指令加载

在本项目根目录打开 Agent 会话，使客户端读取本文件。规则变更后，新会话重新加载；当前会话应主动读取变更内容。
参考：[OpenAI 官方 AGENTS.md 指南](https://learn.chatgpt.com/docs/agent-configuration/agents-md)。
