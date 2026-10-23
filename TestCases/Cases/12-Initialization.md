# 初始化顺序与 Editor 生命周期

补全日期：2026-10-23。目标环境：Unity 6000.3.8f1 / Windows Editor。

本案例已补 **4 个实际自动测试方法**，每个均以 R1（Domain Reload 关闭、Scene Reload 开启）连续进入/退出 Play 3 轮。**本次未编译、未运行测试；以下是实现与通过标准，不是通过结果。** 真正脚本重编译另有被动观察工具和人工步骤，不计入自动测试数量。

## 类型、包名与源码路径

- 类型：本地源码案例，覆盖 Unity 运行时初始化与编辑器事件；不是第三方完整项目的接入验证。
- 初始化顺序适配包：`com.yongshi.easyreloadguard@0.1.0`，版本来源：[package.json](../../Packages/com.yongshi.easyreloadguard/package.json)。
- 运行时探针：[InitializationOrderCase.cs](../../Assets/DomainReloadCases/Runtime/InitializationOrderCase.cs)。记录实际 RuntimeInitialize 回调，在 `BeforeSceneLoad` 内创建案例对象，由 Unity 调用其 `Awake`。
- 初始化自动测试：[InitializationOrderTests.cs](../../Assets/DomainReloadCases/Tests/Editor/InitializationOrderTests.cs)，分类 `Case.Initialization`。
- Editor 探针、被动观察菜单与自动测试：[EditorLifecycleTests.cs](../../Assets/DomainReloadCases/Tests/Editor/EditorLifecycleTests.cs)，分类 `Case.EditorLifecycle`。
- 设置保存/恢复：[ReloadCaseTestBase.cs](../../Assets/DomainReloadCases/Tests/Editor/ReloadCaseTestBase.cs)。执行时保存并恢复原 Enter Play Mode 选项、后台运行选项和原设置资源脏状态；不改包的全局策略。

运行时探针通过 `Arm/Disarm` 限定观测范围；未启用时不会创建业务对象。Editor 代码位于 Editor 测试程序集，不让运行时代码引用 `UnityEditor`。

## 适配方案与顺序边界

业务 A 依赖 B，使用包的 `[ResetMethod(-200)]` 初始化 B，再使用 `[ResetMethod(-100)]` 初始化 A。当前包在字段重置完成后按 `Order` 升序调用这些方法，具体声明和实现见 [ResetAttributes.cs](../../Packages/com.yongshi.easyreloadguard/Runtime/ResetAttributes.cs)、[ResetPipeline.cs](../../Packages/com.yongshi.easyreloadguard/Editor/Core/ResetPipeline.cs)。测试期待 B 的值为 42，A 读取就绪 B 后得到 43，且这一过程先于业务 `Awake`。

**同一个 `RuntimeInitializeLoadType` 阶段里的不同方法没有可依赖的相对顺序。** 案例不拿方法名、声明顺序、脚本加载顺序或偶然观测到的先后来组织 A/B 依赖。本地探针与包入口都在 `SubsystemRegistration` 时，不假定二者谁先执行；阶段记录与依赖记录分开管理。真实业务也可以使用一个统一入口按 B→A 调用，不能依赖两个无明确顺序的同阶段回调。

Editor 工具采用 Unity 原生 `[InitializeOnEnterPlayMode]` 作为每局入口：清理本工具的临时缓存，保留工具偏好；进入时登记 `EditorApplication.update`，退出时经真实 `playModeStateChanged` 退订。这些 Editor 探针位于测试程序集，不假设包会发现其中的 `ResetMethod`：当前包主动排除测试程序集。

本案例不需要改包源码。需要改动或接入业务时，目标是上列本地 `InitializationOrderCase.cs` 与 Editor 工具自身的生命周期代码。

## 自动测试映射

以下 4 项本次结果均为 **未编译、未执行**。

| 矩阵 ID | 实际测试方法 | 步骤与测试要点 | 通过标准 |
| --- | --- | --- | --- |
| INIT-01 | `InitializationOrderTests.RuntimePhasesReinitializeWhileStaticConstructorDoesNotRerun` | 启用探针，保存本域标识和静态构造赋值；R1 连续 3 轮。每轮检查阶段记录与业务 `Awake` 读数，然后改写构造状态、污染业务状态再退出。 | 3 轮域标识相同，静态构造计数始终为 1；被改写且未重置的构造状态跨轮保留，不能误认静态构造每局重跑；每局初始化计数为 1/2/3，业务 `Awake` 每轮读到重建值 42。阶段记录符合本案例下述预期。 |
| INIT-02 | `InitializationOrderTests.OrderedResetInitializesDependencyBeforeConsumerEverySession` | 启用探针；R1 连续 3 轮，每轮由包实际调用带 Order 的重置方法，观察 B→A 记录与业务 `Awake`，随后污染依赖状态。 | 每轮依赖记录严格为 B、A；A 确认 B 已就绪且结果为 43；真实业务 `Awake` 看到依赖均就绪。不能靠人工调用重置方法得到通过。 |
| IN-03 | `EditorLifecycleTests.EditorSessionEntryResetsCacheAndExitReleasesSubscription` | 启用 Editor 探针并设置案例偏好；每轮 Play 前将临时缓存置为非零。R1 连续 3 轮，进入后等待本轮真实 `EditorApplication.update` 计数增长；退出后再观察 3 次测试迭代。 | 每局 `InitializeOnEnterPlayMode` 清零临时缓存，进入次数为 1/2/3；`InitializeOnLoad` 计数不增长；案例偏好值始终保留；运行中真实 update 被观测到，退出计数为 1/2/3，退订后 update 计数不再增长。 |
| IN-04，普通 Play 部分 | `EditorLifecycleTests.FastPlayTransitionDoesNotRaiseAssemblyReloadEvents` | 登记 Play 状态和程序集重载观察器；R1 连续 3 轮普通 Play→Stop，不改脚本、不请求编译或重载。 | Enter Play 与 Exiting Play 计数均为 3；`beforeAssemblyReload` 和 `afterAssemblyReload` 计数均为 0。此通过只证明普通 R1 切换不是实际程序集重载。 |

INIT-01 的代码断言阶段记录为：

```text
SubsystemRegistration
AfterAssembliesLoaded
BeforeSplashScreen
BeforeSceneLoad
Awake
AfterSceneLoad
```

这里的 `Awake` 属于**在本案例 `BeforeSceneLoad` 回调内创建的业务对象**；不能把这个序列推广成任意场景对象或同阶段任意方法的总顺序。阶段断言尚未在目标编辑器执行，执行后应保留真实记录；版本或编辑器行为有差异时应独立记录，不能补写不存在的回调。

IN-03 的“偏好”是案例自有的静态值，验证状态所有权的区分；未覆盖真实 EditorWindow 的序列化恢复、EditorPrefs 持久化或所有编辑器工具实现。事件来自 Unity，测试不手工调用生命周期函数冒充编辑器执行。

## IN-04：真实脚本重编译的人工步骤

这是独立的手工补充，**没有注册为自动化测试，也没有执行结果**。被动观察菜单位于 `Tools/Domain Reload Cases/IN-04 Assembly Reload/`，由 Editor 测试程序集提供：

| 菜单 | 实际方法 | 行为 |
| --- | --- | --- |
| `1. 开始被动观察` | `EditorLifecycleCaseProbe.ArmManualAssemblyObservation` | 清零案例计数、启用被动事件订阅；使用 `SessionState` 保存观察开关和跨域计数。 |
| `2. 查看观察计数` | `EditorLifecycleCaseProbe.ReportManualAssemblyObservation` | 输出当前开关及真实 `beforeAssemblyReload`、`afterAssemblyReload` 计数；仅显示观察值，不自动判通过。 |
| `3. 结束观察并清理计数` | `EditorLifecycleCaseProbe.StopManualAssemblyObservation` | 解除本工具订阅并清除案例 `SessionState` 记录。 |

**上述菜单均不触发编译、不请求域重载、不改 Play 设置、不修改脚本。** 观察开关开启后，实际域重载时由 Editor 类初始化重新登记后续事件观察器。

人工执行时按下列步骤分别记录两类事件：

1. 保存当前工作，记录 Unity/包版本和原 Enter Play Mode 选项；设置为 R1。确认退出 Play 且当前没有待处理编译。
2. 点击“1. 开始被动观察”，普通 Play→Stop 后点击“2. 查看观察计数”。预期 `beforeAssemblyReload=0`、`afterAssemblyReload=0`。
3. 保持退出 Play。在一个自有脚本作可恢复的注释修改并保存，由操作人员触发真实脚本编译，等待编译及域重载完成；不要用普通 Play 切换代替这一步。
4. 再点击“2. 查看观察计数”。预期 `beforeAssemblyReload` 与 `afterAssemblyReload` 各增加至少 1 且成对；记录编译次数和实际计数。发生多轮编译时不能强行期待恰好 1/1。
5. 恢复注释修改与原 Play 设置，待必要编译结束后点击“3. 结束观察并清理计数”。恢复源码可能额外引起编译，应与第 4 步的观测分开记录。

只有普通 R1 对照和真正重编译两部分均取得实际证据，才可把 IN-04 的完整人工验证记为通过。若编译失败或没有观察到成对事件，记录失败/待排障；不能因为菜单可见或脚本已保存就认定重载发生。

## 尚未覆盖与结果记录

- R0、R2、R3 尚未由这 4 个方法覆盖；原生阶段探针的启用状态是域内 static，不能假称它在 R0 自动保持。
- INIT-02 当前覆盖显式依赖顺序和跨局重建，未覆盖同局重复初始化时的进行中任务保留；该问题仍需结合 UT-06 独立实现。
- IN-04 真正重编译未自动化；本次没有发起任何编译或域重载。
- 未覆盖 Player、IL2CPP、多线程初始化竞争、全部 EditorWindow 或第三方初始化器。

后续执行时分别填写矩阵 ID、Unity/包版本、R 模式、方法或人工步骤、每轮观测值、实际结果及原始记录路径。当前可填写的实际状态仅为：**代码和通过标准已补齐；未编译、未运行，兼容性未确认。**
