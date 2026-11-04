---
name: easy-reload-guard
description: "在接入了 Easy Reload Guard（com.yongshi.easyreloadguard）的 Unity 工程中写代码或处理检查结果时使用：新增或修改静态字段、单例、静态事件、注册表/缓存/对象池、ScriptableObject 运行时状态、异步任务、线程或计时器，处理 Easy Reload Guard 的检查报告、停止后检查报告或 [Easy Reload Guard] 日志，以及排查关闭 Domain Reload 后才出现的问题。工程的 Packages/manifest.json 中没有 com.yongshi.easyreloadguard 时不使用。"
---

# Easy Reload Guard 约束

关闭 Domain Reload 后，进入 Play 不再重新加载脚本，静态状态会带到下一次 Play。本包在 Editor 中按声明恢复静态状态、在停止 Play 时调用释放方法，并检查遗漏。写代码时要让每个新的静态状态都有明确的处理方式，同时**不改变打包后的游戏逻辑**。

## 开始前

1. 确认 `Packages/manifest.json` 有 `com.yongshi.easyreloadguard`。
2. 找到实际安装的包目录：`Packages/com.yongshi.easyreloadguard/`（嵌入）或 `Library/PackageCache/com.yongshi.easyreloadguard@*/`。以该目录中的 `README.md`、`Documentation~/接入手册.md`、`AGENTS.md` 为准；本技能与包版本不一致时，以包内文档为准。
3. 看工程已有的写法：单例基类、事件中心、Editor 适配器所在目录（搜索 `: ReloadAdapter`）。优先沿用，不新建第二套。

## 三条不能违反的约束

1. **不影响打包后的逻辑。** 业务代码只允许：
   - 在**已有**成员上加特性：`[ResetStatic]`、`[ResetMethod]`、`[OnExitPlayMode]`、`[ReloadSafe("原因")]`，以及为此需要的 `EasyReloadGuard` 程序集引用（asmdef 需手动添加）。
   - 新增**只在 Editor 编译**的重置方法：方法连同 `[ResetMethod]` 整体放在 `#if UNITY_EDITOR` 中。
   
   不要为本包在业务中新增方法、Unity 消息、参数、判断分支，也不要调用 `PlaySession.RegisterReset` 一类的登记接口，不要调整业务的初始化阶段。
2. **释放是业务自己的事。** 订阅、线程、计时器、Socket、下载、原生资源在打包后切场景、登出时同样要释放。缺少释放是业务缺陷，修业务本身；释放方法已经存在之后，才在它上面加 `[OnExitPlayMode]`。不要为本包新建 `[OnExitPlayMode]` 方法。
3. **异步由发起者取消。** 不要让任务比发起它的对象活得更久，本包不替业务取消。

## 写新代码时：每个静态状态选一种处理

先问能不能不用静态：状态放在负责它的对象实例上，随对象创建和销毁，最省事。必须用静态时按下表选择，详细判断见 [references/patterns.md](references/patterns.md)。

| 静态状态 | 处理 |
| --- | --- |
| 值永远不变 | `const` 或 `static readonly`，不需要特性 |
| 单例、普通字段，下一次 Play 一定会重新赋值 | `[ResetStatic]`；有非默认初值的数值或 string 用 `[ResetStatic(ResetMode.InitialValue)]` |
| 只在运行中增删的 `static readonly` 集合 | `[ResetStatic(ResetMode.Clear)]` |
| 静态事件、自动属性 | `[field: ResetStatic]`，前提是订阅方已在销毁或退出时成对取消 |
| 几个字段要一起恢复，或需要恢复初始内容 | `#if UNITY_EDITOR` 中的 `[ResetMethod]` 静态无参方法，只恢复本类型的静态字段 |
| 有意保留到下一次 Play（纯计算缓存、编辑器偏好、固定元数据） | `[ReloadSafe("具体原因")]`，逐个字段写 |
| 订阅、线程、计时器、Socket、原生资源 | 业务自己的停止方法成对释放，再加 `[OnExitPlayMode]` |
| 异步任务 | `destroyCancellationToken`、服务自己的 `CancellationTokenSource` 或 `Application.exitCancellationToken`；`await` 之后写状态前检查令牌；避免 `async void` |
| 泛型单例 | 在泛型定义的字段上加 `[ResetStatic]`；业务直接使用的具体泛型类型要在 Editor 适配器中补 `ResetField` |
| 第三方或外部官方包的静态状态 | 不改源码，写 Editor 适配器；公司内部维护的包改包源码，先与用户确认 |

常见错误：

- 对只在静态构造或字段初值中填充的表用 `Clear`：静态构造不会再执行，清空后永久为空。
- 对只有初值、没有地方重建的引用用 `[ResetStatic]`：下一次 Play 拿到 null。
- `static float s = 1f` 加 `[ResetStatic]`：会被重置成 0，要用 `InitialValue`。
- 只把单例或线程字段置 null：已在运行的工作不会停，监听也还在别处的表里。
- 用类型级、程序集级 `[ReloadSafe]`，或整批记为已知遗留，来掩盖没确认过的问题。
- 手改生成代码：改生成模板或生成器。

## 排查“关闭 Domain Reload 后才出错”

1. 先用开启 Domain Reload、相同的操作复现一次。开启时也会出现，就是原有问题，不归因于本包或 Domain Reload，只记录，不修。
2. 只有关闭时才出现的，再按上表处理；能用 Editor 适配器把静态状态恢复成脚本刚加载时的样子，也算处理完成。
3. 涉及业务逻辑修改或包源码修改时，先向用户说明现象、原因、证据和方案，等确认后再改。

处理检查报告中的具体问题，见 [references/report-triage.md](references/report-triage.md)。

## 验证

依次进行，分别报告结果，没做的写“未验证”：

1. 编译通过，Console 没有 `[Easy Reload Guard]` 错误。
2. 运行检查（菜单 `Tools/Easy Reload Guard/检查静态变量并查看结果`，或命令行 `-executeMethod EasyReloadGuard.Editor.ReloadGuardCli.Audit`）：没有新的错误或警告，没有“特性用法错误”“已加的重置会破坏下一次 Play”“重置方法漏掉了字段”。
3. 关闭 Domain Reload、保持 Scene Reload 开启，在同一次脚本加载内连续 Play → Stop 至少 2 轮，期间不改代码、不重新编译。看 `Library/EasyReloadGuard/ResidueReport.md`：没有“仍引用已销毁的对象”，没有“比上次停止时增加”。
4. 走到改动涉及的入口（登出重进、加载中途停止、下载中停止等）。主流程通过不代表这些入口通过。

用脚本切换 Domain Reload 时，要用 `SerializedObject` 同时写入 `ProjectSettings/EditorSettings.asset` 的 `m_EnterPlayModeOptionsEnabled`，结束后清除脏标记：Unity 6000.3 实际按这个字段决定是否重新加载脚本，只改 `EditorSettings` 接口不生效。

## 不要做

- 不要手工编辑 `ProjectSettings/EasyReloadGuardBaseline.txt`；需要更新时，确认问题后用设置页的“把当前问题记为已知遗留”重新生成。
- 不要为了让检查通过而排除包或程序集；只在自己本机安装、不随项目提交的插件，在插件目录放 `.easyreloadguard-exclude` 标记。
- 不要关闭 Scene Reload，也不要修改用户的 Enter Play Mode 设置并保存。

## 给人看的输出

报告、日志、文档、提交说明都写给同事看：用完整的问题描述，不用规则编号（如 RG003）、“基线”、“会话”、“owner”、“PSP”等内部代称。遗留清单文件中的编号对照见 [references/report-triage.md](references/report-triage.md)，只在读那个文件时使用。
