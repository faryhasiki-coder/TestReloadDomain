---
name: easy-reload-guard
description: "在接入了 Easy Reload Guard（com.yongshi.easyreloadguard）的 Unity 工程中写代码或处理检查结果时使用：编写或修改包含静态字段、单例、静态事件、注册表/缓存/对象池、ScriptableObject 运行时状态、异步任务、线程或计时器的代码（按编码规范一次写对），处理 Easy Reload Guard 的检查报告、停止后检查报告或 [Easy Reload Guard] 日志，以及排查关闭 Domain Reload 后才出现的问题。工程的 Packages/manifest.json 中没有 com.yongshi.easyreloadguard 时不使用。"
---

# Easy Reload Guard 约束

关闭 Domain Reload 后，进入 Play 不再重新加载脚本，静态状态会带到下一次 Play。本包在 Editor 中按声明恢复静态状态、在停止 Play 时调用释放方法，并检查遗漏。写代码时要让每个新的静态状态都有明确的处理方式，同时**不改变打包后的游戏逻辑**。

## 开始前

1. 确认 `Packages/manifest.json` 有 `com.yongshi.easyreloadguard`。
2. 找到实际安装的包目录：`Packages/com.yongshi.easyreloadguard/`（嵌入）或 `Library/PackageCache/com.yongshi.easyreloadguard@*/`。以该目录中的 `README.md`、`Documentation~/接入手册.md`、`AGENTS.md` 为准；本技能与包版本不一致时，以包内文档为准。
3. 看工程已有的写法：单例基类、事件中心、Editor 适配器所在目录（搜索 `: ReloadAdapter`）。优先沿用，不新建第二套。
4. 分清这次是哪种工作，适用的规则不同：

| 工作 | 适用的规则 |
| --- | --- |
| 写新代码，或重写自己负责的代码 | “始终适用”+“写新代码：编码规范”。按规范一次写对，目标是不产生新的检查问题 |
| 给已有代码接入本包：处理检查报告、停止后检查报告，排查关闭 Domain Reload 后才出现的问题 | “始终适用”+“接入已有代码：改动边界”。尽量少改业务 |

## 始终适用

1. **不影响打包后的逻辑。** 本包在业务代码中只以特性、`#if UNITY_EDITOR` 中的 `[ResetMethod]` 重置方法和 `EasyReloadGuard` 程序集引用（asmdef 需手动添加）的形式出现。运行时流程中不调用本包接口（`PlaySession` 上的方法只给 Editor 工具和调试代码用），不为关闭 Domain Reload 加判断分支或 `#if UNITY_EDITOR` 的流程差异，不调整业务的初始化阶段。
2. **释放和取消是业务自己的事。** 订阅、线程、计时器、Socket、下载、原生资源在打包后切场景、登出时同样要释放；异步任务不能比发起它的对象活得更久。本包不替业务释放或取消。
3. **`[OnExitPlayMode]` 只加在业务流程本身也会调用的停止方法上。** 不写只在停止 Play 时才被调用的方法。

## 写新代码：编码规范

目标：静态字段回到脚本刚加载时的值，检查没有不符合项，不依靠暂缓清单。这些规范也是打包后登出重进、切场景、重复初始化的正确写法，所以写新代码时可以按需设计初始化、停止、取消等业务方法，不受“接入已有代码”的改动限制。

1. **先不用静态。** Play 中产生的状态放在负责它的对象实例上（MonoBehaviour，或由启动流程创建并持有的服务），随对象创建和销毁。静态只留给常量、内容不变的计算缓存，以及工程统一的单例和事件中心入口。
2. **声明时就处理。** 每个可变静态成员写下时就按下表选定处理方式并加上特性，不留没有标注的可变静态；能写成 `const`、`static readonly` 的就写。
3. **初始化可以重复执行。** 每次 Play 都要做的初始化放在 `Awake`、`Start` 或业务的 `Init` 中，不放在静态构造或字段初值中（关闭 Domain Reload 时只执行一次）。`Init` 重复调用不会重复订阅、重复创建；“已初始化”标记要加 `[ResetStatic]`。
4. **订阅成对。** 由订阅者在对应退出边界解除；不限制 += / -= 的代码形状，先 -= 再 += 也可以。不在静态构造或 `[InitializeOnLoad]` 中订阅运行时事件。
5. **资源有负责者和停止方法。** 线程、计时器、Socket、下载、原生资源由一个对象创建，并由它的 `Stop`、`Dispose` 或 `Shutdown` 释放。停止方法可以重复调用，由业务的退出流程（销毁、切场景、登出、`Application.quitting`）调用；静态入口的服务给这个停止方法加 `[OnExitPlayMode]`。
6. **异步由发起者取消。** 发起时取得令牌（`destroyCancellationToken`、服务自己的 `CancellationTokenSource`、`Application.exitCancellationToken`）并沿调用链传递；`await` 之后写状态前检查令牌；不用 `async void`。`Task`、`CancellationTokenSource` 不放在静态字段中，放在负责的实例上，由它的停止方法取消。
7. **单例沿用工程的基类。** `Instance` 在 `Awake` 中赋值、在 `OnDestroy` 中是自己时置 null，字段加 `[ResetStatic]`。纯 C# 单例由启动流程创建，并配有停止方法。
8. **缓存也要恢复初始状态。** 运行中填充的纯计算缓存也重置。ReloadSafe 只说明检查器能证明初始化后不变的字段，不能凭“保留不出错”放行。私有集合需元素不可变、无别名或传参逃逸、每次使用在只读白名单内；无法证明则重置。
9. **ScriptableObject 不存运行时状态。** 不把 Play 中的数据写回资产实例；需要运行时状态时用 `Instantiate` 出的副本或单独的运行时对象。资产的 `OnEnable` 只在加载时执行一次，不能当作每次 Play 的初始化。
10. **Editor 代码只在脚本加载时订阅。** `EditorApplication` 等编辑器事件在 `[InitializeOnLoad]` 的静态构造中订阅一次；在每次进入 Play 都会执行的代码中订阅，处理器会越积越多。

业务 SubsystemRegistration 自行初始化不豁免字段；InitializeOnEnterPlayMode 方法必须同时标 ResetMethod（或用适配器 InvokeOnReset），字段重置后再执行一次，必须可重复调用。ResetMethod 需覆盖所有不能证明不变的字段。

适配器只声明处理方式：HandlesType 已删除，改用 ResetField / ResetType / ResetGenericField；SelfHandled 仅限 Unity 官方来源，Git/嵌入来源和 DLL 不满足。ResetType 在 0.5.0 仅声明处理责任，实际结果需运行验收；自动一致性比对尚未实现。

完整的服务写法见 [references/patterns.md](references/patterns.md) 第 2 节“新写一个服务”。

## 每个静态状态选一种处理

写新代码和接入已有代码都按下表选择，详细判断见 [references/patterns.md](references/patterns.md)。

| 静态状态 | 处理 |
| --- | --- |
| 值永远不变 | `const` 或 `static readonly`，不需要特性 |
| 初值为 default 的单例、普通字段 | `[ResetStatic]`；有非默认初值的数值或 string 用 `[ResetStatic(ResetMode.InitialValue)]` |
| 只在运行中增删的 `static readonly` 集合 | `[ResetStatic(ResetMode.Clear)]` |
| 静态事件、自动属性 | `[field: ResetStatic]`，前提是订阅方已在销毁或退出时成对取消 |
| 几个字段要一起恢复，或需要恢复初始内容 | `#if UNITY_EDITOR` 中的 `[ResetMethod]` 静态无参方法，只恢复本类型的静态字段 |
| 检查器能证明不变的字段 | 不需特性，可 `[ReloadSafe("原因")]` 说明；证明不了即不符合，不能暂缓 |
| 订阅、线程、计时器、Socket、原生资源 | 业务自己的停止方法成对释放，再加 `[OnExitPlayMode]` |
| 异步任务 | `destroyCancellationToken`、服务自己的 `CancellationTokenSource` 或 `Application.exitCancellationToken`；`await` 之后写状态前检查令牌；不用 `async void` |
| 泛型静态字段 | `[ResetStatic]` 按派生类和单参数接口/基类约束展开；否则 `ResetGenericField` 提供类型实参或对闭合类型 `ResetField` |
| 第三方或外部官方包的静态状态 | 不改源码，写 Editor 适配器；公司内部维护的包改包源码，先与用户确认 |

常见错误：

- 对只在静态构造或字段初值中填充的表用 `Clear`：静态构造不会再执行，清空后永久为空。
- 对只有初值、没有地方重建的引用用 `[ResetStatic]`：下一次 Play 拿到 null。
- `static float s = 1f` 加 `[ResetStatic]`：会被重置成 0，要用 `InitialValue`。
- 只把单例或线程字段置 null：已在运行的工作不会停，监听也还在别处的表里。
- 用类型级、程序集级 `[ReloadSafe]`，或整批记为暂缓，来掩盖没确认过的问题。
- 手改生成代码：改生成模板或生成器。

## 接入已有代码：改动边界

已有代码以“少改业务、不改流程”为准：

1. 业务代码只允许：
   - 在**已有**成员上加特性：`[ResetStatic]`、`[ResetMethod]`、`[OnExitPlayMode]`、`[ReloadSafe("原因")]`，以及为此需要的 `EasyReloadGuard` 程序集引用。
   - 新增**只在 Editor 编译**的重置方法：方法连同 `[ResetMethod]` 整体放在 `#if UNITY_EDITOR` 中。
2. 不为本包在业务中新增方法、Unity 消息、参数、判断分支或登记调用（如 `PlaySession.RegisterReset`）。
3. 订阅不成对、缺少释放、`Init` 不去重是业务缺陷，不是接入问题：先确认开启 Domain Reload 时、打包后登出重进时也会出现，向用户说明后修业务本身；释放方法存在之后，才在它上面加 `[OnExitPlayMode]`。
4. 不借接入把旧代码顺手改写成新规范；值得重构的地方单独提出，由用户决定。

## 排查“关闭 Domain Reload 后才出错”

1. 先用开启 Domain Reload、相同的操作复现一次。开启时也会出现，就是原有问题，不归因于本包或 Domain Reload，只记录，不修。
2. 只有关闭时才出现的，再按上表处理；能用 Editor 适配器把静态状态恢复成脚本刚加载时的样子，也算处理完成。
3. 涉及业务逻辑修改或包源码修改时，先向用户说明现象、原因、证据和方案，等确认后再改。

处理检查报告中的具体问题，见 [references/report-triage.md](references/report-triage.md)。

## 验证

先遵守当前用户和项目的验证授权；下面是验收证据，不因启用技能而自动获准编译、刷新、测试或 Play。分别报告结果，没做的写“未验证”：

1. 编译通过，Console 没有 `[Easy Reload Guard]` 错误。
2. 运行检查（菜单 `Tools/Easy Reload Guard/检查静态变量并查看结果`，或命令行 `-executeMethod EasyReloadGuard.Editor.ReloadGuardCli.Audit`）：不符合项为 0、暂缓为 0。特性用错、重置初值不匹配、覆盖遗漏、ReloadSafe 证明失败等不能暂缓。建议不计入结论。
3. 关闭 Domain Reload、保持 Scene Reload 开启，在同一次脚本加载内连续 Play → Stop 至少 3 轮，期间不改代码、不重新编译。看 `Library/EasyReloadGuard/ResidueReport.md`：没有“仍引用已销毁的对象”，没有“比上次停止时增加”。
4. 走到改动涉及的入口（登出重进、加载中途停止、下载中停止等）。主流程通过不代表这些入口通过。

用脚本切换 Domain Reload 时，要用 `SerializedObject` 同时写入 `ProjectSettings/EditorSettings.asset` 的 `m_EnterPlayModeOptionsEnabled`，结束后清除脏标记：Unity 6000.3 实际按这个字段决定是否重新加载脚本，只改 `EditorSettings` 接口不生效。

## 不要做

- 升级 0.5.0 按迁移要求清空旧清单，再运行新检查；常规处理不要手工添加 `ProjectSettings/EasyReloadGuardBaseline.txt` 条目；需要更新时，确认问题后用设置页的“把当前问题列为暂缓”重新生成。
- 不要为了让检查通过而排除包或程序集；只在自己本机安装、不随项目提交的插件，在插件目录放 `.easyreloadguard-exclude` 标记。
- 不要关闭 Scene Reload，也不要修改用户的 Enter Play Mode 设置并保存。

## 给人看的输出

报告、日志、文档、提交说明都写给同事看：用完整的问题描述，不用规则编号（如 RG003）、“基线”、“会话”、“owner”、“PSP”等内部代称。暂缓清单文件中的编号对照见 [references/report-triage.md](references/report-triage.md)，只在读那个文件时使用。
